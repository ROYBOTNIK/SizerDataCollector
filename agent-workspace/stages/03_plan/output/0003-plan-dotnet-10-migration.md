# .NET 10 Migration Plan

Backlog item: `0003-plan-dotnet-10-migration`
Date: 2026-06-28

## Decision

Use staged migration, not a direct rewrite.

- A, do nothing: loses because the production code is still legacy MSBuild with `packages.config`, duplicated package references, and .NET Framework-only service installer code.
- B, minimal safe path: wins. First convert production projects to SDK-style `net48` and `PackageReference`, then port runtime to `net10.0-windows`.
- AB, broad direct port: loses for now because WCF, Windows service hosting, deployment scripts, and installer behavior need separate validation gates.

Microsoft lists .NET 10 as LTS supported until November 2028, while .NET Framework follows its Windows component lifecycle. That means this is important modernization work, not an emergency runtime jump. Sources:

- https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
- https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework

## Current Inventory

- `SizerDataCollector.csproj`, `SizerDataCollector.Core/SizerDataCollector.Core.csproj`, and `SizerDataCollector.Service/SizerDataCollector.Service.csproj` are legacy .NET Framework 4.8 projects.
- `SizerDataCollector.Tests/SizerDataCollector.Tests.csproj` is already SDK-style `net48`.
- Production projects still use `packages.config`; each production package entry currently targets `net48`.
- `SizerDataCollector.Core` includes generated WCF proxy code from `Connected Services/SizerServiceReference/Reference.cs`.
- `Sizer/SizerClient.cs`, `Sizer/SizerClientTester.cs`, and `SizerDataCollector.Core/AnomalyDetection/SizerAlarmSink.cs` create `WSHttpBinding(SecurityMode.None)` clients against `http://<host>:<port>/SizerService/`.
- `SizerDataCollector.Service` uses `ServiceBase`, `ServiceController`, `ProjectInstaller`, and `ManagedInstallerClass.InstallHelper`.
- Deployment scripts expect classic build output under `bin\<Configuration>` and `SizerDataCollector.Service\bin\<Configuration>`.
- `OptiFresh.OeeSuite.sln` references an external `.vdproj` installer project beside this repo.

## Migration Phases

1. Baseline gate
   - Keep current branch on .NET Framework 4.8.
   - Run `dotnet test SizerDataCollector.sln --no-restore`.
   - Run `scripts/audit-packages-config-vulnerabilities.ps1`.
   - Capture current service bundle layout from `scripts/build-remote-bundle.ps1`.

2. SDK-style net48
   - Convert the three production `.csproj` files to SDK-style while still targeting `net48`.
   - Move `packages.config` dependencies into `PackageReference`.
   - Preserve SQL definition copy behavior in `SizerDataCollector.Service`.
   - Keep explicit linked/generated WCF files only where SDK globbing will not include them naturally.
   - Add `global.json` only when the team chooses a pinned SDK version.

3. Dependency cleanup
   - Remove compatibility packages that are inbox on `net10.0-windows` unless source still requires a package API.
   - Keep `Newtonsoft.Json` only where the code still needs Json.NET behavior.
   - Upgrade Npgsql only after a build/test pass and a non-production DB smoke test.
   - Consider `Directory.Packages.props` after PackageReference is stable; do not introduce it in the same diff as the project conversion.

4. WCF client port
   - Regenerate or port the WCF client with `dotnet-svcutil` or Visual Studio WCF Web Service Reference.
   - Add the `System.ServiceModel.*` client packages required by the generated `WSHttpBinding` proxy.
   - Generate only from the checked-in WSDL files or a trusted customer Sizer endpoint.
   - Validate read-only calls first: `GetSerialNo`, `GetMachineName`, and existing `test-connections`.

5. Service host port
   - Target `net10.0-windows` for Windows-only service APIs.
   - Replace `ProjectInstaller` and `ManagedInstallerClass.InstallHelper` with script-driven `sc.exe create/config/delete`, matching the existing deployment scripts.
   - Consider `Microsoft.Extensions.Hosting.WindowsServices` only when replacing `ServiceBase` with a worker-style host; do not mix both hosting models in one large diff.
   - Keep CLI mode as a console exe so operational subcommands still print to the parent console.

6. Deployment port
   - Switch scripts from build-output copy to `dotnet publish`.
   - Choose one runtime model before customer deployment:
     - Framework-dependent: customer machines need the supported .NET 10 runtime installed and patched.
     - Self-contained: larger bundle, no runtime install, but each Microsoft servicing update requires rebuilding and redeploying the app.
   - Preserve the current install root, service name, backup folder, rollback flow, and SQL definition layout unless a human approves changing them.

## Validation Gates

- Build: `dotnet build SizerDataCollector.sln -c Release`.
- Tests: `dotnet test SizerDataCollector.sln --no-restore`.
- Vulnerabilities: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\audit-packages-config-vulnerabilities.ps1` until PackageReference migration enables `dotnet list package --vulnerable`.
- CLI: run `SizerDataCollector.Service.exe --help`, `show-config`, and service command usage from published output.
- WCF read-only: run `test-connections` only against a non-production or approved customer Sizer endpoint.
- DB: run schema commands only against disposable or explicitly approved staging TimescaleDB.
- Service: install, start, stop, restart, uninstall on a disposable Windows VM before any customer machine.
- Deployment: build remote bundle, install from bundle, rollback from backup, and confirm SQL definitions are copied.

## Protected Decisions

Stop for human approval before:

- touching production services or customer machines;
- running production DB writes or destructive SQL;
- using real credentials;
- generating WCF code from an unverified endpoint;
- sending Sizer alarms visible to operators;
- making customer-facing claims about compatibility, support, or runtime requirements before validation.

## Next Backlog Candidates

- Convert production projects to SDK-style `net48` with `PackageReference`.
- Fix deployment documentation and scripts that reference stale commands, including any CLI commands not present in `Program.cs`.
- Add a non-production service install smoke test checklist for Windows.
