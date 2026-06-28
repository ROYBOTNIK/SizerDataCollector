# SizerDataCollector Production Workspace

This is the Layer 0 entry point for agents working toward a polished production SizerDataCollector for TOMRA / COMPAC customer work.

Rules:

- Read this file, then `CONTEXT.md`, then the current stage `CONTEXT.md`.
- Keep context scoped. Load only the files named by the stage contract and the active backlog item.
- Treat folders as the orchestrator: inbox notes become backlog items, backlog items become scoped changes, review gates decide release readiness.
- Prefer existing repo patterns, .NET Framework 4.8 compatibility, and existing CLI/SQL workflows.
- Do not touch production services, production databases, credentials, or destructive SQL without explicit human approval.
- Every non-trivial code change needs the smallest runnable check that would catch a regression.
- When code, project files, packages, scripts, command surfaces, deployment behavior, or repo shape changes, update `_config/repo-inventory.md` in the same tick or record why it was not applicable.
- End every tick by updating `state/next-agent-context.md` with the current item, decisions, touched files, checks, and next action.

Default loop:

1. Process `inbox/`.
2. Select the highest-ready backlog item.
3. Execute the smallest useful change.
4. Review against `_config/backlog-rubric.md`.
5. Update `state/next-agent-context.md`.
6. Stage release notes only when tests and approval gates pass.
