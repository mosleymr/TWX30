# TWX AI

`twxai` is an external expansion-module project for TWX30.

The first module in this repo is an Ollama-backed assistant that:

- watches a live `GameInstance`
- keeps a rolling gameplay transcript
- reads `script.html` from the active TWX scripts directory
- answers questions from the MTC `AI` menu using local Ollama

This project intentionally lives outside the main TWX30 tree so it can evolve
independently while still plugging into the shared expansion-module framework.

## Layout

- `src/TwxAi.Module`
  The first module implementation.
- `docs`
  Setup and usage notes.

## Current scope

The first pass is focused on:

- passive observation of the current game session
- grounded Q&A against script reference material and recent gameplay
- per-game transcript storage under the module data directory

It does not yet take autonomous gameplay actions.

## Build

From the repo root:

```bash
dotnet build src/TwxAi.Module/TwxAi.Module.csproj -c Debug
```

## Install

Copy the module output folder into one of the TWX30 module folders, for example:

- `~/Library/twxproxy/modules/twxai`
- `~/Library/MTC/modules/twxai`
- `<ProgramDir>/modules/twxai`

The entire output folder should be copied, not just the DLL, so any adjacent
dependency files remain available.

## Configuration

On first run the module creates:

- `ModuleDataDirectory/twxai.json`
- `ModuleDataDirectory/gameplay.log`
- `ModuleDataDirectory/knowledge/`

The default config targets local Ollama at `http://127.0.0.1:11434` and uses
model `llama3.2`.

Additional gameplay or reference notes can be dropped into the `knowledge`
directory as `.md`, `.txt`, or `.html` files.

## MTC usage

After the module is installed and MTC is restarted:

1. open a game as usual
2. use the top-level `AI` menu
3. choose `TWX AI Assistant`

The first pass is question-and-answer only. It observes the live game and uses
that context when answering, but it does not yet send gameplay commands on its
own.
