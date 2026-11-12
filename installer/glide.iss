; Glide installer (Inno Setup 6). Built by .github/workflows/package-installer.yml:
;   iscc /DAppVersion=1.0.1 /DArch=x64 /DSourceDir=<unzipped Glide folder> /O<out> installer\glide.iss
;
; Glide (S-01): installs per machine into Program Files, which ordinary programs can't write to, so
; nothing that runs as administrator (TUN, the updater) can be swapped by them. The marker file
; "glide-installed" tells Glide it is installed: its settings, logs and geo data then live in
; %LOCALAPPDATA%\Glide, and on first start it copies the settings of an old per-user install
; (%LOCALAPPDATA%\Programs\Glide), which is left in place for the user to uninstall.
; Uninstalling removes the program files only; the data in %LOCALAPPDATA%\Glide stays.

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
; Same AppId as the per-user installs (changing it is R-16, not approved). Per-machine installs
; register under HKLM, so they don't see or overwrite the old HKCU registration.
AppId={{6E0B7C3A-4F1D-4B8E-9A52-2C9D3E7F1A64}
AppName=Glide
AppVersion={#AppVersion}
AppPublisher=BlackkCore
AppPublisherURL=https://github.com/BlackkCore/Glide
AppSupportURL=https://github.com/BlackkCore/Glide/issues
AppUpdatesURL=https://github.com/BlackkCore/Glide/releases
DefaultDirName={autopf}\Glide
; S-01: the installed model is only safe in a folder ordinary programs can't write to, so the wizard
; doesn't offer another one. UsePreviousAppDir=no keeps Setup from ever reusing a folder recorded by an old
; per-user install (%LOCALAPPDATA%\Programs\Glide, user-writable). /DIR= is refused outside Program Files (see [Code]):
// a CI run installed into C:\GlideCustom, where every user may write, and the installed marker then made Glide
// run its cores from there.
DisableDirPage=yes
UsePreviousAppDir=no
DefaultGroupName=Glide
DisableProgramGroupPage=yes
PrivilegesRequired=admin
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
Source: "glide-installed"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Glide"; Filename: "{app}\Glide.exe"
Name: "{group}\{cm:UninstallProgram,Glide}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Glide"; Filename: "{app}\Glide.exe"; Tasks: desktopicon

[UninstallDelete]
// Left behind by in-app updates (the updater renames itself before replacing).
Type: files; Name: "{app}\*.tmp"

[Run]
// Started as the user who ran setup, not with setup's administrator rights.
Filename: "{app}\Glide.exe"; Description: "{cm:LaunchProgram,Glide}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
// S-01: the installed model is only safe in a folder ordinary programs cannot write to. The wizard has no folder page,
// but /DIR= (silent installs, scripts) could still point anywhere; refuse anything outside Program Files.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Dir, ProgramFiles: String;
begin
  Result := '';
  Dir := AddBackslash(ExpandConstant('{app}'));
  ProgramFiles := AddBackslash(ExpandConstant('{autopf}'));
  if CompareText(Copy(Dir, 1, Length(ProgramFiles)), ProgramFiles) <> 0 then
    Result := 'Glide must be installed inside ' + ProgramFiles + ' (a folder ordinary programs cannot write to). Setup was stopped: ' + Dir;
end;
