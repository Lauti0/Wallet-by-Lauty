<#
  Genera el instalador de Cholinanzas: installer\output\Cholinanzas-Setup-<versión>.exe

  Uso (desde cualquier carpeta):   powershell -ExecutionPolicy Bypass -File installer\build.ps1

  Hace dos cosas:
    1. Publica la app "autocontenida" (incluye .NET): quien la instala no necesita instalar nada más.
    2. Compila el instalador con Inno Setup 6. Si no lo tenés instalado, baja una copia portable del
       compilador a %TEMP%\ch-inno (no instala nada en Windows).
#>
param(
    [string]$InnoPath  # ruta a ISCC.exe, si querés usar una específica
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'CholinanzasApp.csproj'
$publishDir = Join-Path $PSScriptRoot 'publish'
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No encontré <Version> en $proj" }

function Find-Iscc {
    if ($InnoPath -and (Test-Path $InnoPath)) { return $InnoPath }
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:TEMP\ch-inno\pkg\tools\ISCC.exe")
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

    Write-Host 'No tenés Inno Setup: bajo una copia portable del compilador (solo para armar el instalador)...'
    $cache = Join-Path $env:TEMP 'ch-inno'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    # un .nupkg es un zip, pero Expand-Archive solo acepta la extensión .zip
    $pkg = Join-Path $cache 'inno.zip'
    Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/tools.innosetup/6.4.3/tools.innosetup.6.4.3.nupkg' -OutFile $pkg
    Expand-Archive $pkg -DestinationPath (Join-Path $cache 'pkg') -Force
    return (Join-Path $cache 'pkg\tools\ISCC.exe')
}

Write-Host "== Publicando Cholinanzas $version (autocontenida, win-x64) =="
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $publishDir `
    -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Falló dotnet publish' }

Write-Host '== Compilando el instalador =='
$iscc = Find-Iscc
& $iscc "/DAppVersion=$version" "/DSourceDir=$publishDir" (Join-Path $PSScriptRoot 'Cholinanzas.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falló Inno Setup' }

$setup = Join-Path $PSScriptRoot "output\Cholinanzas-Setup-$version.exe"
$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "Listo: $setup ($mb MB)"
Write-Host 'SHA-256: ' (Get-FileHash $setup -Algorithm SHA256).Hash
