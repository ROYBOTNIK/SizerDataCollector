# Workspace Context

Purpose: turn rough notes and repo findings into production-ready SizerDataCollector changes with inspectable handoffs.

## Stages

| Stage | Job | Output |
| --- | --- | --- |
| `01_intake` | Turn inbox notes into backlog items. | `backlog/items/*.md` |
| `02_stocktake` | Refresh repo inventory and risks. | `_config/repo-inventory.md` |
| `03_plan` | Select one backlog item and define acceptance checks. | `stages/03_plan/output/*.md` |
| `04_execute` | Implement one scoped change. | repo diff |
| `05_review` | Build/test/review and decide approve, revise, or do nothing. | `stages/05_review/output/*.md` |
| `06_release` | Prepare commit, push, merge, and follow-up branch/worktree. | `stages/06_release/output/*.md` |

Every stage also updates `state/next-agent-context.md` before ending.

## Shared References

- `state/next-agent-context.md` is the first file to read after `AGENTS.md` and `CONTEXT.md`.
- `_config/method.md` explains how ICM, autoresearch, and autoreason are adapted here.
- `_config/backlog-rubric.md` defines backlog shape and approval gates.
- `_config/repo-inventory.md` is the current stocktake.
- `../MD-DOCS/AI_AGENT_GUIDE.md` is the existing repo guide for collector behavior.
- `../MD-DOCS/AGENTS.md` routes operational event workflows.

## Protected Actions

Human approval is required before production deployment, production DB writes, credential changes, destructive SQL, branch deletion, force push, or customer-facing claims that were not verified.
