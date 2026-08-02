; TOfont 安装程序 - Inno Setup 6
; 使用: ISCC.exe installer.iss /DMyAppVersion=1.0.3 /DBuildMode=full
; BuildMode: full=含 .NET 运行时 | nofx=不含 .NET 运行时（需用户自行安装）

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef BuildMode
  #define BuildMode "full"
#endif

#if BuildMode == "nofx"
  #define PublishDir "..\publish-fd"
  #define OutputSuffix "-nofx"
  #define DiskRequired 200
  #define AppDisplayName "TOfont"
  #define AppIdFull "{{7B4D0E31-1E4F-4A56-9C77-672DB3E2D5F6}"
#else
  #define PublishDir "..\publish"
  #define OutputSuffix ""
  #define DiskRequired 300
  #define AppDisplayName "TOfont"
  #define AppIdFull "{{90CBE8EC-BF35-4C77-90CB-672DB3E2D575}"
#endif

#define MyAppName "TOfont"
#define MyAppPublisher "dongbeiboy"
#define MyAppExeName "TOfont.WinUI.exe"
#define MyAppAssocName MyAppName + " File"
#define MyAppAssocExt ".zm"

[Setup]
AppId={#AppIdFull}
AppName={#AppDisplayName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=TOfont-setup{#OutputSuffix}-{#MyAppVersion}
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
; 最低系统要求：Win10 1809（与 TargetPlatformMinVersion 一致，WinUI 3 需要）
MinVersion=10.0.17763
; 安装需要的最小磁盘空间（MB）
ExtraDiskSpaceRequired={#DiskRequired}
; 升级/卸载时自动关闭运行中的应用，避免文件占用
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\TOfont.WinUI.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\LocalState"
