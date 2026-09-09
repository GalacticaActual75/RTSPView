#define MyAppName "SpotMonitor"
#define MyAppPublisher "RTSPView"
#define MyAppVersion GetEnv("SPOTMONITOR_VERSION")
#if MyAppVersion == ""
  #define MyAppVersion "0.0.0-dev"
#endif

#define MyNumericVersion GetEnv("SPOTMONITOR_NUMERIC_VERSION")
#if MyNumericVersion == ""
  #define MyNumericVersion "1.0.29"
#endif

[Setup]
AppId={{B49BC897-9B86-4C97-85BA-9FA1CF27A835}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SpotMonitor
DefaultGroupName=SpotMonitor
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=SpotMonitor-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\src\SpotMonitor.Viewer\Assets\SpotMonitor.ico
UninstallDisplayIcon={app}\Viewer\SpotMonitor.Viewer.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#MyNumericVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyNumericVersion}

[Tasks]
Name: "autostart"; Description: "Start and supervise SpotMonitor when this user signs in"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\stage\Controller\*"; DestDir: "{app}\Controller"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\stage\Viewer\*"; DestDir: "{app}\Viewer"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\deployment\Start-SpotMonitor.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deployment\Run-Appliance.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deployment\Stop-SpotMonitor.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deployment\Open-Web-Admin.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deployment\Repair-LAN-Firewall.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deployment\Apply-Update.ps1"; DestDir: "{app}\Controller"; Flags: ignoreversion
Source: "..\deployment\Show-UpdateProgress.ps1"; DestDir: "{app}\Controller"; Flags: ignoreversion

[Icons]
Name: "{group}\SpotMonitor"; Filename: "{app}\Start-SpotMonitor.cmd"; IconFilename: "{app}\Viewer\SpotMonitor.Viewer.exe"
Name: "{group}\Web configuration"; Filename: "{app}\Open-Web-Admin.cmd"; IconFilename: "{app}\Controller\SpotMonitor.Controller.exe"
Name: "{group}\Stop SpotMonitor"; Filename: "{app}\Stop-SpotMonitor.cmd"; IconFilename: "{app}\Viewer\SpotMonitor.Viewer.exe"
Name: "{group}\Uninstall SpotMonitor"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SpotMonitor"; Filename: "{app}\Start-SpotMonitor.cmd"; IconFilename: "{app}\Viewer\SpotMonitor.Viewer.exe"; Tasks: desktopicon

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - Private LAN"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - Block Public"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - LAN Only"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""SpotMonitor Web Admin - LAN Only"" dir=in action=allow protocol=TCP localport=5080 remoteip=LocalSubnet profile=any enable=yes"; Flags: runhidden waituntilterminated
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""$runner=Join-Path '{app}' 'Run-Appliance.ps1'; $arguments='-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File '+[char]34+$runner+[char]34; $action=New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments; $trigger=New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME; $trigger.Delay='PT20S'; $settings=New-ScheduledTaskSettingsSet -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Days 3650) -StartWhenAvailable; Register-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -Action $action -Trigger $trigger -Settings $settings -Description 'Starts and supervises the SpotMonitor camera wall.' -Force | Out-Null"""; Flags: runhidden waituntilterminated; Tasks: autostart
Filename: "{app}\Start-SpotMonitor.cmd"; Description: "Launch SpotMonitor"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Stop-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -ErrorAction SilentlyContinue; Unregister-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -Confirm:$false -ErrorAction SilentlyContinue; Get-Process -Name 'SpotMonitor.Controller','SpotMonitor.Viewer' -ErrorAction SilentlyContinue | Stop-Process -Force"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - Private LAN"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - Block Public"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - LAN Only"""; Flags: runhidden waituntilterminated

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SpotMonitor.Controller.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SpotMonitor.Viewer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
  Result := '';
end;
