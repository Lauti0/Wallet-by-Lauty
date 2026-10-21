<#
  Genera Legal\AVISOS-DE-TERCEROS.txt: la lista de las librerías de terceros que lleva Cholinanzas, con su licencia y su
  copyright, más el texto de esas licencias. Las licencias MIT y Apache-2.0 exigen incluir estos avisos cuando se reparte
  el programa (el instalador los instala junto a la app y la app los muestra en la pestaña Ayuda).

  Volver a correrlo cada vez que se agregue o actualice un paquete:
      powershell -ExecutionPolicy Bypass -File installer\generate-notices.ps1

  Lee los datos de cada paquete en su .nuspec (carpeta de caché de NuGet): no hay que tipear nada a mano. Si aparece una
  licencia distinta de MIT o Apache-2.0, el script avisa y hay que revisarla (con un abogado, si hace falta) antes de repartir.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'CholinanzasApp.csproj'
$nuget = Join-Path $env:USERPROFILE '.nuget\packages'
$out = Join-Path $root 'Legal\AVISOS-DE-TERCEROS.txt'
$apache = Join-Path $root 'Legal\licenses\Apache-2.0.txt'

# restaurar para que los paquetes (y las bibliotecas de .NET que se incluyen) estén en la caché
dotnet restore $proj -r win-x64 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Falló dotnet restore' }

$lines = dotnet list $proj package --include-transitive | Where-Object { $_ -match '^\s*>\s' }
$packages = foreach ($line in $lines) {
    $parts = ($line -replace '^\s*>\s*', '').Trim() -split '\s+'
    [pscustomobject]@{ Id = $parts[0]; Version = $parts[-1] }
}
$packages = $packages | Sort-Object Id -Unique

$known = @('MIT', 'Apache-2.0')
$text = New-Object System.Text.StringBuilder
function Add([string]$s = '') { [void]$text.AppendLine($s) }

Add 'AVISOS DE SOFTWARE DE TERCEROS'
Add '=============================='
Add ''
Add 'Cholinanzas incluye software de terceros. Cada uno se usa bajo su propia licencia, y estas licencias piden que'
Add 'se reproduzcan sus avisos de copyright junto con el programa. Eso es lo que hace este archivo.'
Add 'Este archivo se genera automáticamente con installer\generate-notices.ps1.'
Add ''
Add '1. COMPONENTES'
Add '-------------'
Add ''

$warnings = @()
foreach ($p in $packages) {
    $id = $p.Id.ToLowerInvariant()
    $spec = Join-Path $nuget "$id\$($p.Version)\$id.nuspec"
    if (-not (Test-Path $spec)) { $warnings += "Sin .nuspec: $($p.Id) $($p.Version)"; continue }
    $meta = ([xml](Get-Content $spec -Raw)).package.metadata
    $license = if ($meta.license) { [string]$meta.license.'#text' } else { 'DESCONOCIDA' }
    if ($known -notcontains $license) { $warnings += "Licencia a revisar: $($p.Id) $($p.Version) -> $license" }
    $copyright = if ($meta.copyright) { ([string]$meta.copyright).Trim() } else { "(sin aviso propio; autores: $($meta.authors))" }
    Add "$($p.Id) $($p.Version)"
    Add "    Licencia: $license"
    Add "    Copyright: $copyright"
    Add ''
}

# el entorno de ejecución de .NET y WPF que lleva la app (es "autocontenida")
$runtimeDir = Get-ChildItem (Join-Path $nuget 'microsoft.netcore.app.runtime.win-x64') -Directory |
    Sort-Object { [version]($_.Name -replace '[-+].*$', '') } -Descending | Select-Object -First 1
Add ".NET (entorno de ejecución) y Windows Desktop / WPF $($runtimeDir.Name)"
Add '    Licencia: MIT'
Add '    Copyright: (c) .NET Foundation and Contributors'
Add ''
Add 'SQLite (dentro de SQLitePCLRaw.lib.e_sqlite3)'
Add '    SQLite es de dominio público: https://www.sqlite.org/copyright.html'
Add ''

Add ''
Add '2. LICENCIA MIT'
Add '---------------'
Add 'Aplica a los componentes marcados "MIT", cada uno con el copyright indicado arriba.'
Add ''
Add (Get-Content (Join-Path $runtimeDir.FullName 'LICENSE.TXT') -Raw).TrimEnd()
Add ''
Add ''
Add '3. LICENCIA APACHE 2.0'
Add '----------------------'
Add 'Aplica a los componentes marcados "Apache-2.0", cada uno con el copyright indicado arriba.'
Add ''
Add (Get-Content $apache -Raw).TrimEnd()
Add ''
Add ''
Add '4. AVISOS ADICIONALES DE MICROSOFT (.NET)'
Add '----------------------------------------'
Add 'Microsoft incluye, junto a .NET, los avisos de otros componentes de terceros que usa. Se reproducen completos:'
Add ''
Add (Get-Content (Join-Path $runtimeDir.FullName 'THIRD-PARTY-NOTICES.TXT') -Raw).TrimEnd()
Add ''

# UTF-8 con marca (BOM): la lee bien el instalador y la app
[System.IO.File]::WriteAllText($out, $text.ToString(), (New-Object System.Text.UTF8Encoding($true)))

Write-Host "Listo: $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB, $($packages.Count) paquetes)"
foreach ($w in $warnings) { Write-Warning $w }
if ($warnings.Count -gt 0) { exit 1 }
