# Toolbox

A basic C# solution structure.

## Structure

- ``src/Toolbox.Core`` - shared pure-logic library (no UI)
- ``src/Toolbox.Ui.WinForms`` - WinForms control library (SVG control, etc.)
- ``src/Toolbox.Ui.WPF`` - WPF control library (vector circle control, etc.)
- ``src/Tools/Example`` - standalone WinForms tool (exe)
- ``src/Tools/ExampleWPF`` - standalone WPF tool (exe)
- ``tests/Toolbox.Tests`` - xUnit test project

Tools are standalone executables under ``src/Tools/<ToolName>`` and share logic via ``Toolbox.Core``.

## Build

````dotnet build````

## Test

````dotnet test````

## Run

````dotnet run --project src/Tools/Example````

````dotnet run --project src/Tools/ExampleWPF````
