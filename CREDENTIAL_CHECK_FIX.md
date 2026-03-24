# Credential Check Fix for Relog Loop

## Problem
Mom.ts would attempt to relog when disconnected from the game server, even if login credentials (username, password, game letter) were not configured. This caused an endless loop:

1. Script loads and sets `$doRelog = TRUE` if config file exists (line 11288)
2. Script detects `CONNECTED <> TRUE` at `:wait_for_command` (line 885-887)
3. Jumps to `:relog_attempt` without verifying credentials are configured
4. Relog attempts fail repeatedly, triggering delay timers
5. Loop repeats endlessly, flooding console with variable assignments

## Root Cause
The original flow had two issues:

### Issue 1: Missing Credential Validation Before Relog
At `:wait_for_command` (line 885-887), the script checked:
```typescript
if ((CONNECTED <> TRUE) AND ($doRelog = TRUE))
    goto :relog_attempt
end
```

This jumped directly to relog without verifying that `$username`, `$password`, and `$letter` were properly configured.

### Issue 2: Pregame Menu Only Shown When Not Connected
At `:initiate_bot` (line 11482-11483), pregame menu was only shown if:
```typescript
if (CONNECTED <> TRUE)
    goto :pregameMenuLoad
end
```

But if you connected via telnet AFTER loading the script, or if credentials were never configured during initial setup, the pregame menu would be skipped.

## Solution Implemented

### 1. Credential Validation in `:wait_for_command` (lines 885-903)
Added comprehensive credential check before attempting relog:

```typescript
# Check if relog credentials are configured before attempting relog
if ((CONNECTED <> TRUE) AND ($doRelog = TRUE))
    # Verify we have login credentials before trying to relog
    # Check for empty strings OR system constant placeholder values
    if (($username = "") OR ($letter = "") OR ($password = ""))
        echo "{" $bot_name "} - Auto Relog disabled: Missing login credentials*"
        echo "{" $bot_name "} - Please configure username, password, and game letter in pregame menu*"
        setVar $doRelog FALSE
        saveVar $doRelog
    elseif (($username = "LOGINNAME") OR ($letter = "GAME") OR ($password = "PASSWORD"))
        echo "{" $bot_name "} - Auto Relog disabled: Credentials contain default values*"
        echo "{" $bot_name "} - Please configure username, password, and game letter in pregame menu*"
        setVar $doRelog FALSE
        saveVar $doRelog
    else
        goto :relog_attempt
    end
end
```

**What this does:**
- Checks if credentials are empty strings (not configured)
- Checks if credentials are system constant placeholders (LOGINNAME, GAME, PASSWORD)
- If invalid, disables auto-relog and notifies user
- Saves `$doRelog = FALSE` to prevent future relog attempts
- Only proceeds to `:relog_attempt` if credentials are properly configured

### 2. Enhanced Pregame Menu Logic in `:initiate_bot` (lines 11480-11494)
Always show pregame menu if credentials are missing, regardless of connection status:

```typescript
:initiate_bot
# Always show pregame menu if credentials are not configured, regardless of connection status
# Check for empty strings OR system constant placeholder values
if (($username = "") OR ($letter = "") OR ($password = ""))
    echo "{" $bot_name "} - Login credentials not configured, showing pregame menu*"
    goto :pregameMenuLoad
end
if (($username = "LOGINNAME") OR ($letter = "GAME") OR ($password = "PASSWORD"))
    echo "{" $bot_name "} - Login credentials contain default values, showing pregame menu*"
    goto :pregameMenuLoad
end
# Or show pregame menu if not connected to server
if (CONNECTED <> TRUE)
    goto :pregameMenuLoad
end
```

**What this does:**
- **First priority:** Check if credentials are missing or default values
- Show pregame menu immediately if credentials are invalid
- **Second priority:** Check if not connected to server
- Show pregame menu if no server connection
- This ensures proper setup flow regardless of when/how the script is loaded

## Benefits

### 1. Prevents Endless Relog Loops
- Script will no longer attempt to relog without proper credentials
- Eliminates the rapid delay trigger firing that caused console spam
- Works with the loop detection already implemented in Script.cs

### 2. Clear User Feedback
- User is notified when credentials are missing
- Clear instructions to configure credentials in pregame menu
- Auto-relog is automatically disabled and saved when credentials are invalid

### 3. Handles Edge Cases
- Works whether credentials are empty strings or system constant placeholders
- Handles both initial setup and post-disconnect scenarios
- Ensures pregame menu is shown when needed, regardless of connection timing

## User Experience

### First Time Setup
1. User loads mom.ts without any configuration
2. Credentials are empty or default values
3. Script detects this at `:initiate_bot` and shows pregame menu immediately
4. User configures username, password, game letter
5. Script saves configuration and continues normally

### Existing Configuration Without Credentials
1. User loads mom.ts with config file but missing login credentials
2. Script detects invalid credentials at `:initiate_bot`
3. Shows pregame menu to collect credentials
4. Once configured, auto-relog is enabled

### Server Disconnection Without Credentials
1. Script is running, server disconnects
2. At `:wait_for_command`, script detects disconnection
3. Before attempting relog, validates credentials
4. If invalid, disables auto-relog and notifies user
5. User must configure credentials before relog will work

## Testing

### Compile Verification
```bash
cd /Users/mosleym/Code/twxproxy/TWX30/Source/TWXC
dotnet run /Users/mosleym/Code/twxproxy/TWX30/Source/mom.ts
```

**Result:** 
- Lines: 11527 ✓
- Definitions: 14311 ✓
- Commands: 14288 ✓
- No compilation errors ✓

### Test Scenarios
1. **Load with no credentials:** Should show pregame menu immediately
2. **Load with partial credentials:** Should detect and show pregame menu
3. **Disconnect with no credentials:** Should disable relog and notify user
4. **Disconnect with valid credentials:** Should attempt normal relog

## Files Modified
- `/Users/mosleym/Code/twxproxy/TWX30/Source/mom.ts`
  - Lines 885-903: Added credential validation in `:wait_for_command`
  - Lines 11480-11494: Enhanced pregame menu logic in `:initiate_bot`

## Related Fixes
This fix works in conjunction with:
- **Loop Detection** in Script.cs (RELOG_LOOP_FIX.md)
- **Variable Debugging Control** to reduce console spam

Together, these fixes prevent endless relog loops, provide clear user feedback, and ensure proper credential configuration before any relog attempts.
