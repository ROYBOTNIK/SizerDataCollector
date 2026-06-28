# 06 Release

## Inputs

- Layer 4: reviewed repo diff
- Layer 4: `../05_review/output/*.md`
- Layer 3: `../../_config/backlog-rubric.md`

## Process

Only release when review approves and no protected action is pending. Stage intentional files, commit tersely, push, then merge or open a PR according to repo state.

After merging back to `master`, create the next `codex/production-workspace` branch or Codex worktree fork for continued work.

## Outputs

- Commit hash, pushed branch, merge/PR result, and next branch/worktree reference.
