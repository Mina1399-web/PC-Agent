"""Synthetic disposable proof data only; runs the real readonly checker."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sqlite3
import tempfile
import unittest
from contextlib import closing

source = Path(__file__).resolve().parents[2] / 'scripts' / 'chaos' / 'check-reboot-persistence.py'
spec = importlib.util.spec_from_file_location('reboot_check', source)
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class ReadOnlyRebootCheck(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='HostAgent-reboot-check-')
        self.previous = os.environ.get('ProgramData')
        os.environ['ProgramData'] = self.temp.name
        self.id = 'a7e26ca9929b48b5b36d1b2556c41bda'
        self.data = Path(self.temp.name) / ('HostAgent-DEV-SCM-' + self.id) / 'data'
        self.data.mkdir(parents=True)
        (self.data / 'reboot-proof-ready.json').write_text(json.dumps(dict(runId=self.id, committed=True, pendingTransactions=2, snapshotIds=['ack'])), encoding='utf-8')
        for name in ('host-agent.db', 'notifications.db'):
            with closing(sqlite3.connect(self.data / name)) as db, db:
                db.execute('CREATE TABLE dev_scm_crash_proof(run_id TEXT,kind TEXT)')
                db.execute('INSERT INTO dev_scm_crash_proof VALUES(?,?)', (self.id, 'committed'))
                db.execute('INSERT INTO dev_scm_crash_proof VALUES(?,?)', (self.id, 'post_boot'))
                if name == 'host-agent.db':
                    db.execute('CREATE TABLE telemetry_snapshots(snapshot_id TEXT,sampled_at_utc TEXT)')
                    db.executemany('INSERT INTO telemetry_snapshots VALUES(?,?)', [('ack','2026-10-18T01:00:00+00:00'),('new','2026-10-18T02:00:10+00:00')])

    def tearDown(self):
        if self.previous is None:
            os.environ.pop('ProgramData', None)
        else:
            os.environ['ProgramData'] = self.previous
        self.temp.cleanup()

    def test_valid_proof_uses_both_databases_without_writing_or_sidecars(self):
        before = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in self.data.iterdir()}
        result = checker.check(self.id, '2026-10-18T02:00:00Z')
        self.assertEqual(['host-agent.db', 'notifications.db'], [row['database'] for row in result])
        self.assertTrue(result[0]['acknowledgedSnapshotsPreserved'])
        self.assertIsNone(result[1]['acknowledgedSnapshotsPreserved'])
        self.assertTrue(all(row['quickCheck'] == 'ok' and row['committedProofPreserved'] and row['uncommittedProofAbsent'] for row in result))
        self.assertEqual(before, {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in self.data.iterdir()})

    def test_persisted_pending_is_rejected(self):
        with closing(sqlite3.connect(self.data / 'notifications.db')) as db, db:
            db.execute('INSERT INTO dev_scm_crash_proof VALUES(?,?)', (self.id, 'pending'))
        with self.assertRaisesRegex(ValueError, 'invariant_failed'):
            checker.check(self.id, '2026-10-18T02:00:00Z')

    def test_no_fresh_runtime_progress_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'invariant_failed'):
            checker.check(self.id, '2026-10-18T03:00:00Z')

    def test_notification_post_boot_write_is_required(self):
        with closing(sqlite3.connect(self.data / 'notifications.db')) as db, db:
            db.execute("DELETE FROM dev_scm_crash_proof WHERE kind='post_boot'")
        with self.assertRaisesRegex(ValueError, 'invariant_failed'):
            checker.check(self.id, '2026-10-18T02:00:00Z')

    def test_missing_acknowledged_row_is_rejected(self):
        with closing(sqlite3.connect(self.data / 'host-agent.db')) as db, db:
            db.execute("DELETE FROM telemetry_snapshots WHERE snapshot_id='ack'")
        with self.assertRaisesRegex(ValueError, 'invariant_failed'):
            checker.check(self.id, '2026-10-18T02:00:00Z')

    def test_nonempty_wal_is_never_ignored_in_immutable_mode(self):
        (self.data / 'host-agent.db-wal').write_bytes(b'synthetic uncheckpointed WAL')
        with self.assertRaisesRegex(ValueError, 'sidecar'):
            checker.check(self.id, '2026-10-18T02:00:00Z')


if __name__ == '__main__':
    unittest.main()
