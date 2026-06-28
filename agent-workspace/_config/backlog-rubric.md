# Backlog And Approval Rubric

## Backlog Item Format

Each item in `backlog/items/` should include:

- `Status`: ready, doing, blocked, review, done.
- `Source`: inbox note, stocktake, test failure, production observation, or human request.
- `Goal`: one sentence.
- `Scope`: files or behavior likely touched.
- `Acceptance checks`: build, tests, docs, SQL dry-run, manual approval, or reason no check applies.
- `Protected action`: yes/no.
- `Decision`: do nothing, minimal change, broader change.
- `Workset`: none, or the related backlog item IDs selected for the same tick.

## Priority

| Priority | Meaning |
| --- | --- |
| P0 | Data loss, credentials, production outage, broken build, unsafe deployment path. |
| P1 | Customer-visible correctness, security hardening, service reliability, missing release gate. |
| P2 | Refactor, docs drift, cleanup, ergonomics. |

## Auto-Approval Gate

Auto-approve when:

- The item is P1 or lower, or P0 is fully test-only/documentation-only.
- Protected action is `no`.
- Acceptance checks passed.
- The diff is scoped to the item.
- `A = do nothing` was considered and lost for a concrete reason.
- If part of a workset, every item shares files/checks/risk and was completed sequentially.

Otherwise mark blocked and state the missing human decision.
