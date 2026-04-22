; OnStepX — Inno Setup script
; Build: iscc OnStepX.AscomDriver.iss
; Prereq: Release build of three projects under src\*\bin\Release\

#define MyAppName "OnStepX ASCOM + Hub"
#define MyAppShortName "OnStepX"
#define MyAppPublisher "OnStepX Community"
; Allow override from command line: ISCC /DMyAppVersion=0.4.0
#ifndef MyAppVersion
#define MyAppVersion "0.4.0"
#endif
#define HubExe     "OnStepX.Hub.exe"
#define DriverDll  "ASCOM.OnStepX.Telescope.dll"
#define SharedDll  "OnStepX.Shared.dll"
; AppId stays the same as v0.3.x so Windows recognises the install as an upgrade
; and correctly removes the old LocalServer entries before we lay down InprocServer32.
#define MyAppAppId "{{A7F3B9C1-4E2D-4F5A-8B1C-9D3E2F4A5B6C}"
; COM identifiers — must match Telescope class [Guid] and [ProgId] attributes
#define ComClsid   "{E3F7B8A1-6C2D-4F3E-9A5B-1F2C3D4E5A6B}"
#define ComProgId  "ASCOM.OnStepX.Telescope"
#define ComFriendly "OnStepX Telescope Driver"
; DriverVersion must match the AssemblyVersion of ASCOM.OnStepX.Telescope.dll —
; COM activation fails if the InprocServer32 Assembly=... Version token disagrees
; with the assembly manifest. build-installer.cmd derives this from the /DVERSION arg.
#ifndef DriverVersion
#define DriverVersion "0.4.0.0"
#endif
#define DriverAsmName "ASCOM.OnStepX.Telescope"
#define DriverPublicKeyToken ""
#define DriverSrc  "..\src\OnStepX.Driver\bin\Release"
#define HubSrc     "..\src\OnStepX.Hub\bin\Release"
#define OutRoot    "..\installer"

[Setup]
AppId={#MyAppAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppSupportURL=https://github.com/hjd1964/OnStepX
DefaultDirName={autopf}\OnStepX
DefaultGroupName=OnStepX
DisableProgramGroupPage=no
UninstallDisplayIcon={app}\{#HubExe}
OutputDir={#OutRoot}
OutputBaseFilename=OnStepX-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
MinVersion=10.0.17763
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#HubSrc}\{#HubExe}";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#HubSrc}\{#HubExe}.config";       DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#DriverSrc}\{#DriverDll}";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#HubSrc}\{#SharedDll}";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#HubSrc}\*.dll";                  DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#DriverSrc}\*.pdb";               DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#HubSrc}\*.pdb";                  DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; COM registration written directly — regasm is run post-install as belt-and-
; suspenders; these keys are the authoritative source so the install works
; without .NET Framework SDK tools on the target machine.
; Inno escapes braces by doubling: "{{" → literal "{".
[Registry]
Root: HKCR; Subkey: "CLSID\{{#ComClsid}";                                 ValueType: string; ValueName: "";               ValueData: "{#ComFriendly}";                                                               Flags: uninsdeletekey
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\InprocServer32";                  ValueType: string; ValueName: "";               ValueData: "mscoree.dll"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\InprocServer32";                  ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\InprocServer32";                  ValueType: string; ValueName: "Class";          ValueData: "ASCOM.OnStepX.Driver.Telescope"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\InprocServer32";                  ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\InprocServer32";                  ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\InprocServer32";                  ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Class";          ValueData: "ASCOM.OnStepX.Driver.Telescope"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\ProgId";                          ValueType: string; ValueName: "";               ValueData: "{#ComProgId}"
Root: HKCR; Subkey: "CLSID\{{#ComClsid}\Implemented Categories\{{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"

Root: HKCR; Subkey: "{#ComProgId}";                                       ValueType: string; ValueName: "";               ValueData: "{#ComFriendly}";                                                               Flags: uninsdeletekey
Root: HKCR; Subkey: "{#ComProgId}\CLSID";                                 ValueType: string; ValueName: "";               ValueData: "{{#ComClsid}"

; ASCOM Profile registry-mirror store (Platform 6/7 compatible).
Root: HKLM; Subkey: "SOFTWARE\ASCOM\Telescope Drivers\{#ComProgId}";      ValueType: string; ValueName: "";               ValueData: "{#ComFriendly}";                                                               Flags: uninsdeletekey

; Hub install-path registry hint — HubLauncher in the driver reads this.
Root: HKLM; Subkey: "SOFTWARE\OnStepX\Hub";                               ValueType: string; ValueName: "InstallPath";    ValueData: "{app}\{#HubExe}";                                                              Flags: uninsdeletekey

[Icons]
Name: "{group}\{#MyAppShortName} Hub";               Filename: "{app}\{#HubExe}"
Name: "{group}\Uninstall {#MyAppShortName}";         Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppShortName} Hub";         Filename: "{app}\{#HubExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#HubExe}"; Description: "Launch {#MyAppShortName} Hub"; Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
; Let regasm tear down any [ComRegisterFunction] residue before we drop files.
Filename: "{dotnet40}\regasm.exe"; Parameters: "/unregister ""{app}\{#DriverDll}"""; Flags: runhidden; RunOnceId: "RegasmUnreg"

[Code]
function IsAscomPlatformInstalled(): Boolean;
var
  regPath: String;
begin
  regPath := 'SOFTWARE\ASCOM';
  Result := RegKeyExists(HKLM32, regPath) or RegKeyExists(HKLM64, regPath);
end;

function InitializeSetup(): Boolean;
begin
  if not IsAscomPlatformInstalled() then
  begin
    if MsgBox('ASCOM Platform was not detected on this machine.' + #13#10 +
              'The driver will install but will not function until the ASCOM Platform is installed.' + #13#10 + #13#10 +
              'Download from: https://ascom-standards.org/' + #13#10 + #13#10 +
              'Continue anyway?', mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;
  Result := True;
end;

// Best-effort: kill any running v0.3.x LocalServer exe or old Hub still holding
// a handle on the install dir during upgrade. CloseApplications=force handles
// most cases; this is belt-and-suspenders.
procedure KillLegacyServer();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im ASCOM.OnStepX.Telescope.exe >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im OnStepX.Hub.exe             >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Register the driver in the ASCOM Profile store via the Profile COM object.
procedure RegisterAscomProfile();
var
  Profile: Variant;
  Ok: Boolean;
  ErrMsg: String;
begin
  Ok := False;
  try
    Profile := CreateOleObject('ASCOM.Utilities.Profile');
    Profile.DeviceType := 'Telescope';
    if not Profile.IsRegistered('{#ComProgId}') then
      Profile.Register('{#ComProgId}', '{#ComFriendly}');
    Ok := True;
  except
    ErrMsg := GetExceptionMessage;
  end;
  if not Ok then
    MsgBox('ASCOM Profile registration failed:' + #13#10 + ErrMsg + #13#10 + #13#10 +
           'The driver files are installed and COM is registered, but it may not appear in the ASCOM Chooser.' + #13#10 +
           'You can add it manually via ASCOM Profile Explorer:' + #13#10 +
           '  Telescope Drivers -> Add device -> ProgID {#ComProgId}',
           mbError, MB_OK);
end;

procedure UnregisterAscomProfile();
var
  Profile: Variant;
begin
  try
    Profile := CreateOleObject('ASCOM.Utilities.Profile');
    Profile.DeviceType := 'Telescope';
    if Profile.IsRegistered('{#ComProgId}') then
      Profile.Unregister('{#ComProgId}');
  except
    // swallow on uninstall
  end;
end;

// Drop legacy v0.3.x LocalServer32 / AppID keys left over from an in-place
// upgrade from the exe-based driver. The new [Registry] block writes fresh
// InprocServer32 keys at the same CLSID, but the old LocalServer32 subkey
// under that CLSID must be gone or COM may prefer it.
procedure CleanLegacyComKeys();
begin
  RegDeleteKeyIncludingSubkeys(HKCR, 'CLSID\{#ComClsid}\LocalServer32');
  RegDeleteKeyIncludingSubkeys(HKCR, 'AppID\{A7F3B9C1-4E2D-4F5A-8B1C-9D3E2F4A5B6C}');
  RegDeleteKeyIncludingSubkeys(HKCR, 'AppID\ASCOM.OnStepX.Telescope.exe');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall: begin
      KillLegacyServer();
      CleanLegacyComKeys();
    end;
    ssPostInstall: begin
      RegisterAscomProfile();
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    UnregisterAscomProfile();
end;
