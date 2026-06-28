# 02 Stocktake

## Inputs

- Layer 3: `../../_config/repo-inventory.md`
- Layer 3: `../../../MD-DOCS/AI_AGENT_GUIDE.md`
- Layer 4: repository files relevant to the requested audit

## Process

Refresh only the inventory sections affected by new facts. Prefer commands over manual counting.

Minimum checks:

- `git status --short --branch`
- project/package inventory
- script inventory
- build/test gate discovery
- obvious secrets, TODOs, sync-over-async, destructive SQL patterns

## Outputs

- Updated `../../_config/repo-inventory.md`
- New backlog items for real risks
