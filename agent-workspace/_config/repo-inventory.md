# Repo Inventory - 2026-06-28

Branch: `codex/production-workspace` tracking `origin/codex/production-workspace`.
Remote default: `origin/master`.
Current branch contains production workspace commits ahead of `origin/master`; refresh with `git status --short --branch` before merge.

## Application Shape

- .NET Framework 4.8 solution.
- Production projects are SDK-style `net48` projects using `PackageReference`; the test project was already SDK-style.
- `SizerDataCollector.Service`: production Windows service and CLI.
- `SizerDataCollector.Core`: collector, config, DB, schema, WCF client, anomaly/OEE logic.
- `SizerDataCollector`: legacy console probe.
- `SizerDataCollector.Tests`: MSTest test project.
- `Connected Services/SizerServiceReference/Reference.cs`: generated WCF proxy. Do not hand-edit unless regenerating the service reference.

## Line Inventory

Generated from tracked and workspace-visible source/docs excluding `bin`, `obj`, `packages`, `.git`, `.vs`, and `dist`.

| Type | Files | Lines |
| --- | ---: | ---: |
| C# | 79 | 22392 |
| SQL | 8 | 5881 |
| Markdown | 62 | 5752 |
| WSDL | 2 | 3461 |
| XSD | 8 | 2040 |
| C# projects | 4 | 126 |
| Python | 1 | 470 |
| PowerShell | 6 | 542 |
| config | 3 | 190 |
| solutions | 2 | 172 |

C# split: 17757 handwritten lines plus 4635 generated WCF proxy lines.

## Packages

Production packages are declared with `PackageReference` in the project files. The old production `packages.config` files were removed in backlog item `0007`.

Production packages:

- `Microsoft.Bcl.AsyncInterfaces` 10.0.7
- `Newtonsoft.Json` 13.0.4
- `Npgsql` 6.0.13
- `System.*` compatibility packages for net48: Buffers, Collections.Immutable, DiagnosticSource, IO.Pipelines, Memory, Numerics.Vectors, Runtime.CompilerServices.Unsafe, Text.Encodings.Web, Text.Json, Threading.Channels, Threading.Tasks.Extensions, ValueTuple where required by production projects

Test packages:

- `Microsoft.NET.Test.Sdk` 18.4.0
- `MSTest.TestAdapter` 4.2.1
- `MSTest.TestFramework` 4.2.1

## Scripts

| Script | Lines | Purpose |
| --- | ---: | --- |
| `scripts/audit-packages-config-vulnerabilities.ps1` | 118 | Legacy `packages.config` vulnerability helper; exits cleanly when production projects use `PackageReference` and points agents to `dotnet list package --vulnerable`. |
| `scripts/build-remote-bundle.ps1` | 87 | Build deployment bundle. |
| `scripts/install-from-bundle.ps1` | 75 | Install from bundle. |
| `scripts/install-production.ps1` | 148 | Production install helper; preflight now calls `SizerDataCollector.Service.exe show-config`. |
| `scripts/REMOTE_BUNDLE_README.md` | 83 | Remote bundle operator notes. |
| `scripts/rollback-production.ps1` | 53 | Roll back production install. |
| `scripts/uninstall-production.ps1` | 41 | Uninstall production service. |
| `scripts/verify-adaptive-thresholds.py` | 470 | Verify adaptive thresholds. |

## Immediate Risks

- Resolved 2026-06-28: sample connection strings in `App.config` and `README.md` now use placeholders instead of a real-looking password.
- P1: CLI command code uses synchronous `.GetAwaiter().GetResult()` wrappers. This is acceptable for a console boundary but should be reviewed per command for cancellation, timeout, and user-facing error behavior.
- Resolved 2026-06-28: `scripts/audit-packages-config-vulnerabilities.ps1` audits production `packages.config` files against NuGet's official vulnerability feed. Current run found no vulnerable listed packages.
- Resolved 2026-06-28: `scripts/install-production.ps1` no longer calls the unsupported legacy `preflight` command; preflight now uses the deployed service executable's `show-config`.
- Resolved 2026-06-28: production projects are SDK-style `net48` projects with `PackageReference`; backlog item `0007` completed without retargeting to .NET 10.
- P1: `master` is behind the current production workspace branch. Merge still requires a real conflict check.
- P2: `MD-DOCS/DESIGN.md` still describes an older CLI direction and should be reconciled with the current service-first CLI.
- P2: `docs/delivery/` is ignored by git and not part of the production workspace until deliberately promoted.

## Validation

- 2026-06-28 stocktake: `git status --short --branch`, project/package inventory, script inventory, and risk pattern search refreshed.
- `dotnet test SizerDataCollector.sln --no-restore`: last passed, 30 tests.
- 2026-06-28 `0007`: `dotnet restore SizerDataCollector.sln`; `dotnet build SizerDataCollector.sln -c Release --no-restore` passed with 0 warnings; `dotnet test SizerDataCollector.sln --no-restore` passed, 30 tests; `dotnet list SizerDataCollector.sln package --vulnerable --include-transitive` found no vulnerable packages; service SQL definitions copied to `SizerDataCollector.Service/bin/Release/sql/definitions`; WCF proxy plus checked-in WSDL/XSD metadata are linked from `SizerDataCollector.Core`.
- Skill validation: passed with system Python. Bundled Python was missing `yaml`.
