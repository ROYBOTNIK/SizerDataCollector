# Next Agent Context

Last updated: 2026-06-28
Active item: none
Status: documentation reality audit completed

## Read First

- `agent-workspace/AGENTS.md`
- `agent-workspace/CONTEXT.md`
- `agent-workspace/orchestrator.md`
- Active backlog item, when one is named here

## Current Decisions

- Every tick must rewrite this file before ending.
- The orchestrator may choose `A = no work`, `B = one item`, or `AB = 2-3 related items`.
- `0005` is done. README and remote bundle docs now match current solution/DB commands.
- `0006` tracks the unsupported `scripts/install-production.ps1` `preflight` call.
- Stop before production services, production DB writes, credentials, generated WCF from unverified endpoints, or customer-facing compatibility claims.

## Touched Files

- `README.md`
- `scripts/REMOTE_BUNDLE_README.md`
- `agent-workspace/backlog/items/0005-documentation-reality-audit.md`
- `agent-workspace/backlog/items/0006-install-script-preflight-command-gap.md`
- `agent-workspace/stages/05_review/output/0005-documentation-reality-audit-review.md`
- `agent-workspace/state/next-agent-context.md`

## Checks

- `git diff --check` passed; only CRLF normalization warnings.
- Stale-command search found no live `db apply-schema` docs outside backlog/review history.
- `dotnet test SizerDataCollector.sln --no-restore` passed, 30 tests.

## Next Action

Process `0006` install script preflight gap, or add the first implementation item from the .NET migration plan.
