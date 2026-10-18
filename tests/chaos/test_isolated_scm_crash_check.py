import hashlib
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import unittest
import uuid


class CrashCheckTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="HostAgent-crash-check-")
        self.addCleanup(self.temporary.cleanup)
        self.id = uuid.uuid4().hex
        self.root = Path(self.temporary.name)
        self.data = self.root / ("HostAgent-DEV-SCM-" + self.id) / "data"
        self.data.mkdir(parents=True)
        for name in ("host-agent.db", "notifications.db"):
            with sqlite3.connect(self.data / name) as db:
                db.execute("CREATE TABLE dev_scm_crash_proof(run_id TEXT,kind TEXT)")
                db.execute("INSERT INTO dev_scm_crash_proof VALUES(?,'committed')", (self.id,))
                db.execute("CREATE TABLE telemetry_snapshots(snapshot_id TEXT)")
                db.execute("INSERT INTO telemetry_snapshots VALUES('acknowledged')")
            db.close()
        (self.data / "crash-proof-ready.json").write_text(json.dumps(dict(
            runId=self.id, pendingTransactions=2, committed=True, snapshotIds=["acknowledged"])), encoding="utf-8")

    def check(self):
        environment = os.environ.copy()
        environment["ProgramData"] = str(self.root)
        source = Path(__file__).resolve().parents[2] / "scripts/chaos/check-isolated-scm-crash.py"
        return subprocess.run([sys.executable, "-I", "-B", str(source), self.id],
                              env=environment, capture_output=True, text=True, timeout=20)

    def test_healthy_committed_proofs_and_snapshots_without_file_changes(self):
        before = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in self.data.iterdir() if p.is_file()}
        result = self.check()
        self.assertEqual(0, result.returncode, result.stderr)
        rows = json.loads(result.stdout)
        self.assertEqual(2, len(rows))
        self.assertTrue(all(r["quickCheck"] == "ok" and r["committedProofPreserved"] and r["uncommittedProofAbsent"] for r in rows))
        after = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in self.data.iterdir() if p.is_file()}
        self.assertEqual(before, after)

    def test_pending_row_in_notifications_cannot_pass(self):
        with sqlite3.connect(self.data / "notifications.db") as db:
            db.execute("INSERT INTO dev_scm_crash_proof VALUES(?,'pending')", (self.id,))
        db.close()
        self.assertNotEqual(0, self.check().returncode)

    def test_nonempty_wal_cannot_be_ignored_by_immutable_check(self):
        (self.data / "host-agent.db-wal").write_bytes(b"test-uncheckpointed-state")
        result = self.check()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("nonempty_wal_or_journal", result.stderr)

    def test_lost_acknowledged_snapshot_cannot_pass(self):
        with sqlite3.connect(self.data / "host-agent.db") as db:
            db.execute("DELETE FROM telemetry_snapshots")
        db.close()
        self.assertNotEqual(0, self.check().returncode)


if __name__ == "__main__":
    unittest.main()
