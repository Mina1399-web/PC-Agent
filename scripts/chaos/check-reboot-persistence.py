"""Stopped GUID S5 fixture only; immutable read-only database inspection."""
import json
import os
import pathlib
import sqlite3
import sys
import time
import uuid
from contextlib import closing


def check(run_id, boot_after):
    if uuid.UUID(hex=run_id).hex != run_id or uuid.UUID(hex=run_id).int == 0:
        raise ValueError('reboot_invalid_guid')
    root = pathlib.Path(os.environ['ProgramData']) / ('HostAgent-DEV-SCM-' + run_id)
    data = root / 'data'
    for path in (data, root, *root.parents):
        if path.is_symlink() or path.is_junction():
            raise ValueError('reboot_reparse_path')
    marker = data / 'reboot-proof-ready.json'
    if marker.is_symlink() or marker.is_junction() or marker.stat().st_size > 16384:
        raise ValueError('reboot_invalid_marker')
    ready = json.loads(marker.read_text(encoding='utf-8-sig'))
    ids = ready.get('snapshotIds', [])
    if ready.get('runId') != run_id or ready.get('committed') is not True or ready.get('pendingTransactions') != 2 or not 1 <= len(ids) <= 64 or len(set(ids)) != len(ids):
        raise ValueError('reboot_invalid_ready_proof')
    results = []
    for name in ('host-agent.db', 'notifications.db'):
        database = data / name
        if not database.is_file() or database.is_symlink() or database.is_junction():
            raise ValueError('reboot_database_path_invalid')
        if database.stat().st_size > 128 * 1024 * 1024:
            raise ValueError('reboot_disposable_database_too_large')
        for suffix in ('-wal', '-journal'):
            sidecar = pathlib.Path(str(database) + suffix)
            if sidecar.is_symlink() or sidecar.is_junction() or (sidecar.exists() and sidecar.stat().st_size):
                raise ValueError('reboot_nonempty_or_reparse_sidecar')
        with closing(sqlite3.connect(database.as_uri() + '?mode=ro&immutable=1', uri=True, timeout=3)) as db:
            db.execute('PRAGMA query_only=ON')
            db.execute('PRAGMA temp_store=MEMORY')
            deadline = time.monotonic() + 10
            db.set_progress_handler(lambda: int(time.monotonic() >= deadline), 1000)
            quick = db.execute('PRAGMA quick_check').fetchall() == [('ok',)]
            kinds = db.execute('SELECT kind FROM dev_scm_crash_proof WHERE run_id=? ORDER BY kind', (run_id,)).fetchall()
            committed = kinds == [('committed',), ('post_boot',)]
            post_boot_written = ('post_boot',) in kinds
            preserved, progress = True, True
            if name == 'host-agent.db':
                preserved = all(db.execute('SELECT EXISTS(SELECT 1 FROM telemetry_snapshots WHERE snapshot_id=?)', (value,)).fetchone()[0] for value in ids)
                # ISO encodings can differ (+00:00/Z). SQLite normalizes before comparison.
                progress = db.execute('SELECT COUNT(*) FROM telemetry_snapshots WHERE julianday(sampled_at_utc) >= julianday(?)', (boot_after,)).fetchone()[0] > 0
            results.append(dict(database=name, quickCheck='ok' if quick else 'not_ok', committedProofPreserved=committed,
                                uncommittedProofAbsent=all(row[0] != 'pending' for row in kinds), acknowledgedSnapshotsPreserved=bool(preserved) if name == 'host-agent.db' else None, postBootProgress=bool(progress and post_boot_written)))
            if not (quick and committed and preserved and progress):
                raise ValueError('reboot_persistence_invariant_failed')
    return results


if __name__ == '__main__':
    try:
        print(json.dumps(check(sys.argv[1], sys.argv[2])))
    except (sqlite3.Error, ValueError, OSError, KeyError, TypeError, IndexError):
        sys.exit('reboot_readonly_check_failed')
