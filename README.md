# SizerDataCollector (console)

## Purpose
SizerDataCollector is a .NET Framework 4.8 console app that validates connectivity with a Sizer WCF service and a TimescaleDB/Postgres backend. It performs:
- A safe, idempotent schema existence check (no destructive migrations)
- A read-only probe to the Sizer service (`GetSerialNo`, `GetMachineName`)

This project is part of the broader OptiFresh.OeeSuite solution and serves as the baseline collector prototype.

## Requirements
- .NET Framework 4.8
- Network access to the Sizer WCF service endpoint
- Optional: Access to a TimescaleDB/Postgres instance (for schema check)

## Configuration
Edit `App.config`:
- `appSettings`
  - `SizerHost`: hostname or IP of the Sizer service (default `10.155.155.10`)
  - `SizerPort`: port of the Sizer service (default `8001`)
  - `OpenTimeoutSec`, `SendTimeoutSec`, `ReceiveTimeoutSec`: WCF timeouts in seconds (default `5`)
  - `LogDirectory`: directory for log files (empty = `<exe>\logs`)
- `connectionStrings`
  - `TimescaleDb`: standard Postgres connection string. Example:
    `Host=127.0.0.1;Port=5432;Username=postgres;Password=root;Database=sizer_metrics_staging;`

## How to run the probe
1. Build the solution (`OptiFresh.OeeSuite.sln`) or the project (`SizerDataCollector.csproj`).
2. Run `SizerDataCollector.exe`.
3. The app will:
   - Attempt a TimescaleDB connection and ensure tables exist (`machines`, `batches`, `metrics`).
   - Open a WCF client and call `GetSerialNo` and `GetMachineName` on the Sizer service.
4. Check console output and daily log files in `LogDirectory` for results and errors.

## Project layout
- `Config\CollectorConfig.cs`
- `Logging\Logging.cs`
- `Db\DatabaseTester.cs`
- `Sizer\SizerClientTester.cs`
- `Connected Services\SizerServiceReference\...`
- `Program.cs`


