; Instalador de Cholinanzas (Inno Setup 6). No se compila a mano: usar installer\build.ps1,
; que publica la app y le pasa la versión y la carpeta de origen.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #error Falta SourceDir: compilá con installer\build.ps1
#endif

#define AppName "Cholinanzas"
#define AppExe  "CholinanzasApp.exe"

[Setup]
; Este GUID identifica la app para que una versión nueva se instale encima de la anterior. No cambiarlo nunca.
AppId={{6F2C7B1E-4A3D-4D8E-9B57-C1E5A0D93F42}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
; Instalación solo para el usuario actual: no pide permisos de administrador, y es coherente con que los
; datos viven en la carpeta de usuario.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=output
OutputBaseFilename=Cholinanzas-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
; si la app está abierta, ofrece cerrarla y volver a abrirla
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Los datos del usuario (movimientos, respaldos, configuración) viven en %LOCALAPPDATA%\CholinanzasApp,
// fuera de la carpeta del programa. Instalar una versión nueva o desinstalar NO los toca, salvo que el usuario
// lo pida expresamente al desinstalar.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  DataDir := ExpandConstant('{localappdata}\CholinanzasApp');
  if not DirExists(DataDir) then Exit;
  // en una desinstalación silenciosa nunca se borran los datos
  if UninstallSilent then Exit;
  if MsgBox('¿Querés borrar también tus datos (movimientos, cuentas y respaldos)?' + #13#10 + #13#10 +
            'Si elegís "No", la app se desinstala pero tus datos quedan guardados en:' + #13#10 + DataDir + #13#10 + #13#10 +
            'Si elegís "Sí", se borran para siempre y no se pueden recuperar.',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataDir, True, True, True);
end;
