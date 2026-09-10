# Toolbox

A basic C# solution structure.

## Structure

- ``src/Toolbox`` - WinForms main application
- ``src/Tools/Example`` - example tool (UserControl library)
- ``tests/Toolbox.Tests`` - xUnit test project

Add new tools under ``src/Tools/<ToolName>`` and reference them from the main app.

## Build

````dotnet build````

## Test

````dotnet test````

## Run

````dotnet run --project src/Toolbox````
