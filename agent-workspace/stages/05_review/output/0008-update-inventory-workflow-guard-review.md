# 0008 Review

Decision: approve.

Checks:

- `rg "_config/repo-inventory.md|repo-inventory.md" agent-workspace/AGENTS.md agent-workspace/orchestrator.md agent-workspace/stages/04_execute/CONTEXT.md agent-workspace/stages/05_review/CONTEXT.md agent-workspace/skills/sizer-production-workspace/SKILL.md` confirmed the workflow guard is present.
- `git diff --check` passed; only CRLF normalization warnings.
- No production service, database, credential, source code, project file, or deployment script was touched.

A, do nothing, loses because future agents could edit code without refreshing inventory. B, add the guard to canonical workflow docs, passes. AB, add automation, loses because a written workflow rule is enough until agents miss it in practice.

Residual risk: this is a procedural guard, not an enforced CI check.
