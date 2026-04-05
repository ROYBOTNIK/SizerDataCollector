# SizerDataCollector OEE setup flow

This document summarizes the **recommended order** for bringing the collector online and configuring OEE-related settings. Commands target **`SizerDataCollector.Service.exe`** (project **SizerDataCollector.Service**); the Windows service registers as **`SizerDataCollectorService`**.

Configuration is written to **`%ProgramData%\Opti-Fresh\SizerDataCollector\collector_config.json`** via the CLI (see `README.md` and `AI_AGENT_GUIDE.md` for full command reference).

---

## Setup diagram

```mermaid
flowchart TD
    subgraph prereq [Prerequisites]
        A[SizerDataCollector.Service.exe built or installed]
    end

    subgraph conn [Connections — before reliable ingestion]
        B[set-sizer: host port timeouts]
        C[set-db: Timescale connection string]
        D[Optional: set-shared-dir or configure interactive]
        E[test-connections]
    end

    subgraph db [Database definitions]
        F[db init — schema functions CAGGs views]
    end

    subgraph svc [Windows service — Administrator for install]
        H[service install]
        I[service start]
        J[set-ingestion true when ready]
    end

    G[Optional: db status]

    subgraph machine [Per-machine setup — needs serial from Sizer]
        K[machine register — serial and name]
        L[set-thresholds — min-rpm min-total-fpm]
        M[set-settings — target-speed lane-count target-pct recycle-outlet]
    end

    subgraph grade [Grade to category]
        N[grade-map — list overrides]
        O[grade-map —set —grade —category 0-3]
    end

    subgraph oee [OEE targets and bands]
        P[set-quality-params — quality targets and weights]
        Q[set-perf-params — performance curve inputs]
        R[set-band / show-bands — OEE bands]
    end

    subgraph verify [Verification]
        S[machine commission]
    end

    subgraph optional [Optional — alarms and detection]
        T[set-anomaly / set-size-anomaly / test-alarm]
    end

    A --> B
    A --> C
    B --> D
    C --> D
    D --> E
    E --> F
    F --> H
    F -.-> G
    H --> I
    I --> J
    J --> K
    K --> L
    L --> M
    M --> N
    N --> O
    O --> P
    P --> Q
    Q --> R
    R --> S
    S --> T
```

---

## Notes

- **Service install/start** assumes **`db init`** has been run so Timescale has the expected schema; the service can start with API/DB temporarily down, but OEE data paths need the DB objects in place.
- **`machine register`** requires a **serial number** that matches the Sizer (often obtained after API connectivity; commissioning helps confirm discovery).
- **Grade categories** `0–3` map Sizer grades into OEE quality groupings; use **`machine grade-map`** to list and override.
- **Quality / performance / bands** are per serial: **`machine show-quality-params`**, **`show-perf-params`**, **`show-bands`** to inspect before changing.
- For **elevated** steps (`service install`, `uninstall`, `start`, `stop`, `restart`), run the CLI from an **Administrator** shell (or equivalent).

For exact flags and examples, see **`README.md`** (Quickstart and CLI summary) and **`AI_AGENT_GUIDE.md`** (CLI command reference for automation).
