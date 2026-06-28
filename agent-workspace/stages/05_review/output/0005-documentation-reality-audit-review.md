# 0005 Review

Decision: approve scoped documentation reality pass.

Checks:

- `SizerDataCollector.sln` includes `SizerDataCollector.Tests`; `OptiFresh.OeeSuite.sln` includes the external installer project.
- `SizerDataCollector.Service/Commands/DbCommands.cs` exposes `db init`, `apply-functions`, `apply-caggs`, `apply-views`, and `apply-all`; no `db apply-schema` command exists.
- `rg` found no live WPF/GUI docs outside completed backlog history.
- `git diff --check` passed; only CRLF normalization warnings.
- `dotnet test SizerDataCollector.sln --no-restore` passed, 30 tests.

A, do nothing, loses because entry docs had stale build/test and DB command guidance. B, scoped docs fixes, passes and keeps the diff reviewable. AB, rewrite all historical workflow docs, loses for this tick because `MD-DOCS/DESIGN.md` is already marked historical and broader deployment script behavior needs its own item.

Residual risk: `scripts/install-production.ps1` still calls unsupported `preflight`; captured as backlog item `0006` rather than changing deployment behavior in a docs audit.
