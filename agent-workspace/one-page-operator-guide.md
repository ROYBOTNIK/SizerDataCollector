# One Page Agent Operator Guide

To request work, add a markdown file to `agent-workspace/inbox/`. Use any name, for example `fix-product-setup-refresh.md`. Write the problem, why it matters, any customer/site context, and what would count as done.

Every automation tick reads the inbox, turns notes into backlog items, picks one ready item, makes the smallest safe change, runs checks, and records review output. If the item touches production services, production data, credentials, destructive SQL, or unverified customer claims, the agent must stop and ask for approval.

Useful folders:

- `inbox/`: rough notes from you.
- `backlog/items/`: shaped work items with acceptance checks.
- `stages/*/CONTEXT.md`: stage instructions for agents.
- `_config/repo-inventory.md`: current app stocktake and risks.
- `orchestrator.md`: manual tick instruction.

Manual run prompt:

```text
Use agent-workspace/orchestrator.md and process the next SizerDataCollector workspace item.
```

The loop is done when `inbox/` is empty and every backlog item is either done, blocked with a clear reason, or waiting for human approval.
