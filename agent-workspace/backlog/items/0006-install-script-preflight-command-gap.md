# Install Script Preflight Command Gap

Status: ready
Priority: P1
Source: documentation reality audit `0005`
Goal: fix or remove the production install script's call to a `preflight` CLI command that does not exist in `SizerDataCollector.Service.exe`.

## Scope

- `scripts/install-production.ps1`
- `SizerDataCollector.Service/Program.cs`
- README or deployment docs if the install flow changes

## Acceptance Checks

- `scripts/install-production.ps1` no longer calls an unsupported command, or the service CLI implements the expected preflight behavior.
- Any replacement uses existing safe commands such as `show-config`, `test-connections`, or documented dry-run checks where appropriate.
- `dotnet test SizerDataCollector.sln --no-restore` passes if service CLI code changes.

Protected action: no
Decision: minimal change should update the script or make preflight an alias only after reviewing deployment expectations.
Workset: none
