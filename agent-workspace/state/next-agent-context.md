# Next Agent Context

Last updated: 2026-06-28
Active item: none
Status: inventory workflow guard completed

## Read First

- `agent-workspace/AGENTS.md`
- `agent-workspace/CONTEXT.md`
- `agent-workspace/orchestrator.md`
- Active backlog item, when one is named here

## Current Decisions

- Every tick must rewrite this file before ending.
- The orchestrator may choose `A = no work`, `B = one item`, or `AB = 2-3 related items`.
- Inbox note `update-inventory.md` became done backlog item `0008`.
- `0007` is ready: convert production projects to SDK-style `net48` with `PackageReference`.
- Do not retarget to .NET 10 in `0007`; keep the first implementation step build-system only.
- Future codebase-shape edits must update `_config/repo-inventory.md` or explain why not applicable.
- Stop before production services, production DB writes, credentials, generated WCF from unverified endpoints, or customer-facing compatibility claims.

## Touched Files

- `agent-workspace/AGENTS.md`
- `agent-workspace/orchestrator.md`
- `agent-workspace/stages/04_execute/CONTEXT.md`
- `agent-workspace/stages/05_review/CONTEXT.md`
- `agent-workspace/skills/sizer-production-workspace/SKILL.md`
- `agent-workspace/backlog/items/0008-update-inventory-workflow-guard.md`
- `agent-workspace/stages/05_review/output/0008-update-inventory-workflow-guard-review.md`
- `agent-workspace/state/next-agent-context.md`

## Checks

- Inventory-rule search confirmed the guard in AGENTS, orchestrator, execute/review stages, and the repo-local skill.
- `git diff --check` passed; only CRLF normalization warnings.
- Ready backlog check shows only `0007`.

## Next Action

Process `0007` with `msbuild-modernization`; stop before production services, DBs, credentials, or unverified endpoints.
