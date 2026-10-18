"""Metadata-only integrity check of a stopped disposable runtime fixture. No -c."""
import json
import os
import pathlib
import re
import sqlite3
import sys
import time

def check(run):
    if not re.fullmatch(r"[a-f0-9]{32}", run):
        raise ValueError("invalid_fixture_id")
    root = pathlib.Path(os.environ["LOCALAPPDATA"]) / "HostAgent-DEV-evidence" / ("runtime-candidate-" + run)
    return check_root(root)

def check_root(root):
    results = []
    locks = []
    try:
        for name in ("fixture.db", "notices.db"):
            path = root / name
            # Read-only immutable never applies a WAL. Refuse to hide pending WAL evidence.
            for parent in (path, *path.parents):
                if parent.is_symlink() or parent.is_junction():
                    raise ValueError("reparse_path")
            for suffix in ("-wal", "-journal"):
                sidecar = pathlib.Path(str(path) + suffix)
                if sidecar.exists() and sidecar.stat().st_size:
                    raise ValueError("nonempty_wal_or_journal")
            locks.append(open(path, "rb"))
            connection = sqlite3.connect(path.as_uri() + "?mode=ro&immutable=1", uri=True, timeout=3)
            try:
                connection.execute("PRAGMA query_only=ON")
                connection.execute("PRAGMA temp_store=MEMORY")
                deadline = time.monotonic() + 10
                connection.set_progress_handler(lambda: int(time.monotonic() >= deadline), 1000)
                healthy = connection.execute("PRAGMA quick_check").fetchall() == [("ok",)]
                results.append({"database": name, "quickCheck": "ok" if healthy else "not_ok"})
            finally:
                connection.close()
        return {"ok": all(item["quickCheck"] == "ok" for item in results), "databases": results}
    finally:
        for handle in locks:
            handle.close()

if __name__ == "__main__":
    try:
        if sqlite3.sqlite_version_info < (3, 22, 0):
            raise ValueError("unsupported_sqlite")
        result = check(sys.argv[1])
        print(json.dumps(result))
        sys.exit(0 if result["ok"] else 1)
    except (OSError, ValueError, sqlite3.Error, IndexError, KeyError):
        print(json.dumps({"ok": False, "code": "readonly_fixture_check_failed_or_blocked"}))
        sys.exit(1)
