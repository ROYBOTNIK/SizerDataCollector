# Next Agent Context

Last updated: 2026-06-28
Active item: none
Status: `0007` completed

## Read First

- `agent-workspace/AGENTS.md`
- `agent-workspace/CONTEXT.md`
- `agent-workspace/orchestrator.md`
- Active backlog item, when one is named here

## Current Decisions

- Production projects are now SDK-style `net48` with `PackageReference`; do not retarget to .NET 10 except through a new backlog item.
- Future codebase-shape edits must update `_config/repo-inventory.md`.
- Stop before production services, DB writes, credentials, generated WCF from unverified endpoints, force pushes, branch deletion, or customer-facing compatibility claims.

## Touched Files

- Production `.csproj` files and production `packages.config` removals
- `scripts/audit-packages-config-vulnerabilities.ps1`
- `agent-workspace/_config/repo-inventory.md`
- `agent-workspace/backlog/items/0007-convert-production-projects-sdk-style-net48.md`
- `agent-workspace/stages/05_review/output/0007-convert-production-projects-sdk-style-net48-review.md`

## Checks

Restore, Release build, tests, package vulnerability audit, SQL-copy check, WCF-link check, and legacy package-audit script passed.

## Next Action

Process inbox. If still empty, create/select the next ready backlog item before coding.
