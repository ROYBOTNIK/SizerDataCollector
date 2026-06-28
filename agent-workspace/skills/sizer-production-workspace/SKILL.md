---
name: sizer-production-workspace
description: Orchestrate SizerDataCollector production work from inbox notes through backlog shaping, implementation, review, and release readiness. Use when an agent is asked to process agent-workspace inbox notes, run the production workspace loop, approve or reject a proposed change without waiting for a human, triage SizerDataCollector backlog items, or prepare TOMRA/COMPAC collector work for production.
---

# Sizer Production Workspace

## Overview

Use this skill to operate the repo-local Model Workspace Protocol under `agent-workspace/`. Keep context scoped to the current stage, leave inspectable markdown artifacts, and prefer the smallest change that advances production readiness.

## Workflow

1. Read `agent-workspace/AGENTS.md`, then `agent-workspace/CONTEXT.md`.
2. Pick the stage from `agent-workspace/stages/*/CONTEXT.md`.
3. Load only the stage inputs listed there plus the backlog item being worked.
4. For every decision, include a `do nothing` option and choose it when the change does not improve the goal.
5. Stand in for human approval only when the approval rubric passes and protected actions are not involved.
6. Write or update the smallest required artifact, then run the stage's validation command.

## Approval Rubric

Auto-approve only when all are true:

- The change maps to one backlog item and has clear acceptance checks.
- No production service, production database, customer data, secrets, or destructive SQL are touched.
- Build/tests or an explicit doc-only check passed.
- The diff is reversible and scoped to the item.
- Security posture is not weakened.

Require human approval for deployment, credential changes, destructive data changes, customer-facing claims, or broad rewrites.

## Resource Sizing

- One inbox note, doc change, or single-file fix: one agent, no subagents.
- Cross-cutting C#/SQL behavior: inspect callers first, run tests, use a review pass before release.
- Unclear or high-risk work: create a plan artifact in `agent-workspace/backlog/items/`, then stop before protected actions.
- Use Codex goals only when the user explicitly asks for a goal or the active environment requires it.

## References

- `agent-workspace/_config/method.md`: ICM, autoresearch, and autoreason adaptation.
- `agent-workspace/_config/repo-inventory.md`: current stocktake and risk list.
- `agent-workspace/_config/backlog-rubric.md`: backlog item format and scoring.
