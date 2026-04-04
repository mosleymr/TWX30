# VM Optimization Design

## Status

- Baseline snapshot: `v3.0.0`
- Active development version: `3.0.1`
- Goal: improve VM load and execution performance without breaking compatibility with existing Pascal `.cts` files or current `.ts` source behavior

## Goals

- Preserve runtime compatibility for existing Pascal-compiled `.cts` files.
- Preserve compiler/decompiler compatibility for existing `.ts` and `.cts`.
- Improve startup cost for source-loaded scripts where possible.
- Improve steady-state VM throughput and reduce allocations in the execution hot path.
- Keep a Pascal-compatible compiler output mode for sharing scripts with older clients.

## Non-Goals

- Replacing the VM with a Roslyn/DLL backend in this phase.
- Breaking byte-identical Pascal `.cts` output in compatibility mode.
- Changing script-visible semantics for math, strings, arrays, triggers, labels, menus, or persistence.

## Compatibility Contract

### Must remain supported

- Pascal `.cts` versions 2 through 6
- Current TWX30 `.ts` source files, including include trees
- Current decompiler round-trip expectations
- Shared Core runtime behavior used by MTC, TWXC, TWXD, and the proxy

### Compiler modes

- `pascal` compatibility mode
  - emits legacy `.cts` bytecode layout
  - preserves current byte-identical behavior where already achieved
- `native` optimized mode
  - optional future format for TWX30-only use
  - can use more efficient encoding and metadata
  - loader must still accept legacy Pascal `.cts`

## Current Performance Costs

### Source load

`Source/Core/ScriptCmp.cs`

- `CompileFromFile()` reads, tokenizes, parses, lowers, and emits bytecode every time a `.ts` is loaded.
- Include-heavy scripts multiply that work during recursive compile.

### Compiled load

`Source/Core/ScriptCmp.cs`

- `LoadFromFile()` is mostly deserialization and is already substantially cheaper than source compile.
- It still leaves execution on the raw bytecode path.

### Runtime hot path

`Source/Core/Script.cs`

- `Execute()` re-parses bytecode every time it runs.
- Each instruction repeatedly:
  - reads fields from the raw byte array
  - resolves command metadata
  - resolves system constant metadata
  - rebuilds parameter collections
  - allocates arrays for dispatch and some index evaluation paths

## Optimization Strategy

## Phase 1: Measurement Harness

Add a dedicated measurement harness to measure:

- source compile time
- compiled load time
- prepared/decode time
- time to first `pause`/completion
- instruction count and allocation trends for representative scripts

Representative scripts:

- `2_WorldTrade.ts`
- Pascal `2_WorldTrade.cts`
- `mom_bot3_1045s.cts`
- other include-heavy and trigger-heavy scripts as needed

## Phase 2: Prepared Instruction IR

Add a load-time decode step that converts raw bytecode into an internal prepared representation:

- `PreparedScriptProgram`
- `PreparedInstruction`
- `PreparedParam`

The prepared representation should contain:

- instruction list in execution order
- raw bytecode offsets for compatibility with existing label locations
- pre-resolved command metadata when possible
- pre-parsed parameter metadata and nested index descriptors

Important rule:

- labels continue to use their original raw bytecode offsets so `goto`, `gosub`, `return`, triggers, and menu activation remain compatible with existing label tables

## Phase 3: Hot-Loop Allocation Reduction

Use the prepared representation to remove avoidable allocations:

- replace per-command `List<T>.ToArray()` dispatch allocation with reusable exact-size dispatch buffers
- reuse scratch `CmdParam` instances for constant, char, and sysconst values
- avoid reparsing parameter bytecode on every execution pass
- reduce repeated string/array creation where safe

Important rule:

- do not share mutable constant parameter instances across commands in a way that changes script-visible behavior

## Phase 4: Metadata Pre-Resolution

During prepare/decode:

- resolve `cmdID -> ScriptCmd`
- resolve sysconst IDs where possible
- keep parameter descriptors typed and pre-parsed

This removes repeated list lookups and repeated byte walking from the hot loop.

## Phase 5: Optional Native Script Format

After the prepared IR is stable:

- add an optional optimized TWX30-native format
- keep legacy Pascal `.cts` load support
- keep `twxc --format pascal` for old-client sharing
- allow `twxc --format native` for TWX30-only optimized output

Potential native-format advantages:

- direct serialized instruction table
- pre-parsed parameter descriptors
- compact label/index metadata
- no legacy byte-walking on load

## Proposed Runtime Shape

### ScriptCmp

- remains the owner of raw bytecode, labels, params, and include metadata
- gains a cached prepared program for execution

### Script

- continues to own runtime variable state, triggers, pause state, stacks, and menu/input state
- executes prepared instructions when available
- falls back to legacy raw bytecode execution if prepared decode fails

## Validation Matrix

Every VM change should be validated against:

- Pascal `.cts` load and execution
- TWXC-compiled `.cts` load and execution
- `.ts` source load and execution
- include-heavy scripts
- menu/input-heavy scripts
- trigger-heavy scripts
- savevar/loadvar behavior
- byte-identical compiler output in Pascal compatibility mode

## Suggested Milestones

1. Add the benchmark harness.
2. Add prepared instruction decode with runtime fallback.
3. Switch `Script.Execute()` to prepared instructions when available.
4. Eliminate per-dispatch array allocation.
5. Cache scratch params and pre-resolved command/sysconst metadata.
6. Benchmark before and after on the same script set.
7. Design the optional native output format only after the prepared VM path is stable.
