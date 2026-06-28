# Production Stocktake Hardening

Status: done
Priority: P1
Source: initial stocktake
Goal: remove the first blockers to calling SizerDataCollector production polished.

## Scope

- Replace sample secrets with placeholders in config/docs.
- Run package vulnerability/currency checks and record net48-compatible update options.
- Reconcile README and `MD-DOCS/DESIGN.md` with the current service-first CLI.
- Confirm `dotnet test SizerDataCollector.sln` is the standard local gate.

## Work Done

- Replaced the old hard-coded sample DB password with placeholder credentials in `App.config` and `README.md`.
- Added a historical note to `MD-DOCS/DESIGN.md` pointing agents to the current service-first CLI docs.
- Confirmed `dotnet list package --vulnerable --include-transitive` does not support the production `packages.config` projects; moved the scanner-path gap to follow-up backlog item `0004`.

## Acceptance Checks

- Tests pass or failures are documented with the root cause.
- No real-looking password remains in checked-in examples.
- Docs point agents to the current service CLI, not the older console-only design.
- No production DB or service is touched.

Protected action: no
Decision: minimal change
