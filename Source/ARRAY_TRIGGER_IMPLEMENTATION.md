# Array and Trigger Command Implementation

## Overview
Implemented full support for array operations and trigger management in the TWX script system.

## Implemented Commands

### Array Commands (2 commands)

#### SETARRAY
- **Syntax**: `SETARRAY var dimensions...`
- **Purpose**: Creates a multi-dimensional array with specified dimensions
- **Implementation**: Calls `VarParam.SetArray()` which manages memory allocation for array dimensions
- **Example**: `SETARRAY $myarray 10 5` creates a 10x5 two-dimensional array

#### SORT
- **Syntax**: `SORT sourceArray resultArray`
- **Purpose**: Sorts an array in ascending order
- **Implementation**: 
  - Collects all non-empty array elements from source
  - Sorts using standard string comparison
  - Stores results in result array variable
  - Sets result variable value to count of sorted items
- **Example**: `SORT $unsorted $sorted` sorts $unsorted and stores in $sorted

### Trigger Commands (8 commands)

#### SETTEXTTRIGGER
- **Syntax**: `SETTEXTTRIGGER name label [text]`
- **Purpose**: Creates a trigger that activates when text appears anywhere in the input stream
- **Implementation**: Calls `Script.SetTextTrigger()` which adds trigger to Text trigger list

#### SETTEXTLINETRIGGER
- **Syntax**: `SETTEXTLINETRIGGER name label [text]`
- **Purpose**: Creates a trigger that activates when text appears on a complete line
- **Implementation**: Calls `Script.SetTextLineTrigger()` which adds trigger to TextLine trigger list
- **Note**: If text is empty, trigger activates on every line

#### SETTEXTOUTTRIGGER
- **Syntax**: `SETTEXTOUTTRIGGER name label [text]`
- **Purpose**: Creates a trigger that activates when text is sent to the output stream
- **Implementation**: Calls `Script.SetTextOutTrigger()` which adds trigger to TextOut trigger list
- **Use Case**: Intercepting outgoing text before display

#### SETDELAYTRIGGER
- **Syntax**: `SETDELAYTRIGGER name delay label`
- **Purpose**: Creates a timer-based trigger that activates after specified milliseconds
- **Implementation**: Calls `Script.SetDelayTrigger()` which:
  - Creates trigger with specified delay interval
  - Sets up DelayTimer with elapsed event handler
  - Adds trigger to Delay trigger list
- **Example**: `SETDELAYTRIGGER timer1 5000 :handleTimeout` triggers after 5 seconds

#### SETEVENTTRIGGER
- **Syntax**: `SETEVENTTRIGGER name event label [param]`
- **Purpose**: Creates an event-based trigger that activates on specific game events
- **Implementation**: Calls `Script.SetEventTrigger()` which adds trigger to Event trigger list
- **Events**: TIME HIT, WARP, PORT, PLANET, etc.
- **Special**: "TIME HIT" events call `ModInterpreter.CountTimerEvent()`

#### SETAUTOTRIGGER
- **Syntax**: `SETAUTOTRIGGER name text response [lifecycle]`
- **Purpose**: Creates an automatic trigger that sends a response when text is matched
- **Implementation**: Calls `Script.SetAutoTrigger()` which adds trigger to TextAuto trigger list
- **Lifecycle**: Number of times trigger will fire before auto-removal (0 = infinite, default = 1)
- **Use Case**: Automated responses to game prompts

#### KILLTRIGGER
- **Syntax**: `KILLTRIGGER name`
- **Purpose**: Removes a specific trigger by name
- **Implementation**: Calls `Script.KillTrigger()` which:
  - Searches all trigger lists for matching name
  - Frees trigger resources (including timers)
  - Removes trigger from list
  - UnCounts timer events if "TIME HIT" trigger

#### KILLALLTRIGGERS
- **Syntax**: `KILLALLTRIGGERS`
- **Purpose**: Removes all triggers from the current script
- **Implementation**: Calls `Script.KillAllTriggers()` which:
  - Iterates through all trigger types
  - Frees all trigger resources
  - Clears all trigger lists
  - UnCounts all "TIME HIT" triggers

## File Structure

### New Files Created
1. **ScriptCmdImpl_Arrays.cs** (71 lines)
   - Contains implementation methods for array commands
   - `CmdSetArray_Impl()` - Array allocation
   - `CmdSort_Impl()` - Array sorting

2. **ScriptCmdImpl_Triggers.cs** (218 lines)
   - Contains implementation methods for trigger commands
   - 8 implementation methods (`*_Impl`)
   - Full error handling with descriptive messages

### Modified Files
1. **ScriptCmdImpl.cs**
   - Replaced array command stubs with calls to `*_Impl` methods
   - Replaced trigger command stubs with calls to `*_Impl` methods
   - Maintains stub interface for command routing

## Trigger System Architecture

### Trigger Types
The system supports 5 trigger types (from `TriggerType` enum):
1. **Text** - Matches text anywhere in input stream
2. **TextLine** - Matches complete lines
3. **TextOut** - Matches outgoing text
4. **TextAuto** - Automatic response triggers
5. **Delay** - Timer-based triggers
6. **Event** - Game event triggers

### Trigger Lifecycle
1. **Creation**: Trigger created via SET*TRIGGER command
2. **Storage**: Added to appropriate trigger list in `Script._triggers` dictionary
3. **Activation**: Monitored by `Script.CheckTriggers()` during text processing
4. **Execution**: When matched, script jumps to specified label via `GotoLabel()`
5. **Lifecycle Management**: Auto-removal after lifecycle count reaches 0
6. **Cleanup**: Manual removal via KILLTRIGGER or automatic via KILLALLTRIGGERS

### Integration Points
- **Script.cs**: Contains trigger management methods and data structures
- **CheckTriggers()**: Called from TextEvent, TextLineEvent, TextOutEvent, etc.
- **ModInterpreter**: Tracks timer events with CountTimerEvent/UnCountTimerEvent
- **DelayTimer**: System.Timers.Timer subclass for delay trigger management

## Array System Architecture

### VarParam Structure
- **_vars**: List of child VarParam objects for array elements
- **_arraySize**: Total allocated size of array
- **Indexing**: 1-based indexing (matches TWX Pascal original)
- **Multi-dimensional**: Recursive VarParam structure supports n-dimensions

### Array Operations
1. **SetArray()**: Allocates array structure with specified dimensions
2. **SetArrayFromStrings()**: Populates array from string list
3. **GetIndexVar()**: Retrieves array element by index path
4. **ArraySize**: Property returning current array size

## Build Status
✅ **Build Succeeded**: 0 errors, 16 pre-existing warnings

## Testing Recommendations
1. **Array Tests**:
   - Single dimension arrays: `SETARRAY $arr 10`
   - Multi-dimensional arrays: `SETARRAY $grid 5 5`
   - Array sorting: `SORT $unsorted $sorted`
   - Dynamic arrays from file: `READTOARRAY file.txt $arr`

2. **Trigger Tests**:
   - Text matching triggers: `SETTEXTTRIGGER mytrig :handler "Welcome"`
   - Line triggers: `SETTEXTLINETRIGGER linetrig :linehandler`
   - Delay triggers: `SETDELAYTRIGGER timer1 1000 :timeout`
   - Auto triggers: `SETAUTOTRIGGER auto1 "Password:" "secret" 1`
   - Event triggers: `SETEVENTTRIGGER event1 "TIME HIT" :timehit`
   - Trigger removal: `KILLTRIGGER mytrig`, `KILLALLTRIGGERS`

3. **Integration Tests**:
   - Trigger activation during script execution
   - Trigger lifecycle management
   - Multiple concurrent triggers
   - Trigger persistence across script pauses

## Notes
- All trigger methods already exist in Script.cs with full implementations
- Array functionality already exists in VarParam.cs (SetArray, SetArrayFromStrings)
- This implementation connects command stubs to existing core functionality
- Error handling wraps Script method exceptions with descriptive messages
- Follows existing patterns from ScriptCmdImpl_Network.cs and ScriptCmdImpl_Database.cs
