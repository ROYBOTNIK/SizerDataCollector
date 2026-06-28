# Next Agent Context

Last updated: 2026-06-28
Active item: none
Status: stocktake refreshed; migration item ready

## Read First

- `agent-workspace/AGENTS.md`
- `agent-workspace/CONTEXT.md`
- `agent-workspace/orchestrator.md`
- Active backlog item, when one is named here

## Current Decisions

- Every tick must rewrite this file before ending.
- The orchestrator may choose `A = no work`, `B = one item`, or `AB = 2-3 related items`.
- No actionable inbox note was present.
- `0007` is ready: convert production projects to SDK-style `net48` with `PackageReference`.
- Do not retarget to .NET 10 in `0007`; keep the first implementation step build-system only.
- Stop before production services, production DB writes, credentials, generated WCF from unverified endpoints, or customer-facing compatibility claims.

## Touched Files

- `agent-workspace/_config/repo-inventory.md`
- `agent-workspace/backlog/items/0007-convert-production-projects-sdk-style-net48.md`
- `agent-workspace/stages/05_review/output/0007-stocktake-ready-item-review.md`
- `agent-workspace/state/next-agent-context.md`

## Checks

- `git diff --check` passed; only CRLF normalization warnings.
- Ready backlog check shows only `0007`.

## Next Action

Process `0007` with `msbuild-modernization`; stop before production services, DBs, credentials, or unverified endpoints.
