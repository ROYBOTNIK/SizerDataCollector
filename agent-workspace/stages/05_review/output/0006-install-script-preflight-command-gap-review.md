# 0006 Review

Decision: approve.

Checks:

- PowerShell parser check passed for `scripts/install-production.ps1`.
- `rg "preflight --format=text|& \$CliExe preflight|case \"preflight\""` found no remaining unsupported preflight command path.
- No service, production machine, database, credential, or installer execution was touched.

A, do nothing, loses because the install script would call an unsupported command after deployment. B, point preflight at existing `show-config`, passes because it verifies the deployed service executable starts and can load runtime settings without Sizer/API or DB writes. AB, implement a new service CLI `preflight`, loses for this tick because it would add behavior without a reviewed preflight contract.

Residual risk: `show-config` is a light preflight. It does not prove Sizer/API or Timescale connectivity; those remain explicit operator checks via `test-connections` or DB commands with human-approved targets.
