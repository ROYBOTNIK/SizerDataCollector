# Plan .NET 10 Migration

Status: ready
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
