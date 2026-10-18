"""Post-recovery check only: stopped GUID fixture, no source/DB writes."""
import json
import os
import pathlib
import sqlite3
import sys
import time
import uuid
from contextlib import closing

run_id = sys.argv[1]
if uuid.UUID(hex=run_id).hex != run_id:
    sys.exit("invalid_run_id")
root = pathlib.Path(os.environ["ProgramData"]) / ("HostAgent-DEV-SCM-" + run_id)
data = root / "data"
for path in (root, data, *root.parents):
    if path.is_symlink() or path.is_junction():
        sys.exit("reparse_path")
ready = json.loads((data / "crash-proof-ready.json").read_text(encoding="utf-8"))
if ready["runId"] != run_id or ready["pendingTransactions"] != 2 or not ready["committed"]:
    sys.exit("invalid_proof_marker")
if (data / "crash-proof-expired.flag").exists():
    sys.exit("barrier_expired_or_gracefully_cancelled")
results = []
for name in ("host-agent.db", "notifications.db"):
    database = data / name
    if not database.is_file() or database.is_symlink() or database.is_junction():
        sys.exit("missing_or_reparse_database")
    for suffix in ("-wal", "-journal"):
        sidecar = pathlib.Path(str(database) + suffix)
        if sidecar.exists() and sidecar.stat().st_size:
            sys.exit("nonempty_wal_or_journal_check_not_run")
    with closing(sqlite3.connect(database.as_uri() + "?mode=ro&immutable=1", uri=True, timeout=3)) as db:
        db.execute("PRAGMA query_only=ON")
        db.execute("PRAGMA temp_store=MEMORY")
        deadline = time.monotonic() + 10
        db.set_progress_handler(lambda: int(time.monotonic() >= deadline), 1000)
        quick = db.execute("PRAGMA quick_check").fetchall() == [("ok",)]
        kinds = db.execute("SELECT kind FROM dev_scm_crash_proof WHERE run_id=? ORDER BY kind", (run_id,)).fetchall()
        preserved = kinds == [("committed",)]
        snapshots = True
        if name == "host-agent.db":
            ids = ready["snapshotIds"]
            if not 1 <= len(ids) <= 64:
                sys.exit("invalid_snapshot_bounds")
            snapshots = all(db.execute("SELECT EXISTS(SELECT 1 FROM telemetry_snapshots WHERE snapshot_id=?)", (value,)).fetchone()[0] for value in ids)
        results.append(dict(database=name, quickCheck="ok" if quick else "not_ok", committedProofPreserved=preserved,
                            uncommittedProofAbsent=all(kind[0] != "pending" for kind in kinds), acknowledgedSnapshotsPreserved=bool(snapshots)))
        if not quick or not preserved or not snapshots:
            sys.exit("recovery_invariant_failed")
print(json.dumps(results))
