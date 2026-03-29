# TWX Proxy 3.0

Trade Wars 2002 Proxy Server with scripting support - Complete rewrite in .NET 10.0 (C#)

**Version:** 3.0.1  
**Original Author:** Remco Mulder  
**Primary 3.0 Author:** Matt Mosley (Shadow)

**License:** GPL v2+

## Overview

TWX Proxy 3.0 is a complete modern rewrite of the classic TWXProxy application, converting the original Delphi codebase to C# .NET 10.0. It provides a proxy server for Trade Wars 2002 with powerful scripting capabilities and a modern cross-platform user interface.

## Project Structure

The TWX30 solution consists of three main executables and a core library:

```
TWX30/
├── TWXProxy.csproj          # Core library (non-UI components)
│   ├── Ansi.cs              # ANSI color/formatting
│   ├── Database.cs          # Game database management
│   ├── Script.cs            # Script execution engine
│   ├── ScriptCmd.cs         # Command/system constant registry
│   ├── ScriptCmp.cs         # Script compiler
│   └── TCP.cs               # TCP connection handling
│
├── TWXP/                    # Main application (proxy server + UI)
│   ├── TWXP.csproj          # .NET MAUI cross-platform app
│   ├── Models/              # Data models
│   ├── Services/            # Business logic
│   ├── ViewModels/          # MVVM view models
│   ├── Pages/               # UI pages
│   └── Platforms/           # Platform-specific code
│       ├── MacCatalyst/     # macOS support
│       └── Windows/         # Windows support
│
├── TWXC/                    # Script compiler executable
│   ├── TWXC.csproj          # Command-line compiler
│   └── Program.cs           # Compiles .ts → .cts
│
└── TWXD/                    # Script decompiler executable
    ├── TWXD.csproj          # Command-line decompiler
    ├── Program.cs           # Entry point
    └── ScriptDecompiler.cs  # Decompiles .cts → .ts
```

## Executables

### 1. TWXP (Main Application)
- **Purpose:** Proxy server with graphical user interface
- **Features:**
  - Cross-platform (macOS and Windows)
  - Manage multiple game configurations
  - Start/stop/reset game proxies
  - Connection and database settings
  - Auto-connect on startup
- **Output:** `twxp` (executable)
- **Technology:** .NET MAUI

### 2. TWXC (Compiler)
- **Purpose:** TWX Script compiler
- **Features:**
  - Compiles `.ts` script files to `.cts` bytecode
  - 153 commands + 130+ system constants
  - Array indexing support
  - Concatenation operators
  - Control flow (IF/WHILE/ELSE)
  - Comment support (# and //)
- **Usage:** `twxc <scriptfile.ts>`
- **Output:** `twxc` (command-line tool)

### 3. TWXD (Decompiler)
- **Purpose:** TWX Script decompiler
- **Features:**
  - Decompiles `.cts` bytecode back to `.ts` source
  - Preserves array indexes
  - Reconstructs concatenation chains
  - Round-trip compilation support
- **Usage:** `twxd <scriptfile.cts>`
- **Output:** `twxd` (command-line tool)

## Building

### Prerequisites
- .NET 8.0 SDK
- Visual Studio 2022 (Windows) or Visual Studio for Mac
- macOS 14.0+ or Windows 10.0.19041.0+

### Build All Projects
```bash
cd TWX30
dotnet build
```

### Build Individual Projects
```bash
# Core library
dotnet build TWXProxy.csproj

# Main application (macOS)
dotnet build TWXP/TWXP.csproj -f net8.0-maccatalyst

# Main application (Windows)
dotnet build TWXP/TWXP.csproj -f net8.0-windows10.0.19041.0

# Compiler
dotnet build TWXC/TWXC.csproj

# Decompiler
dotnet build TWXD/TWXD.csproj
```

### Run Applications
```bash
# Run main app (macOS)
dotnet run --project TWXP/TWXP.csproj -f net8.0-maccatalyst

# Run main app (Windows)
dotnet run --project TWXP/TWXP.csproj -f net8.0-windows10.0.19041.0

# Compile a script
dotnet run --project TWXC -- myscript.ts

# Decompile a script
dotnet run --project TWXD -- myscript.cts
```

## Key Features

### Script Compilation (TWXC)
- **Full TWX script syntax support** including all 153 commands
- **System constants** for game data access (SECTOR.WARPS, SHIP.HOLDS, etc.)
- **Array indexing** with multi-level support: `SECTOR.WARPS[CURRENTSECTOR][$i]`
- **Concatenation operators** with `&` for string building
- **Control flow** with IF/WHILE/ELSEIF/ELSE/END macros
- **Comments** using `#` (full line) or `//` (inline)
- **Label support** for GOTO/GOSUB
- **Include files** for modular scripts
- **Bytecode output** to encrypted .cts format

### Script Decompilation (TWXD)
- **Full bytecode reconstruction** to readable source
- **Array index preservation** showing all indexing operations
- **Special character handling** (* for carriage return, \\t for tab)
- **Variable name recovery** for $vars and %progvars
- **System constant names** instead of numeric IDs
- **Round-trip capable** - decompiled code recompiles successfully

### Proxy Server (TWXP)
- **Multi-game support** - run multiple game instances simultaneously
- **Background operation** - all game interaction happens in background
- **Auto-connect** - games marked for auto-start connect on launch
- **Status monitoring** - real-time status indicators per game
- **Configuration persistence** - saves game configs to JSON
- **Cross-platform UI** - native experience on macOS and Windows

## Script Language

TWX scripts use a simple command-based syntax:

```twx
# Example TWX script
setvar $sector 1
while ($sector <= SECTORS)
    if (SECTOR.WARPS[$sector][1] > 0)
        echo "Sector "&$sector&" has warps"
    end
    add $sector 1
end
halt
```

### Command Types
- **Variables:** `$localvar`, `%globalvar`
- **System Constants:** `CURRENTSECTOR`, `SHIP.HOLDS`, `SECTOR.WARPS`
- **Operators:** `+`, `-`, `*`, `/`, `<`, `>`, `=`, `<>`, `AND`, `OR`
- **String Concatenation:** `&`
- **Control Flow:** `IF`, `WHILE`, `ELSEIF`, `ELSE`, `END`
- **Comments:** `# full line comment`, `// inline comment`

## Configuration

### Game Configurations (TWXP)
Stored in: 
- **macOS:** `~/Library/Application Support/TWXP/gameconfigs.json`
- **Windows:** `%APPDATA%/TWXP/gameconfigs.json`

### Script Files
- **Source:** `.ts` files (text-based TWX scripts)
- **Compiled:** `.cts` files (encrypted bytecode)

## Development Status

### ✅ Completed
- Core script compilation engine with all 153 commands
- Script decompiler with full round-trip support
- Array indexing compilation and decompilation
- Concatenation operator handling
- Control flow macros (IF/WHILE/ELSE)
- System constant support (130+ constants)
- Cross-platform MAUI UI framework
- Game configuration management
- Multi-game proxy service architecture
- TCP connection implementation
- Database integration with proxy
- Script execution engine integration
- Proxy server to game connection
- Runtime command execution

## Migration from TWXProxy 2.x

This is a complete rewrite, not a drop-in replacement. Key differences:
- **Platform:** .NET 10.0 (C#) instead of Delphi
- **UI:** Cross-platform MAUI instead of Windows-only VCL
- **Architecture:** Modular design with separate compiler/decompiler tools
- **Scripts:** Bytecode format maintained for compatibility

## Credits

- **Original TWXProxy:** Remco Mulder
- **TWX 2.6 and 2.7:** David O. McCartney (MicroBlaster)
- **License:** GNU General Public License v2 or later

## License

TWXProxy is licensed under the GNU General Public License v2+.  
See LICENSE file for full text.

Copyright (C) 2005 Remco Mulder  
Copyright (C) 2025 David O. McCartney
Copyright (C) 2026 Matt Mosley

