# 0001 Production Stocktake Hardening Review

Decision: approved

## Checks

- `rg` for the old real-looking DB password: no live config/docs examples remain after inventory wording cleanup.
- `rg` for removed WPF project references in solution/source/docs: no matches.
- `dotnet list SizerDataCollector.sln package --vulnerable --include-transitive`: failed because production projects use `packages.config`; captured as backlog item `0004`.
- `dotnet test SizerDataCollector.sln --no-restore`: passed, 30 tests.

## A/B/AB

- A, do nothing: loses because the old sample credential looked too real for production polish.
- B, minimal change: wins; placeholders plus a historical design-doc note solve the item.
- AB, broader rewrite: loses; package scanner support and .NET 10 migration are separate backlog work.

## Residual Risk

Package vulnerability scanning still needs a `packages.config`-compatible path before release claims should mention dependency security.
