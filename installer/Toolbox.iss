; Toolbox installer script for Inno Setup 6
; Build via installer\build-installer.ps1 (dotnet publish + ISCC)

#define MyAppName "Toolbox"
#define MyAppVersion "0.2.3"
#define MyAppPublisher "Toolbox"

; Flavor is passed in by build-installer.ps1 (/DFlavor=...); default for manual compiles.
#ifndef Flavor
#define Flavor "framework"
#endif

[Setup]
AppId={{7C2A9B1E-4D3F-4A8B-9E5C-6F0D1A2B3C4D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\Example.exe
OutputDir=dist
OutputBaseFilename=Toolbox-{#MyAppVersion}-{#Flavor}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ---- Install types shown on the "Setup Type" page ----
[Types]
Name: "full"; Description: "Full installation (all tools)"
Name: "compact"; Description: "Compact installation (WinForms tool only)"
Name: "custom"; Description: "Custom selection"; Flags: iscustom

; ---- Optional components (checkbox tree on the "Custom Setup" page) ----
[Components]
Name: "example"; Description: "Example tool (WinForms)"; Types: full compact custom
Name: "examplewpf"; Description: "ExampleWPF tool (WPF)"; Types: full custom
Name: "funcgen"; Description: "Function Generator (UDP signal generator)"; Types: full custom
Name: "rttcli"; Description: "RTT-CLI (J-Link RTT terminal)"; Types: full custom
Name: "serialassistant"; Description: "Serial Assistant (COM port debug tool)"; Types: full custom

; ---- Additional optional tasks ----
[Tasks]
Name: "desktopicon_example"; Description: "Create a &desktop shortcut for Example"; Components: example; Flags: unchecked
Name: "desktopicon_examplewpf"; Description: "Create a &desktop shortcut for ExampleWPF"; Components: examplewpf; Flags: unchecked
Name: "desktopicon_funcgen"; Description: "Create a &desktop shortcut for Function Generator"; Components: funcgen; Flags: unchecked
Name: "desktopicon_serialassistant"; Description: "Create a &desktop shortcut for Serial Assistant"; Components: serialassistant; Flags: unchecked

; ---- Flat layout: exes and shared libraries (Toolbox.Core.dll etc.) side by side in {app} ----
[Files]
Source: "publish\App\Example.exe"; DestDir: "{app}"; Components: example; Flags: ignoreversion
Source: "publish\App\Example.dll"; DestDir: "{app}"; Components: example; Flags: ignoreversion
Source: "publish\App\Example.deps.json"; DestDir: "{app}"; Components: example; Flags: ignoreversion
Source: "publish\App\Example.runtimeconfig.json"; DestDir: "{app}"; Components: example; Flags: ignoreversion
Source: "publish\App\ExampleWPF.exe"; DestDir: "{app}"; Components: examplewpf; Flags: ignoreversion
Source: "publish\App\ExampleWPF.dll"; DestDir: "{app}"; Components: examplewpf; Flags: ignoreversion
Source: "publish\App\ExampleWPF.deps.json"; DestDir: "{app}"; Components: examplewpf; Flags: ignoreversion
Source: "publish\App\ExampleWPF.runtimeconfig.json"; DestDir: "{app}"; Components: examplewpf; Flags: ignoreversion
Source: "publish\App\FunctionGenerator.exe"; DestDir: "{app}"; Components: funcgen; Flags: ignoreversion
Source: "publish\App\FunctionGenerator.dll"; DestDir: "{app}"; Components: funcgen; Flags: ignoreversion
Source: "publish\App\FunctionGenerator.deps.json"; DestDir: "{app}"; Components: funcgen; Flags: ignoreversion
Source: "publish\App\FunctionGenerator.runtimeconfig.json"; DestDir: "{app}"; Components: funcgen; Flags: ignoreversion
Source: "publish\App\rtt-cli.exe"; DestDir: "{app}"; Components: rttcli; Flags: ignoreversion
Source: "publish\App\rtt-cli.dll"; DestDir: "{app}"; Components: rttcli; Flags: ignoreversion
Source: "publish\App\rtt-cli.deps.json"; DestDir: "{app}"; Components: rttcli; Flags: ignoreversion
Source: "publish\App\rtt-cli.runtimeconfig.json"; DestDir: "{app}"; Components: rttcli; Flags: ignoreversion
Source: "publish\App\SerialAssistant.exe"; DestDir: "{app}"; Components: serialassistant; Flags: ignoreversion
Source: "publish\App\SerialAssistant.dll"; DestDir: "{app}"; Components: serialassistant; Flags: ignoreversion
Source: "publish\App\SerialAssistant.deps.json"; DestDir: "{app}"; Components: serialassistant; Flags: ignoreversion
Source: "publish\App\SerialAssistant.runtimeconfig.json"; DestDir: "{app}"; Components: serialassistant; Flags: ignoreversion
Source: "publish\App\Toolbox.Core.dll"; DestDir: "{app}"; Components: example examplewpf funcgen rttcli serialassistant; Flags: ignoreversion
Source: "publish\App\Toolbox.Ui.WinForms.dll"; DestDir: "{app}"; Components: example funcgen; Flags: ignoreversion
Source: "publish\App\Toolbox.Ui.WPF.dll"; DestDir: "{app}"; Components: examplewpf; Flags: ignoreversion
Source: "publish\App\Svg.dll"; DestDir: "{app}"; Components: example; Flags: ignoreversion
Source: "publish\App\System.IO.Ports.dll"; DestDir: "{app}"; Components: serialassistant; Flags: ignoreversion
Source: "publish\App\ICSharpCode.AvalonEdit.dll"; DestDir: "{app}"; Components: serialassistant; Flags: ignoreversion
; deps.json prefers the rid=win asset over the root dll; the unix/mac native libs are dead weight on Windows and stay out
Source: "publish\App\runtimes\win\*"; DestDir: "{app}\runtimes\win"; Components: serialassistant; Flags: ignoreversion recursesubdirs
Source: "publish\App\ExCSS.dll"; DestDir: "{app}"; Components: example; Flags: ignoreversion
Source: "publish\App\Assets\*"; DestDir: "{app}\Assets"; Components: example; Flags: recursesubdirs ignoreversion createallsubdirs

[Icons]
Name: "{group}\Example (WinForms)"; Filename: "{app}\Example.exe"; Components: example
Name: "{group}\ExampleWPF"; Filename: "{app}\ExampleWPF.exe"; Components: examplewpf
Name: "{group}\Function Generator"; Filename: "{app}\FunctionGenerator.exe"; Components: funcgen
Name: "{group}\Serial Assistant"; Filename: "{app}\SerialAssistant.exe"; Components: serialassistant
Name: "{autodesktop}\Example (WinForms)"; Filename: "{app}\Example.exe"; Tasks: desktopicon_example
Name: "{autodesktop}\ExampleWPF"; Filename: "{app}\ExampleWPF.exe"; Tasks: desktopicon_examplewpf
Name: "{autodesktop}\Function Generator"; Filename: "{app}\FunctionGenerator.exe"; Tasks: desktopicon_funcgen
Name: "{autodesktop}\Serial Assistant"; Filename: "{app}\SerialAssistant.exe"; Tasks: desktopicon_serialassistant

