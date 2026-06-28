# Orchestrator Tick

Run this manually or from Codex automation.

1. Read `AGENTS.md`, `CONTEXT.md`, `_config/backlog-rubric.md`, and the current stage contract.
2. Move each actionable markdown note from `inbox/` into a backlog item.
3. Pick one ready backlog item. If none are ready, refresh stocktake and stop.
4. Load only the files needed for that item.
5. Consider three candidates: keep current state, minimal fix, broader fix. Choose the smallest candidate that passes acceptance checks.
6. Implement one item, run the smallest relevant checks, and write review output.
7. Auto-approve only if `_config/backlog-rubric.md` passes. Otherwise leave the item blocked with the missing decision.
8. Update `state/next-agent-context.md` in 300 tokens or less so the next agent can resume without loading broad history.
9. Commit/push/merge only in `06_release` after checks pass and no protected action is involved.

ponytail: no custom orchestrator code; the filesystem, git, and Codex automation are enough until the loop needs metrics or queues.
