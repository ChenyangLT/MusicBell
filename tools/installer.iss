; MusicBell 点歌铃 安装脚本（Inno Setup 6）
#ifndef MyVersion
  #define MyVersion "1.1.0"
#endif

[Setup]
AppId={{9C4C2A71-3E9B-4C21-9D6E-2A7C31B7E114}
AppName=MusicBell 点歌铃
AppVersion={#MyVersion}
AppVerName=MusicBell 点歌铃 {#MyVersion}
AppPublisher=ChenyangLT
AppPublisherURL=https://github.com/ChenyangLT/MusicBell
AppSupportURL=https://github.com/ChenyangLT/MusicBell/issues
AppUpdatesURL=https://github.com/ChenyangLT/MusicBell
DefaultDirName={autopf}\MusicBell
DefaultGroupName=MusicBell 点歌铃
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=MusicBell-Setup-{#MyVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\MusicBell.exe
SetupIconFile=..\src\MusicBell.App\Assets\app.ico
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: checkedonce
Name: "autostart"; Description: "开机自动启动（静默驻留后台）"; GroupDescription: "附加任务:"; Flags: checkedonce

[Files]
Source: "..\dist\MusicBell\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MusicBell 点歌铃"; Filename: "{app}\MusicBell.exe"
Name: "{group}\卸载 MusicBell"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MusicBell 点歌铃"; Filename: "{app}\MusicBell.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MusicBell"; ValueData: """{app}\MusicBell.exe"" --silent"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\MusicBell.exe"; Description: "立即启动 MusicBell 点歌铃"; Flags: nowait postinstall skipifsilent
