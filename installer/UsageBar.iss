#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif

#define MyAppName "UsageBar"
#define MyAppPublisher "ghostySRC"
#define MyAppUrl "https://github.com/ghostySRC/UsageBar"

[Setup]
AppId={{BFE42717-41B8-4CC8-883A-84B51E21D1A4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={localappdata}\Programs\UsageBar
DefaultGroupName=UsageBar
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=UsageBar-Setup
SetupIconFile=..\src\UsageBar.Windows\Assets\UsageBar.ico
UninstallDisplayIcon={app}\UsageBar.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
Uninstallable=yes

[Tasks]
Name: "startup"; Description: "Start UsageBar when I sign in to Windows"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\UsageBar"; Filename: "{app}\UsageBar.exe"; IconFilename: "{app}\UsageBar.exe"
Name: "{autodesktop}\UsageBar"; Filename: "{app}\UsageBar.exe"; IconFilename: "{app}\UsageBar.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\UsageBar.exe"; Parameters: "{code:StartupArgument}"; Description: "Launch UsageBar"; Flags: postinstall nowait skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "UsageBar"; ValueData: """{app}\UsageBar.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Code]
function StartupArgument(Param: String): String;
begin
  if WizardIsTaskSelected('startup') then
    Result := '--enable-startup'
  else
    Result := '--disable-startup';
end;
