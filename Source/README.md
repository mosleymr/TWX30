# TWX Proxy 3.0

Trade Wars 2002 proxy, scripting, compiler, and decompiler toolchain rewritten in C# on .NET 10.

**Version:** 3.0.1  
**Original Author:** Remco Mulder  
**C# Port / Ongoing Development:** Matt Mosley  
**TWX 2.6 / 2.7 Lineage:** David O. McCartney (MicroBlaster)  
**License:** GPL v2+

## Overview

TWX30 is the modern C# rewrite of the classic TWXProxy codebase. The repository includes:

- a shared Core runtime used by the proxy, compiler, decompiler, and client apps
- `TWXC`, which compiles TWX source scripts (`.ts`) to compiled bytecode (`.cts`)
- `TWXD`, which decompiles `.cts` files back to `.ts`
- `MTC`, an Avalonia desktop client that can connect to a proxy or run one natively
- `TWXP`, a multi threaded proxy with management interface

The current focus is compatibility with the original Pascal TWX 2.7 behavior, especially for script compile, decompile, and runtime execution.

## Project Structure

From the `Source/` directory:

```text
Source/
├── Core/                    # Shared proxy, runtime, compiler, decompiler support code
├── MTC/                     # Avalonia desktop client
├── TWXC/                    # Command-line compiler
├── TWXD/                    # Command-line decompiler
├── TWXP/                    # MAUI app shell
├── Test/                    # Focused regression / probe harnesses
└── TWXProxy.csproj          # Shared Core library project
```

## Main Components

### TWXProxy Core

- shared script runtime used by the embedded proxy and supporting tools
- compiler and bytecode loader
- database and game-state support
- trigger, menu, variable, and persistence logic

### TWXC

- compiles `.ts` scripts to `.cts`
- supports include trees
- preserves Pascal-compatible output where parity has been implemented

Usage:

```bash
twxc myscript.ts
```

### TWXD

- decompiles `.cts` scripts to `.ts`
- reconstructs include layouts
- supports whitespace-compaction mode for cleaner output when needed

Usage:

```bash
twxd myscript.cts
twxd --compact-whitespace myscript.cts
twxd --in-place --backup-existing myscript.cts
twxd --output-dir /tmp/twxd-out myscript.cts
```

### MTC

- Avalonia desktop client
- can connect to a proxy or host an embedded proxy
- uses the shared Core runtime for script execution and database behavior

### TWXP

- MAUI-based UI shell still included in the repository
- currently targets `net10.0-maccatalyst`

## Building

### Prerequisites

- .NET 10 SDK
- macOS for the current `TWXP` target
- Avalonia dependencies restored for `MTC`

### Build Everything

From `Source/`:

```bash
dotnet build
```

### Build Individual Projects

```bash
# Shared Core
dotnet build TWXProxy.csproj

# Compiler
dotnet build TWXC/TWXC.csproj

# Decompiler
dotnet build TWXD/TWXD.csproj

# Avalonia client
dotnet build MTC/MTC.csproj

# MAUI shell
dotnet build TWXP/TWXP.csproj -f net10.0-maccatalyst
```

### Publish Standalone Tool Binaries

From `Source/`:

```bash
# Compiler: osx-arm64, osx-x64, win-x64
./build-twxc.sh

# Decompiler: osx-arm64, osx-x64, win-x64
./build-twxd.sh

# Avalonia client release binaries: Source/bin/MTC/<rid>
./build-mtc.sh
```

### Run

```bash
# Compile a script
dotnet run --project TWXC/TWXC.csproj -- myscript.ts

# Decompile a script into the current directory (default)
dotnet run --project TWXD/TWXD.csproj -- myscript.cts

# Decompile in place intentionally
dotnet run --project TWXD/TWXD.csproj -- --in-place --backup-existing myscript.cts

# Run MTC
dotnet run --project MTC/MTC.csproj
```

## Script Support

The toolchain supports TWX script source files and compiled bytecode:

- source scripts: `.ts`
- compiled scripts: `.cts`
- include-based script trees
- Pascal-compatible bytecode loading for older compiled scripts

Current work in this branch has been focused on:

- byte-identical compiler parity for key Pascal-compiled scripts
- decompiler round-tripping for include-heavy scripts
- shared Core behavior so `.ts` source loads and `.cts` loads execute the same way in the embedded proxy

## Development Notes

- The shared Core library is the compatibility-critical layer.
- `TWXC`, `TWXD`, `MTC`, and the embedded proxy all depend on the same runtime behavior.
- VM optimization work is being tracked separately in [`../docs/vm-optimization-design.md`](../docs/vm-optimization-design.md).

## Migration Notes

This is not a drop-in binary replacement for TWX 2.x, but it aims to preserve script behavior and compiled-script compatibility as closely as possible.

Key differences from the original Pascal/Delphi codebase:

- implementation language is now C# on .NET 10
- the desktop client is Avalonia-based (`MTC`)
- compiler / decompiler / runtime share more code through the Core library
- Pascal `.cts` compatibility remains a first-class goal

## Credits

- **Original TWXProxy:** Remco Mulder
- **TWX 2.6 / 2.7:** David O. McCartney (MicroBlaster)
- **TWX30 C# Port:** Matt Mosley

## License

TWXProxy is licensed under the GNU General Public License v2 or later.  
See the repository license files for full terms.

Copyright (C) 2005 Remco Mulder  
Copyright (C) 2026 David O. McCartney  
Copyright (C) 2026 Matt Mosley
