import hashlib
import importlib.util
import pathlib
import sqlite3
import tempfile
import unittest

spec=importlib.util.spec_from_file_location("checker", pathlib.Path(__file__).resolve().parents[2]/"scripts/chaos/check-remaining-runtime.py")
checker=importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)

class Checks(unittest.TestCase):
    def fixture(self, root):
        for name in ("fixture.db", "notices.db"):
            connection=sqlite3.connect(root/name)
            connection.execute("CREATE TABLE fixture (id INTEGER)")
            connection.commit()
            connection.close()

    def test_readonly_both_files_and_inventory_unchanged(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory);self.fixture(root)
            before={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in root.iterdir()}
            self.assertTrue(checker.check_root(root)["ok"])
            self.assertEqual(before,{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in root.iterdir()})

    def test_never_ignores_nonempty_wal(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory);self.fixture(root)
            (root/"fixture.db-wal").write_bytes(b"fixture")
            with self.assertRaises(ValueError): checker.check_root(root)

    def test_corruption_is_not_repaired(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory);self.fixture(root)
            corrupt=b"corrupt fixture"*100
            (root/"notices.db").write_bytes(corrupt)
            with self.assertRaises(sqlite3.Error): checker.check_root(root)
            self.assertEqual(corrupt,(root/"notices.db").read_bytes())

if __name__ == "__main__": unittest.main()
