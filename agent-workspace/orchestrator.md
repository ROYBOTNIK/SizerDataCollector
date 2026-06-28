# Orchestrator Tick

Run this manually or from Codex automation.

1. Read `AGENTS.md`, `CONTEXT.md`, `_config/backlog-rubric.md`, and the current stage contract.
2. Move each actionable markdown note from `inbox/` into a backlog item.
3. Use autoreasoning to choose the workset. Compare `A = do nothing`, `B = one best item`, and `AB = 2-3 related items`. If none are ready, refresh stocktake and stop.
4. Load only the files needed for the chosen workset.
5. For each item, compare keep current state, minimal fix, and broader fix. Choose the smallest candidate that passes acceptance checks.
6. Implement the workset sequentially, refresh `_config/repo-inventory.md` when the codebase shape changes, run the smallest relevant checks after each item, and write review output.
7. Stop the workset on the first failed check, protected action, unclear acceptance check, or context growth that would require broad reload.
8. Auto-approve only if `_config/backlog-rubric.md` passes. Otherwise leave the item blocked with the missing decision.
9. Update `state/next-agent-context.md` in 300 tokens or less after each item and before ending so the next agent can resume without loading broad history.
10. Commit/push/merge only in `06_release` after checks pass and no protected action is involved.

ponytail: no custom orchestrator code; the filesystem, git, and Codex automation are enough until the loop needs metrics or queues.
