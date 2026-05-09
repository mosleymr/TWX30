# Script Command Compatibility Notes

This file tracks intentional or currently accepted behavior differences between the
classic Pascal TWX 2.7 runtime and the current TWX30 C# runtime when those differences
matter to script authors or to debugging live script behavior.

## Command Table Shape

As of the current review:

- the shared TWX27/TWX30 command set still has matching command names
- the shared commands still have matching minimum and maximum argument counts

The differences documented below are behavioral differences in existing commands,
not command-name or command-arity drift.

## `LOGGING` vs `REQRECORDING`

These are related, but they control two different systems.

### Pascal TWX27 behavior

- `LOGGING ON` / `LOGGING OFF`
  - toggled the global session log capture flag
  - directly changed whether incoming game text was written to the `.log` file
- `REQRECORDING`
  - checked whether database recording was enabled
  - if recording was disabled, the script printed:
    - `This script requires recording to be enabled`
  - then the script stopped

### Current TWX30 behavior

- `LOGGING ON` / `LOGGING OFF`
  - from inside a script, this is **script-scoped**
  - it does **not** flip the global menu logging flag
  - instead, it suppresses or unsuppresses session log writes for the current
    script/interpreter scope
  - the suppression is cleared automatically when the script exits
- `REQRECORDING`
  - is currently a compatibility stub
  - it does nothing and does not stop the script

### Practical meaning

- In TWX30, `LOGGING OFF` is safe for scripts that want to keep a noisy operation out of
  the session log without permanently changing the user's global logging preference.
- In TWX30, scripts cannot rely on `REQRECORDING` to enforce database-recording state the
  way they could in Pascal TWX27.

### Related menu options

TWX30 still keeps session logging and database recording as separate concepts:

- `L` in the proxy menu controls **session log capture**
- `E` in the proxy menu controls **database recording**

That separation matches the older product model, even though `REQRECORDING` is no longer
an active gate in the script command layer.

## `SOUND`

### Pascal TWX27 behavior

- `SOUND <filename>`
  - called the platform sound API and attempted to play the requested sound file

### Current TWX30 behavior

- `SOUND <filename>`
  - is currently a compatibility stub
  - writes a diagnostic line to the console/log path
  - does **not** currently play audio

### Practical meaning

- Scripts that use `SOUND` for real audible alerts will be quieter in TWX30 unless a
  separate UI or host layer handles that notification some other way.

## `GETCOURSE`

### Pascal TWX27 behavior

- `GETCOURSE`
  - returned an array including both the start sector and the destination sector
  - returned a scalar equal to hop count
  - chose among equal-length routes using Pascal breadth-first discovery order / warp order

### Current TWX30 behavior

- `GETCOURSE`
  - still returns an array including both the start sector and the destination sector
  - still returns hop count as the scalar value
  - now uses the shared bidirectional shortest-path routine
  - still guarantees a shortest-hop route
  - may choose a different **equal-length** route than Pascal TWX27

### Practical meaning

- Distance-sensitive scripts should still behave correctly.
- Scripts that depend on the exact route ordering of equal-length paths may see different
  first hops or different returned sector arrays.

## `DISCONNECT`

### Pascal TWX27 behavior

- `DISCONNECT`
  - with no parameter, used the soft client-close path
  - with the optional parameter present, used the hard disconnect path documented as
    disabling reconnects

### Current TWX30 behavior

- `DISCONNECT`
  - always disconnects the server session without stopping the whole proxy instance
  - only disables auto-reconnect when the optional parameter is explicitly `1`

### Practical meaning

- Scripts that call plain `DISCONNECT` should still disconnect normally.
- Scripts that relied on the **presence** of any optional parameter to suppress reconnect
  should use `DISCONNECT 1` in TWX30 if they need that behavior explicitly.

## Empty String vs `0`

This is an important edge case because script behavior depends both on the operator being
used and on whether a variable is still carrying numeric state from an earlier assignment.

### Pascal TWX27 behavior

- A newly created variable starts as numeric/string `0`.
- `setVar $x ""`
  - stores an empty string
  - clears the variable's numeric state
- Numeric conversion uses `TextToFloat(...)`
  - if the value is `""`, conversion fails and raises a script error
- `ISEQUAL`
  - with precision `0`, compares as strings
  - with nonzero precision, tries numeric compare first, then falls back to string compare
    if numeric conversion fails
- `ISNOTEQUAL`
  - tries numeric compare first
  - falls back to string compare if numeric conversion fails
- `ISGREATER`, `ISGREATEREQUAL`, `ISLESSER`, `ISLESSEREQUAL`
  - go straight through numeric conversion
  - so `""` is not treated as numeric `0` there; it is more likely to raise

### Current TWX30 behavior

- A newly created variable starts as numeric/string `0`.
- `setVar $x ""`
  - stores an empty string
  - clears the variable's numeric state
- `ISEQUAL`
  - explicitly refuses to treat `""` as numeric
  - falls back to string comparison
- `ISNOTEQUAL`
  - also refuses to treat `""` as numeric
  - falls back to string comparison
- `ISGREATER`, `ISGREATEREQUAL`, `ISLESSER`, `ISLESSEREQUAL`
  - currently use `DecValue` directly
  - and TWX30 `DecValue` currently coerces `""` to numeric `0`

### Current practical matrix

If a variable is literally holding the string `""`:

- In TWX27:
  - `if ($x = 0)` is false
  - `if ($x = "")` is true
  - `if ($x > 0)` is likely to error rather than quietly treat `""` as `0`
- In current TWX30:
  - `if ($x = 0)` is false
  - `if ($x = "")` is true
  - `if ($x > 0)` compares as `0 > 0`
  - `if ($x <= 0)` compares as `0 <= 0`

### Why this can feel inconsistent

- `setVar` preserves numeric state when copying from a numeric source variable.
  - So `setVar $x $y` can behave differently from `setVar $x ""`.
- Current TWX30 is internally inconsistent here:
  - equality-style operators treat `""` as a string
  - ordering/numeric operators currently treat `""` as numeric `0`

### Practical meaning

- If a script wants to detect "missing or blank" safely across both runtimes, prefer an
  explicit empty-string check such as:
  - `if ($x = "")`
- If a script wants "missing or zero" behavior, prefer spelling both cases out:
  - `if (($x = 0) or ($x = ""))`
- Do not assume current TWX30 numeric-ordering behavior on `""` is Pascal TWX27 parity.
