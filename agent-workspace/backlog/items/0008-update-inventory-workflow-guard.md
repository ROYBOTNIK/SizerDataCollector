# Update Inventory Workflow Guard

Status: done
Priority: P1
Source: inbox note `update-inventory.md`
Goal: ensure workspace workflows tell agents to update `agent-workspace/_config/repo-inventory.md` when editing the codebase.

## User Note

> MAke sure the workflows have instruction to update @repo-inventory.md when they are editing the codebase

## Scope

- `agent-workspace/AGENTS.md`
- `agent-workspace/orchestrator.md`
- `agent-workspace/stages/04_execute/CONTEXT.md`
- `agent-workspace/stages/05_review/CONTEXT.md`
- `agent-workspace/skills/sizer-production-workspace/SKILL.md`

## Acceptance Checks

- Main workspace entrypoint mentions the inventory update rule.
- Execute/review workflows require inventory refresh or an explicit not-applicable decision.
- Repo-local skill repeats the rule for agents that enter through the skill.
- No production service, DB, credential, or code change is touched.

Protected action: no
Decision: minimal change; update workflow docs only.
Workset: none

## Work Done

- Added inventory-refresh instructions to the workspace entrypoint, orchestrator, execute/review stage contracts, and repo-local skill.
- Removed the processed inbox note.
