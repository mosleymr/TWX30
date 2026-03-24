# Relog Loop Fix Documentation

## Problem
When loading mom.ts in TWXP with a telnet client connected but NO server connection, the script enters an endless loop:
- `:wait_for_command` detects `CONNECTED <> TRUE` and `$doRelog = TRUE`
- Jumps to `:relog_attempt` which sets delay triggers (1.5 seconds)
- `:do_relog` tries to `connect`, fails, sets more delay triggers
- Delay triggers fire repeatedly, looping back through the same code
- Each iteration sets variables, flooding console with `[VAR ...]` output

## Solution Implemented

### 1. Loop Detection (lines 1250-1273 in Script.cs)
Tracks when the script repeatedly executes the same code position:
- Monitors `_codePos` and timestamps
- Counts iterations within 100ms windows
- After 50 rapid iterations at the same position, sends warning:
  ```
  *** WARNING: Possible script loop detected at position XXXX ***
  *** Script has executed the same position 50 times in rapid succession ***
  *** This may indicate an endless loop (e.g., relog attempts with no server) ***
  *** Script will continue, but consider stopping it if stuck ***
  ```

### 2. Variable Debugging Control (line 1404 in Script.cs)
The `[VAR ...]` output that floods the console is now controlled by a flag:
- `_enableVariableDebug = false` by default (line 722)
- To enable variable debugging for troubleshooting, set it to `true`
- This eliminates the console spam while preserving debugging capability

## Usage

### Normal Operation
- Loop detection runs automatically
- Variable debugging is disabled (no `[VAR ...]` spam)
- If script gets stuck in a loop, you'll see the warning message after ~50 iterations

### Troubleshooting Mode
To enable variable debugging:
1. Edit `/Users/mosleym/Code/twxproxy/TWX30/Source/Core/Script.cs` line 722
2. Change: `private bool _enableVariableDebug = false;` to `true`
3. Rebuild: `dotnet build TWXProxy.sln -c Debug`
4. All variable assignments will now be logged to client as `[VAR ID] = name (value='...')`

## How to Stop the Loop
If you see the loop warning while running mom.ts without a server connection:

### Option 1: Disable Auto-Relog
In the mom.ts pregame menu (before connecting to game):
- Access the bot configuration
- Set auto-relog to OFF or set doRelog = FALSE

### Option 2: Stop the Script
- Use TWX command: `stop mom.ts` (or whatever you named the script)
- Or disconnect from TWXP and reload

### Option 3: Connect to Server
- Set up the game server connection in TWXP
- The relog process will complete normally once server is available

## Technical Details

### Loop Detection Algorithm
```csharp
if (_codePos == _lastCodePos && (DateTime.Now - _lastLoopCheck).TotalMilliseconds < 100)
{
    _loopCounter++;
    if (_loopCounter >= MAX_LOOP_ITERATIONS)  // 50
    {
        // Send warning to client
        _loopCounter = 0;  // Reset after warning
    }
}
else
{
    _loopCounter = 0;  // Reset if moved or time passed
}
```

### Variable Debugging
```csharp
// Only logs if _enableVariableDebug == true
if (_enableVariableDebug && paramType == ScriptConstants.PARAM_VAR && param is VarParam varParam)
{
    server?.ClientMessage($"  [VAR {paramID}] = {varParam.Name} (value='{varParam.Value}')\r\n");
}
```

## Files Modified
- `/Users/mosleym/Code/twxproxy/TWX30/Source/Core/Script.cs`
  - Lines 717-722: Added loop detection fields
  - Lines 1250-1273: Loop detection logic in Execute()
  - Line 1404: Conditional variable debugging

## Future Enhancements
Consider adding to mom.ts:
- Retry counter for relog attempts
- Maximum relog attempts before giving up
- User prompt to continue/abort after N failed attempts
- Configurable relog delay (currently hardcoded at 1.5 seconds)
