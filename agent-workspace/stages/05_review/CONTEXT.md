# 05 Review

## Inputs

- Layer 4: repo diff
- Layer 4: selected backlog item
- Layer 3: `../../_config/backlog-rubric.md`

## Process

Run the smallest relevant checks. Review for correctness, data loss, security, lifecycle, docs drift, missing tests, and whether `../../_config/repo-inventory.md` was updated when the codebase shape changed. Compare the final diff against doing nothing.

## Outputs

- `output/<item-id>-review.md` with checks, findings, decision, and residual risk.
