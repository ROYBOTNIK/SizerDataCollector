# Plan .NET 10 Migration

Status: done
Priority: P1
Source: inbox note `Remove-WPF-GUI.md`
Goal: produce a migration plan for moving the collector from .NET Framework 4.8 to .NET 10 without breaking Windows service, WCF/Compac integration, installer, or deployment scripts.

## Scope

- Inventory `TargetFrameworkVersion`, `packages.config`, WCF generated proxy, Windows service hosting, installer expectations, and deployment scripts.
- Decide whether to migrate directly to SDK-style .NET 10 or stage through SDK-style net48 first.
- Identify replacement strategy for .NET Framework-only APIs and service installer behavior.
- Do not implement the migration until the plan is reviewed.

## Acceptance Checks

- Plan names blocking APIs and package moves.
- Plan includes build/test/deploy validation gates.
- Plan identifies any customer machine runtime requirements.
- No production DB or service is touched.

Protected action: no
Decision: minimal change means planning first, not a broad runtime rewrite in an automation tick.

## Work Done

- Added `agent-workspace/stages/03_plan/output/0003-plan-dotnet-10-migration.md`.
- Chose staged SDK-style `net48` first, then `net10.0-windows`.
- Named WCF, Windows service installer, deployment script, package, runtime, and customer-machine validation gates.
