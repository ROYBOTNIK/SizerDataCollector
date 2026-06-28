# Next Agent Context

Last updated: 2026-06-28
Active item: none
Status: install script preflight gap completed

## Read First

- `agent-workspace/AGENTS.md`
- `agent-workspace/CONTEXT.md`
- `agent-workspace/orchestrator.md`
- Active backlog item, when one is named here

## Current Decisions

- Every tick must rewrite this file before ending.
- The orchestrator may choose `A = no work`, `B = one item`, or `AB = 2-3 related items`.
- `0006` is done. `install-production.ps1` preflight now calls `SizerDataCollector.Service.exe show-config`.
- `show-config` is intentionally light; API/DB checks still need explicit approved targets.
- Stop before production services, production DB writes, credentials, generated WCF from unverified endpoints, or customer-facing compatibility claims.

## Touched Files

- `scripts/install-production.ps1`
- `agent-workspace/backlog/items/0006-install-script-preflight-command-gap.md`
- `agent-workspace/stages/05_review/output/0006-install-script-preflight-command-gap-review.md`
- `agent-workspace/state/next-agent-context.md`

## Checks

- PowerShell parser check passed for `scripts/install-production.ps1`.
- Unsupported preflight-command search returned no matches.
- `git diff --check` passed; only CRLF normalization warnings.

## Next Action

Add the first implementation item from the .NET migration plan, or refresh stocktake if no ready item exists.
