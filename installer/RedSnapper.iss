; Builds RedSnapper-Setup.exe. See installer\build.ps1.
#define AppName "Red Snapper"
#define AppVersion "1.0.0"

[Setup]
AppId={{66B5AED6-E7CC-4CBF-BF5D-DB6C926C5669}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Ann Layman
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=RedSnapper-Setup
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Files]
Source: "publish\*"; DestDir: "{app}"; Excludes: "RedSnapper.exe,*.pdb"; Flags: ignoreversion recursesubdirs
Source: "..\app.ico"; DestDir: "{app}"; Flags: ignoreversion

; The app is launched through dotnet.exe because the unsigned RedSnapper.exe is blocked by some security policies.
[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{code:DotnetExe}"; Parameters: """{app}\RedSnapper.dll"""; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{code:DotnetExe}"; Parameters: """{app}\RedSnapper.dll"""; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start {#AppName} when I sign in to Windows"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{code:DotnetExe}"" ""{app}\RedSnapper.dll"""; Tasks: startup

[Run]
Filename: "{code:DotnetExe}"; Parameters: """{app}\RedSnapper.dll"""; WorkingDir: "{app}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function DotnetExe(Param: String): String;
begin
  Result := ExpandConstant('{commonpf64}') + '\dotnet\dotnet.exe';
end;

function HasDesktopRuntime(): Boolean;
var
  Found: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}') + '\dotnet\shared\Microsoft.WindowsDesktop.App\8.*', Found) then
  begin
    Result := True;
    FindClose(Found);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not HasDesktopRuntime() then
  begin
    if MsgBox('{#AppName} needs the .NET 8 Desktop Runtime, which was not found on this PC.' + #13#10 + #13#10 +
              'You can continue installing, but the app will not start until the runtime is installed ' +
              '(https://dotnet.microsoft.com/download/dotnet/8.0, "Desktop Runtime", x64). ' +
              'If you are not sure, ask IT.' + #13#10 + #13#10 + 'Continue anyway?',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');
end;
