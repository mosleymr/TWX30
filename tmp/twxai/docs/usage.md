# TWX AI Usage

## What the module sees

The module receives:

- the live `GameInstance`
- the active script directory
- the active database, when available
- a per-module data directory

It records recent server text and keeps a rolling transcript in
`gameplay.log`.

## How answers are grounded

The assistant currently uses:

1. `script.html` from the active TWX scripts directory
2. any extra docs in `knowledge/`
3. recent gameplay lines from the current session
4. the current chat conversation

The retrieval pass is intentionally simple and transparent so it is easy to
improve later.

## Suggested next references

When you find them, the best extra source files to add to `knowledge/` will be:

- gameplay guides
- bot usage docs
- port/haggle strategy references
- worldtrade or mombot reference notes

## Future direction

Likely next steps:

- summarize longer transcripts into persistent notes
- expose a small tool/action API for safe server commands
- let the assistant inspect more live game state from the database
- add a TWXP UI surface alongside the current MTC AI window
