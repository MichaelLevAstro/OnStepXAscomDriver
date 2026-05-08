; OnStepX — Inno Setup script
; Build: iscc OnStepX.AscomDriver.iss
; Prereq: Release build of OnStepX.Driver, OnStepX.Shared, OnStepX.Hub
;         under src\*\bin\Release\
;
; Single combined assembly (ASCOM.OnStepX.dll) hosts three ASCOM drivers:
;   ASCOM.OnStepX.Telescope (ITelescopeV3)
;   ASCOM.OnStepX.Focuser   (IFocuserV2)
;   ASCOM.OnStepX.Rotator   (IRotatorV3)

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

; --- Rotator COM identifiers (must match Rotator class [Guid]/[ProgId]) ---
#define RotatorClsid    "{B6A2D5F4-7C8E-4B3A-9D1F-3E5C7A8B9D2E}"
#define RotatorProgId   "ASCOM.OnStepX.Rotator"
#define RotatorFriendly "OnStepX Rotator Driver"
#define RotatorClass    "ASCOM.OnStepX.Driver.Rotator"

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

; com0com signed redistributable. Vendored under installer\com0com-bin\.
; The Hub uses setupc.exe (post-install + on-demand) to create / delete
; virtual COM pairs for the NINA TPPA OAPA bridge. Compile-time guard so
; a developer can build the installer without the binaries on hand —
; check installer\com0com-bin\BINARIES_README.md for what to drop in.
#if FileExists(AddBackslash(SourcePath) + "com0com-bin\setupc.exe")
Source: "com0com-bin\*"; DestDir: "{app}\com0com"; Flags: ignoreversion recursesubdirs createallsubdirs
#else
#pragma message "WARNING: com0com-bin\setupc.exe missing — installer will ship without bundled com0com. Hub UI degrades gracefully."
#endif

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

; ============================================================================
; Rotator CLSID — 64-bit view
; ============================================================================
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}";                                 ValueType: string; ValueName: "";               ValueData: "{#RotatorFriendly}";                                                           Flags: uninsdeletekey
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "";               ValueData: "mscoree.dll"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "Class";          ValueData: "{#RotatorClass}"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Class";          ValueData: "{#RotatorClass}"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\ProgId";                          ValueType: string; ValueName: "";               ValueData: "{#RotatorProgId}"
Root: HKCR64; Subkey: "CLSID\{{#RotatorClsid}\Implemented Categories\{{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"

Root: HKCR64; Subkey: "{#RotatorProgId}";                                       ValueType: string; ValueName: "";               ValueData: "{#RotatorFriendly}";                                                           Flags: uninsdeletekey
Root: HKCR64; Subkey: "{#RotatorProgId}\CLSID";                                 ValueType: string; ValueName: "";               ValueData: "{{#RotatorClsid}"

; ============================================================================
; Rotator CLSID — 32-bit view (Wow6432Node)
; ============================================================================
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}";                                 ValueType: string; ValueName: "";               ValueData: "{#RotatorFriendly}";                                                           Flags: uninsdeletekey
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "";               ValueData: "mscoree.dll"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "Class";          ValueData: "{#RotatorClass}"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32";                  ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Class";          ValueData: "{#RotatorClass}"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "Assembly";       ValueData: "{#DriverAsmName}, Version={#DriverVersion}, Culture=neutral, PublicKeyToken=null"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\InprocServer32\{#DriverVersion}"; ValueType: string; ValueName: "CodeBase";       ValueData: "file:///{app}\{#DriverDll}"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\ProgId";                          ValueType: string; ValueName: "";               ValueData: "{#RotatorProgId}"
Root: HKCR32; Subkey: "CLSID\{{#RotatorClsid}\Implemented Categories\{{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"

Root: HKCR32; Subkey: "{#RotatorProgId}";                                       ValueType: string; ValueName: "";               ValueData: "{#RotatorFriendly}";                                                           Flags: uninsdeletekey
Root: HKCR32; Subkey: "{#RotatorProgId}\CLSID";                                 ValueType: string; ValueName: "";               ValueData: "{{#RotatorClsid}"

; ASCOM Profile registry-mirror store (Platform 6/7 compatible).
Root: HKLM; Subkey: "SOFTWARE\ASCOM\Telescope Drivers\{#TelescopeProgId}";   ValueType: string; ValueName: "";               ValueData: "{#TelescopeFriendly}";                                                         Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\ASCOM\Focuser Drivers\{#FocuserProgId}";       ValueType: string; ValueName: "";               ValueData: "{#FocuserFriendly}";                                                           Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\ASCOM\Rotator Drivers\{#RotatorProgId}";       ValueType: string; ValueName: "";               ValueData: "{#RotatorFriendly}";                                                           Flags: uninsdeletekey

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

// Pre-0.5 LocalServer-style driver shipped ASCOM.OnStepX.Telescope.exe (a
// hosting EXE registered at the Telescope CLSID via LocalServer32). Newer
// builds use the Inproc DLL only — leaving the legacy exe on disk lets the
// stale Start Menu shortcut "OnStepX ASCOM Hub" still resolve and confuses
// users into running prehistoric UI. Sweep the file + sidecars + shortcuts.
procedure RemoveLegacyLocalServerExe();
var
  app: String;
  startMenu: String;
begin
  app := ExpandConstant('{app}');
  if FileExists(app + '\ASCOM.OnStepX.Telescope.exe') then
    DeleteFile(app + '\ASCOM.OnStepX.Telescope.exe');
  if FileExists(app + '\ASCOM.OnStepX.Telescope.exe.config') then
    DeleteFile(app + '\ASCOM.OnStepX.Telescope.exe.config');
  if FileExists(app + '\ASCOM.OnStepX.Telescope.pdb') then
    DeleteFile(app + '\ASCOM.OnStepX.Telescope.pdb');

  // Stale Start Menu shortcut from prior installer naming.
  startMenu := ExpandConstant('{commonprograms}') + '\OnStepX';
  if FileExists(startMenu + '\OnStepX ASCOM Hub.lnk') then
    DeleteFile(startMenu + '\OnStepX ASCOM Hub.lnk');
  if FileExists(startMenu + '\Uninstall OnStepX ASCOM.lnk') then
    DeleteFile(startMenu + '\Uninstall OnStepX ASCOM.lnk');

  // Stale LocalServer32 registry registration at the Telescope CLSID.
  RegDeleteKeyIncludingSubkeys(HKCR, 'CLSID\{#TelescopeClsid}\LocalServer32');
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
  RegisterAscomProfileEntry('Rotator',   '{#RotatorProgId}',   '{#RotatorFriendly}');
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
  UnregisterAscomProfileEntry('Rotator',   '{#RotatorProgId}');
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

// =========================================================================
// com0com bundled-driver provisioning.
// The Hub's NINA TPPA OAPA bridge needs a paired virtual COM port. Rather
// than asking users to install com0com manually, we ship setupc.exe under
// {app}\com0com\ (see [Files]) and create one pair on first install.
// The pair is recorded in HKLM\SOFTWARE\OnStepX\Hub\Com0comManagedPairs
// (REG_SZ, semicolon-separated "<pairNum>|<portA>|<portB>" entries) so
// Com0comManager.GetManagedPairsFromRegistry() can render the pair list
// without elevation, and so uninstall removes only Hub-created pairs.
// =========================================================================

const
  Com0comRegPath  = 'SOFTWARE\OnStepX\Hub';
  Com0comRegValue = 'Com0comManagedPairs';

var
  // Cache of busy COM names from `setupc busynames COM*`. Stored as a
  // comma-wrapped uppercase list ",COM1,COM10,COM11," so a contains-test
  // is a single Pos() call without false-substring-matches (e.g. COM1
  // matching COM10).
  BusyComNamesCache: String;
  BusyComNamesLoaded: Boolean;

function Com0comSetupcPath(): String;
begin
  Result := ExpandConstant('{app}\com0com\setupc.exe');
end;

procedure LoadBusyComNames();
var
  setupc, tmpFile, cmdLine, body, name: String;
  ansiBody: AnsiString;
  resultCode, i, lineStart: Integer;
begin
  BusyComNamesLoaded := True;
  BusyComNamesCache := ',';
  setupc := Com0comSetupcPath;
  if not FileExists(setupc) then Exit;
  tmpFile := ExpandConstant('{tmp}\onstepx_com0com_busy.log');
  // setupc busynames asks ComDB + DosDevice — covers both stale ComDB
  // claims and live QueryDosDevice ports. Returns one name per line,
  // already uppercased.
  cmdLine := '/c ""' + setupc + '" busynames COM* > "' + tmpFile + '""';
  if not Exec(ExpandConstant('{cmd}'), cmdLine,
             ExpandConstant('{app}\com0com'), SW_HIDE, ewWaitUntilTerminated, resultCode) then Exit;
  if not LoadStringFromFile(tmpFile, ansiBody) then ansiBody := '';
  DeleteFile(tmpFile);
  body := String(ansiBody);
  lineStart := 1;
  for i := 1 to Length(body) do
  begin
    if (body[i] = #10) or (body[i] = #13) then
    begin
      name := Trim(Copy(body, lineStart, i - lineStart));
      lineStart := i + 1;
      if (Length(name) > 0) and (Pos('COM', UpperCase(name)) = 1) then
        BusyComNamesCache := BusyComNamesCache + UpperCase(name) + ',';
    end;
  end;
  // Trailing line w/o newline.
  if lineStart <= Length(body) then
  begin
    name := Trim(Copy(body, lineStart, Length(body) - lineStart + 1));
    if (Length(name) > 0) and (Pos('COM', UpperCase(name)) = 1) then
      BusyComNamesCache := BusyComNamesCache + UpperCase(name) + ',';
  end;
  Log('com0com busy names cache = ' + BusyComNamesCache);
end;

function ComPortInUse(comNum: Integer): Boolean;
var
  names: TArrayOfString;
  i: Integer;
  v: String;
  target: String;
begin
  Result := False;
  target := 'COM' + IntToStr(comNum);
  if not BusyComNamesLoaded then LoadBusyComNames();
  if Pos(',' + target + ',', BusyComNamesCache) > 0 then
  begin
    Result := True;
    Exit;
  end;
  // Fallback in case setupc invocation failed for any reason.
  if not RegGetValueNames(HKLM, 'HARDWARE\DEVICEMAP\SERIALCOMM', names) then Exit;
  for i := 0 to GetArrayLength(names) - 1 do
  begin
    if RegQueryStringValue(HKLM, 'HARDWARE\DEVICEMAP\SERIALCOMM', names[i], v) then
    begin
      if CompareText(v, target) = 0 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function FindFirstFreeComPair(startAt: Integer): Integer;
var
  n: Integer;
begin
  n := startAt;
  while n < 999 do
  begin
    if (not ComPortInUse(n)) and (not ComPortInUse(n + 1)) then
    begin
      Result := n;
      Exit;
    end;
    Inc(n);
  end;
  Result := startAt;
end;

// First "CNCA<n>" -> integer n, or -1 if not found.
function ParsePairNumber(s: String): Integer;
var
  i, p: Integer;
  digits: String;
begin
  Result := -1;
  p := Pos('CNCA', UpperCase(s));
  if p = 0 then Exit;
  digits := '';
  i := p + 4;
  while (i <= Length(s)) and (s[i] >= '0') and (s[i] <= '9') do
  begin
    digits := digits + s[i];
    Inc(i);
  end;
  if Length(digits) = 0 then Exit;
  Result := StrToIntDef(digits, -1);
end;

procedure AppendManagedPair(pairNum: Integer; comA, comB: String);
var
  existing, entry: String;
begin
  entry := IntToStr(pairNum) + '|' + comA + '|' + comB;
  if not RegQueryStringValue(HKLM, Com0comRegPath, Com0comRegValue, existing) then
    existing := '';
  if existing = '' then
    RegWriteStringValue(HKLM, Com0comRegPath, Com0comRegValue, entry)
  else
    RegWriteStringValue(HKLM, Com0comRegPath, Com0comRegValue, existing + ';' + entry);
end;

function Com0comInstallPair(comA, comB: String): Boolean;
var
  setupc, tmpFile, cmdLine, logBody: String;
  ansiBody: AnsiString;
  resultCode, pairNum: Integer;
begin
  Result := False;
  setupc := Com0comSetupcPath;
  if not FileExists(setupc) then
  begin
    Log('com0com setupc.exe missing at ' + setupc + ', skipping pair create');
    Exit;
  end;
  tmpFile := ExpandConstant('{tmp}\onstepx_com0com_install.log');
  cmdLine := '/c ""' + setupc + '" install PortName=' + comA + ' PortName=' + comB + ' > "' + tmpFile + '""';
  // setupc.exe locates com0com.inf / setup.dll relative to its working
  // directory — set workdir to {app}\com0com\ or the install fails with
  // "INF not found".
  if not Exec(ExpandConstant('{cmd}'), cmdLine, ExpandConstant('{app}\com0com'), SW_HIDE, ewWaitUntilTerminated, resultCode) then
  begin
    Log('Exec setupc install failed (Exec returned false)');
    Exit;
  end;
  if resultCode <> 0 then
  begin
    Log('setupc install returned exit code ' + IntToStr(resultCode));
    Exit;
  end;
  // LoadStringFromFile takes AnsiString in Unicode Inno; setupc output is ASCII.
  if not LoadStringFromFile(tmpFile, ansiBody) then ansiBody := '';
  logBody := String(ansiBody);
  DeleteFile(tmpFile);
  pairNum := ParsePairNumber(logBody);
  if pairNum < 0 then
  begin
    Log('Could not parse CNCA<n> from setupc output');
    Exit;
  end;
  AppendManagedPair(pairNum, comA, comB);
  Log('com0com pair ' + IntToStr(pairNum) + ' = ' + comA + ' <-> ' + comB);
  Result := True;
end;

procedure InstallDefaultCom0comPair();
var
  startCom: Integer;
  comA, comB: String;
begin
  if not FileExists(Com0comSetupcPath) then
  begin
    Log('com0com binaries not bundled; skipping default pair install');
    Exit;
  end;
  if RegValueExists(HKLM, Com0comRegPath, Com0comRegValue) then
  begin
    Log('Com0comManagedPairs already populated; skipping default pair install');
    Exit;
  end;
  startCom := FindFirstFreeComPair(10);
  comA := 'COM' + IntToStr(startCom);
  comB := 'COM' + IntToStr(startCom + 1);
  Com0comInstallPair(comA, comB);
end;

procedure Com0comRemoveManagedPairs();
var
  raw, entry, sub: String;
  pairNum, sepIndex, resultCode, idx: Integer;
  setupc: String;
begin
  setupc := Com0comSetupcPath;
  if not RegQueryStringValue(HKLM, Com0comRegPath, Com0comRegValue, raw) then
    Exit;
  // Manual split on ';' — Inno Pascal lacks a stock array splitter.
  while Length(raw) > 0 do
  begin
    idx := Pos(';', raw);
    if idx = 0 then
    begin
      entry := raw;
      raw := '';
    end
    else
    begin
      entry := Copy(raw, 1, idx - 1);
      raw := Copy(raw, idx + 1, Length(raw) - idx);
    end;
    if entry = '' then continue;
    sepIndex := Pos('|', entry);
    if sepIndex = 0 then continue;
    sub := Copy(entry, 1, sepIndex - 1);
    pairNum := StrToIntDef(sub, -1);
    if pairNum < 0 then continue;
    if not FileExists(setupc) then continue;
    Exec(setupc, 'remove ' + IntToStr(pairNum), ExpandConstant('{app}\com0com'), SW_HIDE, ewWaitUntilTerminated, resultCode);
    Log('com0com remove pair ' + IntToStr(pairNum) + ' rc=' + IntToStr(resultCode));
  end;
  RegDeleteValue(HKLM, Com0comRegPath, Com0comRegValue);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall: begin
      KillLegacyServer();
      CleanLegacyComKeys();
      RemoveLegacyWpfBinary();
      RemoveLegacyTelescopeDll();
      RemoveLegacyLocalServerExe();
    end;
    ssPostInstall: begin
      RegisterAscomProfile();
      InstallDefaultCom0comPair();
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    UnregisterAscomProfile();
    Com0comRemoveManagedPairs();
  end;
end;
