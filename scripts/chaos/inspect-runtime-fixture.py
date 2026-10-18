"""Read only a GUID-derived disposable runtime fixture; print bounded metadata."""
import json
import os
from pathlib import Path
import sqlite3
import sys
import uuid

fixture = uuid.UUID(hex=sys.argv[1])
if fixture.hex != sys.argv[1]:
    raise ValueError("invalid_fixture_id")
base = Path(os.environ["LOCALAPPDATA"]) / "HostAgent-DEV-evidence"
kind = sys.argv[2] if len(sys.argv) > 2 else "runtime"
if kind not in ("runtime", "persistence"):
    raise ValueError("invalid_fixture_kind")
directory = base / (kind + "-candidate-" + fixture.hex)
for parent in (directory, *directory.parents):
    if parent.is_symlink() or parent.is_junction():
        raise ValueError("fixture_reparse_point")
path = directory / "fixture.db"
if path.is_symlink() or path.is_junction():
    raise ValueError("fixture_reparse_point")
with sqlite3.connect(path.as_uri() + "?mode=ro", uri=True, timeout=3) as db:
    cpu = db.execute(
        "SELECT sampled_at_utc,value FROM baseline_samples "
        "WHERE metric_name='cpu.usage.percent' AND scope='host' ORDER BY sampled_at_utc LIMIT 32"
    ).fetchall()
    telemetry = db.execute(
        "SELECT collector_id,COUNT(*),MAX(sampled_at_utc) FROM telemetry_snapshots GROUP BY collector_id LIMIT 16"
    ).fetchall()
    counts = db.execute("SELECT COUNT(*) FROM baseline_origins").fetchone()[0]
    incidents = []
    for (payload,) in db.execute("SELECT payload_json FROM incidents LIMIT 64"):
        incident = json.loads(payload)
        diagnosis = incident["LatestDiagnosis"]
        if incident["CorrelationKey"].startswith("cpu_sustained:"):
            incidents.append({
                "code": diagnosis["ProbableRootCause"]["Code"],
                "confidence": diagnosis["Confidence"],
                "at": diagnosis["ObservedAtUtc"],
            })
    print(json.dumps({
        "fixture": fixture.hex,
        "cpu": cpu,
        "telemetry": telemetry,
        "baselineSeries": counts,
        "cpuIncidents": incidents,
        "quickCheck": db.execute("PRAGMA quick_check").fetchone()[0],
    }))
