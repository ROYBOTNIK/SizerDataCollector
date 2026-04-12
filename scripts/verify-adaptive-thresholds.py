#!/usr/bin/env python3
"""
Reversible DB-backed verification for adaptive quality/performance thresholds.

This script:
1. Reads the currently configured DB from collector_config.json.
2. Captures baseline quality/performance outputs for one serial and time window.
3. Updates quality/perf params via the existing Service CLI.
4. Refreshes throughput CAGGs for the affected window.
5. Verifies that OEE-related values move.
6. Restores the original parameter state and refreshes again.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Dict, Optional, Tuple

import psycopg2


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_CONFIG_PATH = Path(
    r"C:\ProgramData\Opti-Fresh\SizerDataCollector\collector_config.json"
)
DEFAULT_SERVICE_EXE = (
    REPO_ROOT / "SizerDataCollector.Service" / "bin" / "Debug" / "SizerDataCollector.Service.exe"
)


@dataclass
class QualityParams:
    tgt_good: float
    tgt_peddler: float
    tgt_bad: float
    tgt_recycle: float
    w_good: float
    w_peddler: float
    w_bad: float
    w_recycle: float
    sig_k: float


@dataclass
class PerfParams:
    min_effective_fpm: float
    low_ratio_threshold: float
    cap_asymptote: float


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Verify adaptive threshold behavior.")
    parser.add_argument("--serial", required=True, help="Machine serial number to test.")
    parser.add_argument(
        "--config",
        default=str(DEFAULT_CONFIG_PATH),
        help="Path to collector_config.json. Defaults to the runtime config.",
    )
    parser.add_argument(
        "--service-exe",
        default=str(DEFAULT_SERVICE_EXE),
        help="Path to SizerDataCollector.Service.exe.",
    )
    parser.add_argument(
        "--hours",
        type=int,
        default=24,
        help="Trailing window in hours when --from/--to are not supplied.",
    )
    parser.add_argument("--from", dest="from_ts", help="UTC timestamp, e.g. 2026-04-01T00:00:00Z")
    parser.add_argument("--to", dest="to_ts", help="UTC timestamp, e.g. 2026-04-02T00:00:00Z")
    return parser.parse_args()


def load_connection_string(config_path: Path) -> str:
    data = json.loads(config_path.read_text(encoding="utf-8"))
    conn_str = data.get("TimescaleConnectionString", "").strip()
    if not conn_str:
        raise RuntimeError("TimescaleConnectionString is empty in collector_config.json.")
    return conn_str


def connect_from_npgsql(conn_str: str):
    mapping = {
        "host": "host",
        "port": "port",
        "username": "user",
        "user id": "user",
        "userid": "user",
        "password": "password",
        "database": "dbname",
        "dbname": "dbname",
    }
    kwargs = {}
    for part in conn_str.split(";"):
        part = part.strip()
        if not part or "=" not in part:
            continue
        key, value = part.split("=", 1)
        normalized = mapping.get(key.strip().lower())
        if normalized:
            kwargs[normalized] = value.strip()

    if "dbname" not in kwargs:
        raise RuntimeError("Could not parse database name from TimescaleConnectionString.")

    return psycopg2.connect(**kwargs)


def run_cli(service_exe: Path, *args: str) -> None:
    cmd = [str(service_exe)] + list(args)
    print("$", " ".join(cmd))
    completed = subprocess.run(
        cmd,
        cwd=str(service_exe.parent),
        text=True,
        capture_output=True,
        check=False,
    )
    if completed.stdout:
        print(completed.stdout.rstrip())
    if completed.stderr:
        print(completed.stderr.rstrip(), file=sys.stderr)
    if completed.returncode != 0:
        raise RuntimeError(f"CLI command failed ({completed.returncode}): {' '.join(cmd)}")


def query_one(cur, sql: str, params: Tuple) -> Tuple:
    cur.execute(sql, params)
    row = cur.fetchone()
    if row is None:
        raise RuntimeError("Expected one row but query returned none.")
    return row


def fetch_optional_quality(cur, serial: str) -> Optional[QualityParams]:
    cur.execute(
        """
        SELECT tgt_good, tgt_peddler, tgt_bad, tgt_recycle,
               w_good, w_peddler, w_bad, w_recycle, sig_k
        FROM oee.quality_params
        WHERE serial_no = %s
        """,
        (serial,),
    )
    row = cur.fetchone()
    if row is None:
        return None
    return QualityParams(*[float(v) for v in row])


def fetch_optional_perf(cur, serial: str) -> Optional[PerfParams]:
    cur.execute(
        """
        SELECT min_effective_fpm, low_ratio_threshold, cap_asymptote
        FROM oee.perf_params
        WHERE serial_no = %s
        """,
        (serial,),
    )
    row = cur.fetchone()
    if row is None:
        return None
    return PerfParams(*[float(v) for v in row])


def latest_quality_ts(cur, serial: str) -> Optional[datetime]:
    cur.execute(
        """
        SELECT max(minute_ts)
        FROM oee.v_quality_minute_batch
        WHERE serial_no = %s
        """,
        (serial,),
    )
    row = cur.fetchone()
    return row[0] if row else None


def resolve_window(cur, serial: str, hours: int, from_raw: Optional[str], to_raw: Optional[str]) -> Tuple[datetime, datetime]:
    if from_raw and to_raw:
        return parse_ts(from_raw), parse_ts(to_raw)

    latest = latest_quality_ts(cur, serial)
    if latest is None:
        raise RuntimeError(
            f"No quality data found in oee.v_quality_minute_batch for serial '{serial}'."
        )

    latest_utc = latest.astimezone(timezone.utc)
    return latest_utc - timedelta(hours=hours), latest_utc


def parse_ts(raw: str) -> datetime:
    value = raw.strip().replace("Z", "+00:00")
    parsed = datetime.fromisoformat(value)
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def metric_snapshot(cur, serial: str, from_ts: datetime, to_ts: datetime) -> Dict[str, Optional[float]]:
    from_day = from_ts.date()
    to_day = to_ts.date()

    cur.execute(
        """
        SELECT avg(quality_ratio)::double precision
        FROM oee.v_quality_minute_batch
        WHERE serial_no = %s
          AND minute_ts >= %s
          AND minute_ts < %s
        """,
        (serial, from_ts, to_ts),
    )
    quality_avg = cur.fetchone()[0]

    cur.execute(
        """
        SELECT avg(throughput_ratio)::double precision
        FROM public.daily_throughput_components
        WHERE serial_no = %s
          AND day >= %s
          AND day <= %s
        """,
        (serial, from_day, to_day),
    )
    throughput_daily_avg = cur.fetchone()[0]

    cur.execute(
        """
        SELECT avg(throughput_ratio)::double precision
        FROM oee.v_throughput_minute_batch
        WHERE serial_no = %s
          AND minute_ts >= %s
          AND minute_ts < %s
        """,
        (serial, from_ts, to_ts),
    )
    throughput_cagg_avg = cur.fetchone()[0]

    return {
        "quality_avg": float(quality_avg) if quality_avg is not None else None,
        "throughput_daily_avg": float(throughput_daily_avg) if throughput_daily_avg is not None else None,
        "throughput_cagg_avg": float(throughput_cagg_avg) if throughput_cagg_avg is not None else None,
    }


def refresh_throughput_caggs(cur, from_ts: datetime, to_ts: datetime) -> None:
    day_from = from_ts.replace(hour=0, minute=0, second=0, microsecond=0)
    day_to = (to_ts + timedelta(days=1)).replace(hour=0, minute=0, second=0, microsecond=0)

    cur.execute(
        "CALL refresh_continuous_aggregate('oee.cagg_throughput_minute_batch', %s, %s)",
        (from_ts, to_ts),
    )
    cur.execute(
        "CALL refresh_continuous_aggregate('oee.cagg_throughput_daily_batch', %s, %s)",
        (day_from, day_to),
    )


def delete_row(cur, table_name: str, serial: str) -> None:
    cur.execute(f"DELETE FROM {table_name} WHERE serial_no = %s", (serial,))


def approx_changed(before: Optional[float], after: Optional[float], epsilon: float = 1e-9) -> bool:
    if before is None or after is None:
        return False
    return not math.isclose(before, after, rel_tol=0, abs_tol=epsilon)


def print_snapshot(label: str, snapshot: Dict[str, Optional[float]]) -> None:
    print(label)
    for key, value in snapshot.items():
        if value is None:
            print(f"  {key}: <no data>")
        else:
            print(f"  {key}: {value:.8f}")


def main() -> int:
    args = parse_args()
    config_path = Path(args.config)
    service_exe = Path(args.service_exe)

    if not config_path.exists():
        raise RuntimeError(f"Config file not found: {config_path}")
    if not service_exe.exists():
        raise RuntimeError(f"Service executable not found: {service_exe}")

    conn_str = load_connection_string(config_path)
    conn = connect_from_npgsql(conn_str)
    conn.autocommit = True

    baseline_quality = None
    baseline_perf = None
    inserted_quality = False
    inserted_perf = False

    try:
        with conn.cursor() as cur:
            from_ts, to_ts = resolve_window(cur, args.serial, args.hours, args.from_ts, args.to_ts)

            baseline_quality = fetch_optional_quality(cur, args.serial)
            baseline_perf = fetch_optional_perf(cur, args.serial)

            before = metric_snapshot(cur, args.serial, from_ts, to_ts)
            print(f"Testing serial {args.serial} from {from_ts.isoformat()} to {to_ts.isoformat()}")
            print_snapshot("Baseline snapshot:", before)

            inserted_quality = baseline_quality is None
            inserted_perf = baseline_perf is None

            quality_update = QualityParams(
                tgt_good=0.99,
                tgt_peddler=0.005,
                tgt_bad=0.0025,
                tgt_recycle=0.0025,
                w_good=(baseline_quality.w_good if baseline_quality else 0.40),
                w_peddler=(baseline_quality.w_peddler if baseline_quality else 0.20),
                w_bad=(baseline_quality.w_bad if baseline_quality else 0.20),
                w_recycle=(baseline_quality.w_recycle if baseline_quality else 0.20),
                sig_k=max((baseline_quality.sig_k if baseline_quality else 4.0), 8.0),
            )
            perf_update = PerfParams(
                min_effective_fpm=100000.0,
                low_ratio_threshold=(baseline_perf.low_ratio_threshold if baseline_perf else 0.5),
                cap_asymptote=(baseline_perf.cap_asymptote if baseline_perf else 0.2),
            )

            run_cli(
                service_exe,
                "machine",
                "set-quality-params",
                "--serial",
                args.serial,
                "--tgt-good",
                f"{quality_update.tgt_good}",
                "--tgt-peddler",
                f"{quality_update.tgt_peddler}",
                "--tgt-bad",
                f"{quality_update.tgt_bad}",
                "--tgt-recycle",
                f"{quality_update.tgt_recycle}",
                "--w-good",
                f"{quality_update.w_good}",
                "--w-peddler",
                f"{quality_update.w_peddler}",
                "--w-bad",
                f"{quality_update.w_bad}",
                "--w-recycle",
                f"{quality_update.w_recycle}",
                "--sig-k",
                f"{quality_update.sig_k}",
            )
            run_cli(
                service_exe,
                "machine",
                "set-perf-params",
                "--serial",
                args.serial,
                "--min-effective",
                f"{perf_update.min_effective_fpm}",
                "--low-ratio",
                f"{perf_update.low_ratio_threshold}",
                "--cap-asymptote",
                f"{perf_update.cap_asymptote}",
            )

            refresh_throughput_caggs(cur, from_ts, to_ts)

        with conn.cursor() as cur:
            after = metric_snapshot(cur, args.serial, from_ts, to_ts)
            print_snapshot("Adjusted snapshot:", after)

            quality_changed = approx_changed(before["quality_avg"], after["quality_avg"])
            throughput_daily_changed = approx_changed(
                before["throughput_daily_avg"], after["throughput_daily_avg"]
            )
            throughput_cagg_changed = approx_changed(
                before["throughput_cagg_avg"], after["throughput_cagg_avg"]
            )

            if not quality_changed:
                raise RuntimeError("Quality ratio did not change after updating quality params.")
            if not throughput_daily_changed:
                raise RuntimeError(
                    "public.daily_throughput_components throughput_ratio did not change after updating perf params."
                )
            if not throughput_cagg_changed:
                raise RuntimeError(
                    "oee.v_throughput_minute_batch throughput_ratio did not change after refreshing throughput CAGGs."
                )

            print("Verification passed.")

    finally:
        try:
            with conn.cursor() as cur:
                if baseline_quality is not None:
                    run_cli(
                        service_exe,
                        "machine",
                        "set-quality-params",
                        "--serial",
                        args.serial,
                        "--tgt-good",
                        f"{baseline_quality.tgt_good}",
                        "--tgt-peddler",
                        f"{baseline_quality.tgt_peddler}",
                        "--tgt-bad",
                        f"{baseline_quality.tgt_bad}",
                        "--tgt-recycle",
                        f"{baseline_quality.tgt_recycle}",
                        "--w-good",
                        f"{baseline_quality.w_good}",
                        "--w-peddler",
                        f"{baseline_quality.w_peddler}",
                        "--w-bad",
                        f"{baseline_quality.w_bad}",
                        "--w-recycle",
                        f"{baseline_quality.w_recycle}",
                        "--sig-k",
                        f"{baseline_quality.sig_k}",
                    )
                elif inserted_quality:
                    delete_row(cur, "oee.quality_params", args.serial)

                if baseline_perf is not None:
                    run_cli(
                        service_exe,
                        "machine",
                        "set-perf-params",
                        "--serial",
                        args.serial,
                        "--min-effective",
                        f"{baseline_perf.min_effective_fpm}",
                        "--low-ratio",
                        f"{baseline_perf.low_ratio_threshold}",
                        "--cap-asymptote",
                        f"{baseline_perf.cap_asymptote}",
                    )
                elif inserted_perf:
                    delete_row(cur, "oee.perf_params", args.serial)

                if "from_ts" in locals() and "to_ts" in locals():
                    refresh_throughput_caggs(cur, from_ts, to_ts)

                print("Original parameter state restored.")
        finally:
            conn.close()

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise
