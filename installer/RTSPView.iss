#define MyAppName "RTSPView"
#define MyAppPublisher "RTSPView"
#define MyAppVersion GetEnv("RTSPVIEW_VERSION")
#if MyAppVersion == ""
  #define MyAppVersion "0.0.0-dev"
#endif

#define MyNumericVersion GetEnv("RTSPVIEW_NUMERIC_VERSION")
#if MyNumericVersion == ""
  #define MyNumericVersion "1.0.29"
#endif

[Setup]
AppId={{B49BC897-9B86-4C97-85BA-9FA1CF27A835}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\RTSPView
UsePreviousAppDir=yes
DefaultGroupName=RTSPView
UsePreviousGroup=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=RTSPView-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\src\RTSPView.Viewer\Assets\RTSPView.ico
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
Name: "autostart"; Description: "Start and supervise RTSPView when this user signs in"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\stage\Controller\*"; DestDir: "{app}\Controller"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\stage\Viewer\*"; DestDir: "{app}\Viewer"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\stage\Maintenance\*"; DestDir: "{app}\Maintenance"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\deployment\Start-RTSPView.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deployment\Run-Appliance.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deployment\Stop-RTSPView.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deployment\Open-Web-Admin.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deployment\Repair-LAN-Firewall.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deployment\Enable-LanAccess.ps1"; DestDir: "{app}\Controller"; Flags: ignoreversion
Source: "..\deployment\Apply-Update.ps1"; DestDir: "{app}\Controller"; Flags: ignoreversion
Source: "..\deployment\Show-UpdateProgress.ps1"; DestDir: "{app}\Controller"; Flags: ignoreversion

[InstallDelete]
; Remove only known files/shortcuts owned by earlier installers; retain settings and updater identities.
Type: files; Name: "{app}\Start-SpotMonitor.cmd"
Type: files; Name: "{app}\Stop-SpotMonitor.cmd"
Type: files; Name: "{autodesktop}\SpotMonitor.lnk"
Type: files; Name: "{commonprograms}\SpotMonitor\SpotMonitor.lnk"
Type: files; Name: "{commonprograms}\SpotMonitor\Stop SpotMonitor.lnk"
Type: files; Name: "{commonprograms}\SpotMonitor\Uninstall SpotMonitor.lnk"
Type: files; Name: "{commonprograms}\SpotMonitor\Web configuration.lnk"
Type: dirifempty; Name: "{commonprograms}\SpotMonitor"
Type: files; Name: "{app}\Controller\SpotMonitor.Core.dll"
Type: files; Name: "{app}\Controller\SpotMonitor.Infrastructure.dll"
Type: files; Name: "{app}\Viewer\SpotMonitor.Core.dll"
Type: files; Name: "{app}\Viewer\SpotMonitor.Infrastructure.dll"

[Icons]
Name: "{group}\RTSPView"; Filename: "{app}\Start-RTSPView.cmd"; IconFilename: "{app}\Viewer\SpotMonitor.Viewer.exe"
Name: "{group}\Web configuration"; Filename: "{app}\Open-Web-Admin.cmd"; IconFilename: "{app}\Controller\SpotMonitor.Controller.exe"
Name: "{group}\Stop RTSPView"; Filename: "{app}\Stop-RTSPView.cmd"; IconFilename: "{app}\Viewer\SpotMonitor.Viewer.exe"
Name: "{group}\Uninstall RTSPView"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RTSPView"; Filename: "{app}\Start-RTSPView.cmd"; IconFilename: "{app}\Viewer\SpotMonitor.Viewer.exe"; Tasks: desktopicon

[Run]
; Start only a previously enabled helper; first-time enabling stays opt-in in System.
Filename: "{sys}\sc.exe"; Parameters: "start RTSPViewMaintenance"; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - Private LAN"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - Block Public"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - LAN Only"""; Flags: runhidden waituntilterminated
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""$runner=Join-Path '{app}' 'Run-Appliance.ps1'; $arguments='-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File '+[char]34+$runner+[char]34; $action=New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments; $trigger=New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME; $trigger.Delay='PT20S'; $settings=New-ScheduledTaskSettingsSet -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Days 3650) -StartWhenAvailable; Register-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -Action $action -Trigger $trigger -Settings $settings -Description 'Starts and supervises the RTSPView camera wall.' -Force | Out-Null"""; Flags: runhidden waituntilterminated; Tasks: autostart
Filename: "{app}\Start-RTSPView.cmd"; Description: "Launch RTSPView"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "delete RTSPViewMaintenance"; Flags: runhidden waituntilterminated
Filename: "{sys}\reg.exe"; Parameters: "delete HKLM\SOFTWARE\RTSPView\Maintenance /f /reg:64"; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""RTSPView Admin - Private LAN"""; Flags: runhidden waituntilterminated
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Stop-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -ErrorAction SilentlyContinue; Unregister-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -Confirm:$false -ErrorAction SilentlyContinue; Get-Process -Name 'SpotMonitor.Controller','SpotMonitor.Viewer' -ErrorAction SilentlyContinue | Stop-Process -Force"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - Private LAN"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - Block Public"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""SpotMonitor Web Admin - LAN Only"""; Flags: runhidden waituntilterminated

[Code]
function StopMaintenance(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -WindowStyle Hidden -Command "try { $s=Get-Service RTSPViewMaintenance -ErrorAction SilentlyContinue; if ($s -and $s.Status -ne ''Stopped'') { $s.Stop(); $s.WaitForStatus(''Stopped'', [TimeSpan]::FromSeconds(30)) }; exit 0 } catch { exit 1 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function InitializeUninstall(): Boolean;
begin
  Result := StopMaintenance();
  if not Result then
    MsgBox('Wait for PawnIO maintenance to finish, then retry uninstalling RTSPView.', mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  if not StopMaintenance() then
  begin
    Result := 'Wait for PawnIO maintenance to finish, then retry installing RTSPView.';
    exit;
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SpotMonitor.Controller.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SpotMonitor.Viewer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
  Result := '';
end;
