; OnStepX — Inno Setup script
; Build: iscc OnStepX.AscomDriver.iss
; Prereq: Release build of OnStepX.Driver, OnStepX.Shared, OnStepX.Hub
;         under src\*\bin\Release\
;
; Single combined assembly (ASCOM.OnStepX.dll) hosts both ASCOM drivers:
;   ASCOM.OnStepX.Telescope (ITelescopeV3)
;   ASCOM.OnStepX.Focuser   (IFocuserV2)

#define MyAppName "OnStepX ASCOM + Hub"
#define MyAppShortName "OnStepX"
#define MyAppPublisher "OnStepX Community"
; Allow override from command line: ISCC /DMyAppVersion=0.5.0
#ifndef MyAppVersion
#define MyAppVersion "0.5.0"
#endif
#define HubExe     "OnStepX.Hub.exe"
#define DriverDll  "ASCOM.OnStepX.dll"
; Legacy DLL name shipped by 0.3.x/0.4.x. Removed on upgrade.
#define LegacyDriverDll "ASCOM.OnStepX.Telescope.dll"
#define SharedDll  "OnStepX.Shared.dll"
; AppId stays the same as v0.3.x/v0.4.x so Windows recognises the install as
; an upgrade and correctly removes prior LocalServer / WinForms hub entries
; before laying down the current Inproc/Wpf-hub layout.
#define MyAppAppId "{{A7F3B9C1-4E2D-4F5A-8B1C-9D3E2F4A5B6C}"

; --- Telescope COM identifiers (must match Telescope class [Guid]/[ProgId]) ---
#define TelescopeClsid    "{E3F7B8A1-6C2D-4F3E-9A5B-1F2C3D4E5A6B}"
#define TelescopeProgId   "ASCOM.OnStepX.Telescope"
#define TelescopeFriendly "OnStepX Telescope Driver"
#define TelescopeClass    "ASCOM.OnStepX.Driver.Telescope"

; --- Focuser COM identifiers (must match Focuser class [Guid]/[ProgId]) ---
#define FocuserClsid    "{9F8B2E5C-3D1A-4F4E-B7C8-2D5E6F7A8B9C}"
#define FocuserProgId   "ASCOM.OnStepX.Focuser"
#define FocuserFriendly "OnStepX Focuser Driver"
#define FocuserClass    "ASCOM.OnStepX.Driver.Focuser"

; DriverVersion must match AssemblyVersion of ASCOM.OnStepX.dll — COM activation
; fails if the InprocServer32 Assembly=... Version token disagrees with the
; assembly manifest. build-installer.cmd derives this from /DVERSION.
#ifndef DriverVersion
#define DriverVersion "0.5.0.0"
#endif
#define DriverAsmName "ASCOM.OnStepX"
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
SetupIconFile=AppIcon.ico
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
;
; 64-bit view (HKCR64) serves 64-bit COM clients (NINA, SGP x64). 32-bit view
; (HKCR32, i.e. Wow6432Node) serves 32-bit clients (PHD2, CdC, legacy x86).
[Registry]
; ============================================================================
; Telescope CLSID — 64-bit view
; ============================================================================
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}";                                 ValueType: string; ValueName: "";               ValueData: "{#TelescopeFriendly}";                                                         Flags: uninsdeletekey
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "";               ValueData: "mscoree.dll"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "Class";          ValueData: "{#TelescopeClass}"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Class";          ValueData: "{#TelescopeClass}"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\ProgId";                          ValueType: string; ValueName: "";               ValueData: "{#TelescopeProgId}"
Root: HKCR64; Subkey: "CLSID\{{#TelescopeClsid}\Implemented Categories\{{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"

Root: HKCR64; Subkey: "{#TelescopeProgId}";                                       ValueType: string; ValueName: "";               ValueData: "{#TelescopeFriendly}";                                                         Flags: uninsdeletekey
Root: HKCR64; Subkey: "{#TelescopeProgId}\CLSID";                                 ValueType: string; ValueName: "";               ValueData: "{{#TelescopeClsid}"

; ============================================================================
; Telescope CLSID — 32-bit view (Wow6432Node)
; ============================================================================
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}";                                 ValueType: string; ValueName: "";               ValueData: "{#TelescopeFriendly}";                                                         Flags: uninsdeletekey
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "";               ValueData: "mscoree.dll"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "Class";          ValueData: "{#TelescopeClass}"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32";                  ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Class";          ValueData: "{#TelescopeClass}"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\ProgId";                          ValueType: string; ValueName: "";               ValueData: "{#TelescopeProgId}"
Root: HKCR32; Subkey: "CLSID\{{#TelescopeClsid}\Implemented Categories\{{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"

Root: HKCR32; Subkey: "{#TelescopeProgId}";                                       ValueType: string; ValueName: "";               ValueData: "{#TelescopeFriendly}";                                                         Flags: uninsdeletekey
Root: HKCR32; Subkey: "{#TelescopeProgId}\CLSID";                                 ValueType: string; ValueName: "";               ValueData: "{{#TelescopeClsid}"

; ============================================================================
; Focuser CLSID — 64-bit view
; ============================================================================
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}";                                 ValueType: string; ValueName: "";               ValueData: "{#FocuserFriendly}";                                                           Flags: uninsdeletekey
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "";               ValueData: "mscoree.dll"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "Class";          ValueData: "{#FocuserClass}"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Class";          ValueData: "{#FocuserClass}"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\ProgId";                          ValueType: string; ValueName: "";               ValueData: "{#FocuserProgId}"
Root: HKCR64; Subkey: "CLSID\{{#FocuserClsid}\Implemented Categories\{{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"

Root: HKCR64; Subkey: "{#FocuserProgId}";                                       ValueType: string; ValueName: "";               ValueData: "{#FocuserFriendly}";                                                           Flags: uninsdeletekey
Root: HKCR64; Subkey: "{#FocuserProgId}\CLSID";                                 ValueType: string; ValueName: "";               ValueData: "{{#FocuserClsid}"

; ============================================================================
; Focuser CLSID — 32-bit view (Wow6432Node)
; ============================================================================
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}";                                 ValueType: string; ValueName: "";               ValueData: "{#FocuserFriendly}";                                                           Flags: uninsdeletekey
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "";               ValueData: "mscoree.dll"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "Class";          ValueData: "{#FocuserClass}"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32";                  ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Class";          ValueData: "{#FocuserClass}"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\ProgId";                          ValueType: string; ValueName: "";               ValueData: "{#FocuserProgId}"
Root: HKCR32; Subkey: "CLSID\{{#FocuserClsid}\Implemented Categories\{{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"

Root: HKCR32; Subkey: "{#FocuserProgId}";                                       ValueType: string; ValueName: "";               ValueData: "{#FocuserFriendly}";                                                           Flags: uninsdeletekey
Root: HKCR32; Subkey: "{#FocuserProgId}\CLSID";                                 ValueType: string; ValueName: "";               ValueData: "{{#FocuserClsid}"

; ASCOM Profile registry-mirror store (Platform 6/7 compatible).
Root: HKLM; Subkey: "SOFTWARE\ASCOM\Telescope Drivers\{#TelescopeProgId}";   ValueType: string; ValueName: "";               ValueData: "{#TelescopeFriendly}";                                                         Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\ASCOM\Focuser Drivers\{#FocuserProgId}";       ValueType: string; ValueName: "";               ValueData: "{#FocuserFriendly}";                                                           Flags: uninsdeletekey

; Hub install-path registry hint — HubLauncher in the driver reads this.
; Stale value WpfInstallPath from a previous beta installer is wiped here so
; the launcher never picks an exe that no longer exists on disk.
Root: HKLM; Subkey: "SOFTWARE\OnStepX\Hub";                               ValueType: string; ValueName: "InstallPath";    ValueData: "{app}\{#HubExe}";                                                              Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\OnStepX\Hub";                               ValueType: none;   ValueName: "WpfInstallPath"; Flags: deletevalue

[Icons]
Name: "{group}\{#MyAppShortName} Hub";               Filename: "{app}\{#HubExe}"
Name: "{group}\Uninstall {#MyAppShortName}";         Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppShortName} Hub";         Filename: "{app}\{#HubExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#HubExe}"; Description: "Launch {#MyAppShortName} Hub"; Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
; Let regasm tear down any [ComRegisterFunction] residue before we drop files.
; Run both 64-bit and 32-bit regasm so the Wow6432Node mirror is cleaned too.
Filename: "{dotnet4064}\regasm.exe"; Parameters: "/unregister ""{app}\{#DriverDll}"""; Flags: runhidden; RunOnceId: "RegasmUnreg64"; Check: IsWin64
Filename: "{dotnet4032}\regasm.exe"; Parameters: "/unregister ""{app}\{#DriverDll}"""; Flags: runhidden; RunOnceId: "RegasmUnreg32"

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

// Best-effort: kill any prior LocalServer exe, the legacy WinForms hub, or a
// running WPF-hub beta build still holding a handle on the install dir during
// upgrade. CloseApplications=force handles most cases; this is belt-and-
// suspenders.
procedure KillLegacyServer();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im ASCOM.OnStepX.Telescope.exe >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im OnStepX.Hub.exe             >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im OnStepX.Hub.Wpf.exe         >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Wipe leftover OnStepX.Hub.Wpf.exe binary from the prior beta installer so
// %ProgramFiles%\OnStepX doesn't sit with two coexisting hub exes.
procedure RemoveLegacyWpfBinary();
var
  app: String;
begin
  app := ExpandConstant('{app}');
  if FileExists(app + '\OnStepX.Hub.Wpf.exe') then
    DeleteFile(app + '\OnStepX.Hub.Wpf.exe');
  if FileExists(app + '\OnStepX.Hub.Wpf.pdb') then
    DeleteFile(app + '\OnStepX.Hub.Wpf.pdb');
end;

// Unregister and remove the pre-0.5 single-driver DLL so the new combined
// ASCOM.OnStepX.dll is the only inproc server bound to the Telescope CLSID.
// regasm /unregister is best-effort — ignore failures; the [Registry] block
// that follows will overwrite stale CLSID keys regardless.
procedure RemoveLegacyTelescopeDll();
var
  app: String;
  ResultCode: Integer;
  legacyDll: String;
begin
  app := ExpandConstant('{app}');
  legacyDll := app + '\{#LegacyDriverDll}';
  if FileExists(legacyDll) then
  begin
    Exec(ExpandConstant('{dotnet4064}\regasm.exe'), '/unregister "' + legacyDll + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{dotnet4032}\regasm.exe'), '/unregister "' + legacyDll + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    DeleteFile(legacyDll);
    DeleteFile(app + '\ASCOM.OnStepX.Telescope.pdb');
  end;
end;

// Register the drivers in the ASCOM Profile store via the Profile COM object.
// One Profile call per device type (Telescope, Focuser).
procedure RegisterAscomProfileEntry(deviceType, progId, friendly: String);
var
  Profile: Variant;
  Ok: Boolean;
  ErrMsg: String;
begin
  Ok := False;
  try
    Profile := CreateOleObject('ASCOM.Utilities.Profile');
    Profile.DeviceType := deviceType;
    if not Profile.IsRegistered(progId) then
      Profile.Register(progId, friendly);
    Ok := True;
  except
    ErrMsg := GetExceptionMessage;
  end;
  if not Ok then
    MsgBox('ASCOM Profile registration failed for ' + progId + ':' + #13#10 + ErrMsg + #13#10 + #13#10 +
           'The driver files are installed and COM is registered, but it may not appear in the ASCOM Chooser.' + #13#10 +
           'You can add it manually via ASCOM Profile Explorer:' + #13#10 +
           '  ' + deviceType + ' Drivers -> Add device -> ProgID ' + progId,
           mbError, MB_OK);
end;

procedure RegisterAscomProfile();
begin
  RegisterAscomProfileEntry('Telescope', '{#TelescopeProgId}', '{#TelescopeFriendly}');
  RegisterAscomProfileEntry('Focuser',   '{#FocuserProgId}',   '{#FocuserFriendly}');
end;

procedure UnregisterAscomProfileEntry(deviceType, progId: String);
var
  Profile: Variant;
begin
  try
    Profile := CreateOleObject('ASCOM.Utilities.Profile');
    Profile.DeviceType := deviceType;
    if Profile.IsRegistered(progId) then
      Profile.Unregister(progId);
  except
    // swallow on uninstall
  end;
end;

procedure UnregisterAscomProfile();
begin
  UnregisterAscomProfileEntry('Telescope', '{#TelescopeProgId}');
  UnregisterAscomProfileEntry('Focuser',   '{#FocuserProgId}');
end;

// Drop legacy v0.3.x LocalServer32 / AppID keys left over from an in-place
// upgrade from the exe-based driver. The new [Registry] block writes fresh
// InprocServer32 keys at the same CLSID, but the old LocalServer32 subkey
// under that CLSID must be gone or COM may prefer it.
procedure CleanLegacyComKeys();
begin
  RegDeleteKeyIncludingSubkeys(HKCR, 'CLSID\{#TelescopeClsid}\LocalServer32');
  RegDeleteKeyIncludingSubkeys(HKCR, 'AppID\{A7F3B9C1-4E2D-4F5A-8B1C-9D3E2F4A5B6C}');
  RegDeleteKeyIncludingSubkeys(HKCR, 'AppID\ASCOM.OnStepX.Telescope.exe');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall: begin
      KillLegacyServer();
      CleanLegacyComKeys();
      RemoveLegacyWpfBinary();
      RemoveLegacyTelescopeDll();
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
