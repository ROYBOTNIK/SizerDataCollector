# 04 Execute

## Inputs

- Layer 4: selected backlog item
- Layer 4: `../03_plan/output/*.md` when present
- Layer 3: relevant source files and tests

## Process

Implement one scoped item. Search callers before changing shared functions. Prefer deletion, existing helpers, stdlib, and installed dependencies before new code.

If the change affects code, project/package files, scripts, command surfaces, deployment behavior, or repo shape, update `../../_config/repo-inventory.md` in the same tick.

## Outputs

- Repo diff
- Updated backlog item status
- Updated `../../_config/repo-inventory.md`, or a review note saying why it was not applicable
