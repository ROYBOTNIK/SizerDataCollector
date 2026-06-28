# Repo Inventory - 2026-06-28

Branch: `codex/adaptive_throughput_work` tracking `origin/codex/adaptive_throughput_work`.
Remote default: `origin/master`.
Current branch was 20 commits ahead of `origin/master` and missing 1 `origin/master` commit at stocktake time.

## Application Shape

- .NET Framework 4.8 solution.
- `SizerDataCollector.Service`: production Windows service and CLI.
- `SizerDataCollector.Core`: collector, config, DB, schema, WCF client, anomaly/OEE logic.
- `SizerDataCollector`: legacy console probe.
- `SizerDataCollector.Tests`: MSTest test project.
- `Connected Services/SizerServiceReference/Reference.cs`: generated WCF proxy. Do not hand-edit unless regenerating the service reference.

## Line Inventory

Generated from tracked and workspace-visible source/docs excluding `bin`, `obj`, `packages`, `.git`, `.vs`, and `dist`.

| Type | Files | Lines |
| --- | ---: | ---: |
| C# | 79 | 20297 |
| SQL | 8 | 5191 |
| Markdown | 34 | 3728 |
| WSDL | 2 | 3461 |
| XSD | 8 | 2040 |
| C# projects | 4 | 511 |
| Python | 1 | 398 |
| PowerShell | 5 | 336 |
| config | 6 | 242 |
| solutions | 2 | 170 |

C# split: 15663 handwritten lines plus 4634 generated WCF proxy lines.

## Packages

Production packages:

- `Microsoft.Bcl.AsyncInterfaces` 10.0.7
- `Newtonsoft.Json` 13.0.4
- `Npgsql` 6.0.13
- `System.*` compatibility packages for net48: Buffers, Collections.Immutable, DiagnosticSource, IO.Pipelines, Memory, Numerics.Vectors, Runtime.CompilerServices.Unsafe, Text.Encodings.Web, Text.Json, Threading.Channels, Threading.Tasks.Extensions, ValueTuple

Test packages:

- `Microsoft.NET.Test.Sdk` 18.4.0
- `MSTest.TestAdapter` 4.2.1
- `MSTest.TestFramework` 4.2.1

## Scripts

| Script | Lines | Purpose |
| --- | ---: | --- |
| `scripts/build-remote-bundle.ps1` | 75 | Build deployment bundle. |
| `scripts/install-from-bundle.ps1` | 62 | Install from bundle. |
| `scripts/install-production.ps1` | 123 | Production install helper. |
| `scripts/rollback-production.ps1` | 42 | Roll back production install. |
| `scripts/uninstall-production.ps1` | 34 | Uninstall production service. |
| `scripts/verify-adaptive-thresholds.py` | 398 | Verify adaptive thresholds. |

## Immediate Risks

- P0: `Password=root` sample connection string appears in `App.config` and README examples. Replace with placeholder-only examples before calling this production polished.
- P1: CLI command code uses synchronous `.GetAwaiter().GetResult()` wrappers. This is acceptable for a console boundary but should be reviewed per command for cancellation, timeout, and user-facing error behavior.
- P1: `Npgsql` and compatibility package currency needs a vulnerability and compatibility audit for net48. `dotnet list package --vulnerable --include-transitive` stops on the production `packages.config` projects, so the scanner path itself needs fixing or a NuGet CLI fallback.
- P1: `master` is behind the current feature branch and has one commit not in this branch. Merge requires a real conflict check.
- P2: `MD-DOCS/DESIGN.md` still describes an older CLI direction and should be reconciled with the current service-first CLI.
- P2: `docs/delivery/` is ignored by git and not part of the production workspace until deliberately promoted.

## Validation

- `dotnet test SizerDataCollector.sln --no-restore`: passed, 30 tests.
- Skill validation: passed with system Python. Bundled Python was missing `yaml`.
