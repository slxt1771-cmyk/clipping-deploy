; Inno Setup script for Clipping Software.
;
; Produces the single Setup.exe a user downloads and double-clicks. Compiled by the release workflow
; (.github/workflows/release.yml), which publishes the app first and passes the paths in via /D switches:
;
;   iscc /DSourceDir=<published app folder> /DAppVersion=<version> /DOutputDir=<where to write Setup.exe> ClippingSoftware.iss
;
; Both defines have fallbacks below so the script also compiles from a local checkout after a manual
; `dotnet publish`, which is the only way to smoke-test installer changes without pushing.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Clipping Software"
#define AppExeName "ClippingSoftware.App.exe"
#define AppPublisher "Clipping Software"

[Setup]
; A fixed GUID is what lets a later version recognise and upgrade this install rather than landing
; beside it as a second copy. Never change it between releases.
AppId={{8F3A6C21-4D7E-4B95-9A18-2E6C5B1D74F0}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir={#OutputDir}
OutputBaseFilename=ClippingSoftware-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; x64 only: the app publishes as win-x64 self-contained, and OBS itself is 64-bit.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-machine install (Program Files) needs elevation; the installer prompts for it once.
PrivilegesRequired=admin
DisableProgramGroupPage=yes
LicenseFile=
; Show what's about to happen before it happens - this installs ~200MB (self-contained runtime plus
; ffmpeg), which is worth stating rather than surprising someone mid-copy.
DisableReadyPage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startupicon"; Description: "Start {#AppName} automatically when I sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; The whole published output, including the assets\ and tools\ trees the app resolves beside its exe
; (see BundledResources) - recursesubdirs is what carries those, not just the top-level DLLs.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
; Startup shortcut rather than a Run registry value: it shows up in Task Manager's Startup tab where a
; user can turn it off themselves, which a bare registry entry doesn't.
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Files the app writes next to itself at runtime would otherwise be left behind and block the
; directory's removal. The user's clips and settings live elsewhere (Videos\ and %LocalAppData%) and are
; deliberately NOT touched - uninstalling the app should never delete someone's recordings.
Type: filesandordirs; Name: "{app}\runtimes"
Type: dirifempty; Name: "{app}"

[Code]
{
  Blocks installing on a machine without OBS Studio, but only as a warning the user can override:
  they may be about to install OBS, or have it somewhere this check doesn't look (a portable copy).
  The app's own first-run wizard does the authoritative detection and lets them browse to it, so this
  is an early heads-up rather than a gate.
}
function IsObsInstalled(): Boolean;
var
  Paths: array[0..1] of String;
  I: Integer;
begin
  Paths[0] := ExpandConstant('{commonpf64}\obs-studio\bin\64bit\obs64.exe');
  Paths[1] := ExpandConstant('{commonpf32}\obs-studio\bin\64bit\obs64.exe');

  Result := False;
  for I := 0 to High(Paths) do
  begin
    if FileExists(Paths[I]) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

  if not IsObsInstalled() then
  begin
    Result := SuppressibleMsgBox(
      'OBS Studio was not found on this PC.' + #13#10#13#10 +
      'Clipping Software controls OBS to do the actual recording, so you will need OBS Studio 28 or ' +
      'newer installed before it can capture anything. You can get it free from obsproject.com.' + #13#10#13#10 +
      'Continue with the installation anyway?',
      mbConfirmation, MB_YESNO, IDYES) = IDYES;
  end;
end;
