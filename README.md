# Toolbox

A basic C# solution structure.

## Structure

- ``src/Toolbox.Core`` - shared pure-logic library (no UI)
- ``src/Toolbox.Ui.WinForms`` - WinForms control library (SVG control, etc.)
- ``src/Toolbox.Ui.WPF`` - WPF control library (vector circle control, etc.)
- ``src/Tools/Example`` - standalone WinForms tool (exe)
- ``src/Tools/ExampleWPF`` - standalone WPF tool (exe)
- ``src/Tools/FunctionGenerator`` - standalone WinForms function generator (UDP PCM output)
- ``src/Tools/SerialAssistant`` - standalone WinForms serial port debug assistant (ASCII/HEX, timestamps, counters, logging)
- ``tests/Toolbox.Tests`` - xUnit test project

Tools are standalone executables under ``src/Tools/<ToolName>`` and share logic via ``Toolbox.Core``.

## Build

````dotnet build````

## Test

````dotnet test````

## Run

````dotnet run --project src/Tools/Example````

````dotnet run --project src/Tools/ExampleWPF````

````dotnet run --project src/Tools/FunctionGenerator````

````dotnet run --project src/Tools/SerialAssistant````

## Package (installer with optional components)

Requires Inno Setup 6 (``scoop install innosetup``).

``powershell
cd installer
.\build-installer.ps1
``

Result: ``installer\dist\Toolbox-<version>-<flavor>-setup.exe`` (``framework`` or ``selfcontained``). During setup, users pick an install type (Full / Compact / Custom) and can check/uncheck each tool (Example, ExampleWPF, Function Generator, Serial Assistant) plus desktop shortcuts.

Use ``-SelfContained`` for a larger installer that runs without the .NET 10 Desktop Runtime.

## Function Generator UDP protocol

Packets: magic ``TFG1`` = ``0x54464731`` (u32 LE) | seq (u32 LE) | timestamp (u64 LE, monotonic) |
sample count (i32 LE) | payload of float32 LE samples (normalized ±1.0).
Default 48 kHz, 960 samples (20 ms) per packet.
