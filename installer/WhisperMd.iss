#ifndef SourceDir
  #define SourceDir "..\\release\\payload"
#endif
#ifndef OutputDir
  #define OutputDir "..\\release"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "WhisperMd"
#define AppExeName "WhisperMd.exe"

[Setup]
AppId={{C65013C5-C9A2-4FCF-8946-B6A5D7B2B08E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=WhisperMd
DefaultDirName={localappdata}\Programs\WhisperMd
DefaultGroupName=WhisperMd
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=WhisperMd-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\\WhisperMd"; Filename: "{app}\\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\\WhisperMd"; Filename: "{app}\\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\\{#AppExeName}"; Description: "Запустить WhisperMd"; Flags: nowait postinstall skipifsilent
