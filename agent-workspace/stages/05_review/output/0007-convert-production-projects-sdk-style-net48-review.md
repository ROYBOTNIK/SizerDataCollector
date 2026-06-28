# Review: 0007 Convert Production Projects To SDK-Style net48

Status: passed
Reviewer: Codex workspace loop
Date: 2026-06-28

## What Changed

- Converted `SizerDataCollector.csproj`, `SizerDataCollector.Core/SizerDataCollector.Core.csproj`, and `SizerDataCollector.Service/SizerDataCollector.Service.csproj` to SDK-style `net48`.
- Removed the three production `packages.config` files and moved package dependencies into `PackageReference`.
- Preserved deployment-sensitive output paths with `AppendTargetFrameworkToOutputPath=false`.
- Kept service SQL definitions copying to the Release output.
- Kept the generated WCF proxy linked and retained connected-service WSDL/XSD metadata as project items.
- Updated `scripts/audit-packages-config-vulnerabilities.ps1` so it no longer fails after the PackageReference migration.

## Checks

- `dotnet restore SizerDataCollector.sln`: passed.
- `dotnet build SizerDataCollector.sln -c Release --no-restore`: passed with 0 warnings.
- `dotnet test SizerDataCollector.sln --no-restore`: passed, 30 tests.
- `dotnet list SizerDataCollector.sln package --vulnerable --include-transitive`: no vulnerable packages reported.
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/audit-packages-config-vulnerabilities.ps1`: passed compatibility path for no `packages.config` files.
- Confirmed the four service SQL definition files exist under `SizerDataCollector.Service/bin/Release/sql/definitions`.
- Confirmed `SizerDataCollector.Core` links `Connected Services/SizerServiceReference/Reference.cs` and includes the remaining connected-service metadata.

## Residual Risk

- No production service, production database, credential, or generated WCF endpoint action was performed.
- This item deliberately did not retarget to .NET 10; that remains a future migration step.
