; Glide installer (Inno Setup 6). Built by .github/workflows/package-installer.yml:
;   iscc /DAppVersion=1.0.1 /DArch=x64 /DSourceDir=<unzipped Glide folder> /O<out> installer\glide.iss
;
; Installs per user into %LOCALAPPDATA%\Programs\Glide: no admin prompt, and the folder stays
; writable, which Glide needs for its settings (guiConfigs) and in-app updates. Uninstalling removes
; the program files only; guiConfigs (servers, settings) stays, so a reinstall keeps everything.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\out"
#endif

[Setup]
AppId={{6E0B7C3A-4F1D-4B8E-9A52-2C9D3E7F1A64}
AppName=Glide
AppVersion={#AppVersion}
AppPublisher=BlackkCore
AppPublisherURL=https://github.com/BlackkCore/Glide
AppSupportURL=https://github.com/BlackkCore/Glide/issues
AppUpdatesURL=https://github.com/BlackkCore/Glide/releases
DefaultDirName={localappdata}\Programs\Glide
DefaultGroupName=Glide
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=Glide-setup-{#Arch}
SetupIconFile=glide.ico
UninstallDisplayIcon={app}\Glide.exe
UninstallDisplayName=Glide
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Glide"; Filename: "{app}\Glide.exe"
Name: "{group}\{cm:UninstallProgram,Glide}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Glide"; Filename: "{app}\Glide.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Glide.exe"; Description: "{cm:LaunchProgram,Glide}"; Flags: nowait postinstall skipifsilent
