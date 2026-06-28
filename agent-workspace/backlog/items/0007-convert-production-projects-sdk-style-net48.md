# Convert Production Projects To SDK-Style net48

Status: done
Priority: P1
Source: .NET 10 migration plan `0003`
Goal: convert the three production projects to SDK-style `net48` with `PackageReference` as the first implementation step toward .NET 10.

## Scope

- `SizerDataCollector.csproj`
- `SizerDataCollector.Core/SizerDataCollector.Core.csproj`
- `SizerDataCollector.Service/SizerDataCollector.Service.csproj`
- `packages.config`
- `SizerDataCollector.Core/packages.config`
- `SizerDataCollector.Service/packages.config`
- Build output/deployment assumptions that depend on classic project layout

## Acceptance Checks

- `dotnet restore SizerDataCollector.sln`
- `dotnet build SizerDataCollector.sln -c Release`
- `dotnet test SizerDataCollector.sln --no-restore`
- Confirm service SQL definition files still copy to `SizerDataCollector.Service/bin/<Configuration>/sql/definitions`.
- Confirm generated WCF proxy and checked-in WSDL/XSD files are still included or intentionally linked.

Protected action: no
Decision: minimal change means SDK-style `net48` and `PackageReference` only; do not retarget to .NET 10 in the same item.
Workset: none

## Completion Notes

- Converted the three production projects to SDK-style `net48` without retargeting.
- Replaced production `packages.config` files with project-level `PackageReference` entries.
- Preserved legacy Release output paths for deployment scripts and explicitly kept service SQL definition copy behavior.
- Linked the generated WCF proxy and checked-in connected-service metadata through the Core project.
- Updated the legacy packages.config vulnerability helper to exit cleanly once production projects use `PackageReference`.
