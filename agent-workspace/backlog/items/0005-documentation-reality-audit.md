# Documentation Reality Audit

Status: done
Priority: P1
Source: inbox note `update-workflow-documentation.md`
Goal: review repo documentation and update it to match the current production app, commands, references, and supported workflows.

## User Note

> Review all the documentation for this repo and update it to match the reality of what we have left over in the working repo.
>
> You should make sure they are formatted correctly, have the correct commands and references and actually do things in the application as it is right now.

## Scope

- Inventory `README.md`, `MD-DOCS/*.md`, `docs/*.md`, `scripts/*.md`, and `agent-workspace/*.md`.
- Verify commands against current solution/project files and service CLI entry points.
- Fix broken references, stale GUI/WPF references, retired console-only guidance, and formatting drift.
- Split high-risk or code-affecting findings into separate backlog items.

## Acceptance Checks

- Documentation updates are scoped and reviewable.
- Commands named in docs map to current source or are marked historical/future.
- `dotnet test SizerDataCollector.sln --no-restore` passes when docs touch operational guidance.
- No production DB or service is touched.

Protected action: no
Decision: minimal change
Workset: none

## Work Done

- Corrected `README.md` to match the current solution contents: `SizerDataCollector.sln` includes the test project; installer work lives in `OptiFresh.OeeSuite.sln`.
- Corrected `scripts/REMOTE_BUNDLE_README.md` to remove the stale `db apply-schema` command and use current DB commands.
- Captured the unsupported `scripts/install-production.ps1` `preflight` call as backlog item `0006`.
