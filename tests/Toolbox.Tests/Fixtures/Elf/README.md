# ELF fixtures

Real firmware images the Elf submodule's tests validate against. Both are builds of
the same STM32H743 project; they are committed because the tests assert constants
against them (symbol addresses, sizes, section byte offsets).

| File | Toolchain | Role |
|---|---|---|
| `stm32-project.axf` | Keil (armclang/armlink), 416,936 B | positive path: `_SEGGER_RTT` = 0x24000070, size 168 (ground truth: the project's armlink .map, not committed) |
| `stm32-project.elf` | arm-none-eabi-gcc 14.2.1 (GNU ld), 3,180,256 B | full-table symbol cross-check + negative path (this build does not compile SEGGER_RTT.c, so `_SEGGER_RTT` is absent) |
| `stm32-project.elf.readelf.txt` | GNU binutils readelf -sW output of the .elf above | independent ground truth for the symbol table |

## Regenerating the snapshot

```
readelf -sW stm32-project.elf > stm32-project.elf.readelf.txt
```

Snapshot produced with MinGW-w64 binutils (GNU readelf 2.44-ish, mingw build, CRLF
output). The snapshot is a version-specific artifact: if you regenerate it with a
different binutils, re-run the whole test suite - the parser in the test mirrors this
exact column layout.
