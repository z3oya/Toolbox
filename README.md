# Toolbox

A basic C# solution structure.

## Structure

- ``src/Toolbox.Core`` - shared pure-logic library (no UI)
- ``src/Toolbox.Ui.WinForms`` - WinForms control library (SVG control, etc.)
- ``src/Toolbox.Ui.WPF`` - WPF control library (vector circle control, etc.)
- ``src/Tools/Example`` - standalone WinForms tool (exe)
- ``src/Tools/ExampleWPF`` - standalone WPF tool (exe)
- ``src/Tools/FunctionGenerator`` - standalone WinForms function generator (UDP PCM output)
- ``src/Tools/rtt-cli`` - standalone console tool for SEGGER J-Link RTT (interactive terminal, device database listing, one-shot send)
- ``src/Tools/SerialAssistant`` - standalone WPF serial port debug assistant (ASCII/HEX, line numbers, counters, logging)
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

````dotnet run --project src/Tools/rtt-cli -- --help````

````dotnet run --project src/Tools/SerialAssistant````

## rtt-cli (J-Link RTT terminal)

Requires a SEGGER J-Link software installation (the DLL is auto-detected under the SEGGER
install roots; pass ``--dll <path>`` otherwise) and a J-Link probe. Core flows:

````rtt-cli list-devices --filter stm32h743````

````rtt-cli --chip STM32H743XI````

````rtt-cli --chip STM32H743XI --hex --log capture.bin````

Data goes to stdout (pipeable), diagnostics to stderr; ``--eol``/``--encoding``/``--rtt-addr``/``--rtt-range``/``--sn`` refine the link, see ``--help``.

### Configuration file (--config)

Repeating the same options on every invocation gets old: ``-c/--config`` loads option
defaults from a JSON file. Without it, ``rtt-cli`` picks up ``./.rttsh.config.json``
automatically when the file is present. Command-line arguments always win over file values.

```json
{
  "chip": "STM32H743XI",
  "channel": 1,
  "speed": 4000,
  "interface": "swd",
  "encoding": "utf8",
  "eol": "lf",
  "rttAddr": "0x20000000"
}
```

Settable keys mirror the CLI options in camelCase (``interface`` is what the help shows as ``--if``): ``chip``, ``speed``, ``interface``, ``rttAddr``,
``rttRange`` (hex strings like ``"0x20000000"``), ``sn``, ``channel``, ``dll``,
``encoding``, ``eol``, ``log``, ``wait``, ``scriptTimeout``. Unknown or misspelled keys
are errors. ``hex``, ``tui``, ``reset`` and ``filter`` are deliberately not settable from
a file.

### rtt-cli script (Lua automation)

```
rtt-cli script smoke.lua --chip STM32H743XI
```

Inline one-liners skip the file entirely:

```
rtt-cli script --eval 'rtt.send("led r on"); rtt.expect("LED r on", 500)' --chip STM32H743XI
```

`--eval` is mutually exclusive with the file path; everything else (rtt.* API,
options, exit codes) is identical. Errors report as ``eval:<line>:``. Run
``rtt-cli manual`` for the full reference: configuration-file keys, the rtt.*
API, and a script-writing guide (send/expect pairing, captures, negative
assertions, pattern pitfalls).

`rtt` API: `send(text)` (appends `--eol`), `send_hex("DE AD")` (lossless binary TX), `log(line)`
(stderr), `wait(ms)` -> new bytes as text (ASCII-reliable) or `""` (passive tap),
`wait_hex(ms)` -> same window as hex text (byte-exact binary RX), `expect(pattern, ms=1000)` ->
text through the match end (consuming), `now()`, `sleep(ms)`, `exit(code)`.
Exit codes: 0 ok, 1 failure, 2 usage. First Ctrl+C asks the script to stop at the
next rtt.* boundary, second Ctrl+C hard-exits.

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
