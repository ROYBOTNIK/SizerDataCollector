# 0003 Review

Decision: approve planning item.

Checks:

- Confirmed production projects still target .NET Framework 4.8 with legacy MSBuild and `packages.config`.
- Confirmed tests are already SDK-style `net48`.
- Confirmed WCF client code uses generated `System.ServiceModel` proxy and `WSHttpBinding`.
- Confirmed service install/control currently depends on `ServiceBase`, `ServiceController`, `ProjectInstaller`, and `ManagedInstallerClass`.
- Confirmed deployment scripts expect classic build output and existing service name/install root.
- `git diff --check` passed; only CRLF normalization warnings.
- `dotnet test SizerDataCollector.sln --no-restore` passed, 30 tests.

A, do nothing, loses because future migration work would lack gates. B, plan first, passes because no production service, DB, credential, or customer-facing action was touched. AB, direct migration, remains too broad for an automation tick.

Residual risk: the plan is source-level only. WCF package compatibility and customer runtime requirements still need validation in a disposable Windows environment before implementation.
