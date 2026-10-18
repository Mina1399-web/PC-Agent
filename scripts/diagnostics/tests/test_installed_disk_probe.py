"""Run only extracted SQLite helpers against disposable fixtures, never installed data."""
import ast
import pathlib
import sqlite3
import stat
import tempfile
import time
import unittest


class ProbeFailureClassificationTests(unittest.TestCase):
    def setUp(self):
        script = pathlib.Path(__file__).resolve().parents[1] / 'Test-InstalledDiskCounters.ps1'
        code = script.read_text(encoding='utf-8-sig').split("$taskCode = @'", 1)[1].split("\n'@", 1)[0]
        tree = ast.parse(code)
        nodes = [n for n in tree.body if
                 isinstance(n, ast.FunctionDef) and n.name in ('guard_files', 'inspect_database') or
                 isinstance(n, ast.Assign) and any(isinstance(t, ast.Name) and
                     t.id in ('REASONS', 'CAP') for t in n.targets)]
        self.ns = dict(pathlib=pathlib, sqlite3=sqlite3, stat=stat, time=time)
        exec(compile(ast.Module(body=nodes, type_ignores=[]), '<sqlite-helper-only>', 'exec'), self.ns)
        self.temp = tempfile.TemporaryDirectory(prefix='hostagent-probe-regression-')
        self.addCleanup(self.temp.cleanup)
        self.path = pathlib.Path(self.temp.name) / 'fixture.db'

    def check(self):
        return self.ns['inspect_database'](self.path)

    def database(self):
        connection = sqlite3.connect(self.path)
        connection.execute('CREATE TABLE fixture(value)')
        connection.commit()
        return connection

    def test_missing_database_has_exact_reason_without_creating_it(self):
        result = self.check()
        self.assertEqual('NOTRUN', result['quickCheck'])
        self.assertEqual('file_missing', result.get('failureCode'))
        self.assertFalse(self.path.exists())

    def test_missing_wal_sidecars_are_not_reported_as_corruption(self):
        connection = self.database()
        connection.execute('PRAGMA journal_mode=WAL')
        connection.close()
        before = {p.name for p in self.path.parent.iterdir()}
        result = self.check()
        self.assertEqual('NOTRUN', result['quickCheck'])
        self.assertEqual('wal_sidecars_required', result.get('failureCode'))
        self.assertEqual(before, {p.name for p in self.path.parent.iterdir()})

    def test_size_guard_remains_enforced_and_distinguishable(self):
        self.database().close()
        self.ns['CAP'] = 1
        result = self.check()
        self.assertEqual('NOTRUN', result['quickCheck'])
        self.assertEqual('size_guard', result.get('failureCode'))

    def test_invalid_header_is_distinguishable_without_exposing_file_contents(self):
        self.path.write_bytes(b'synthetic-private-content' * 6)
        result = self.check()
        self.assertEqual('header_guard', result.get('failureCode'))
        self.assertNotIn('synthetic-private', str(result))

    def test_permission_denial_is_distinct_and_exception_message_is_not_exposed(self):
        def denied(*args):
            raise PermissionError('synthetic-private-message')
        self.ns['guard_files'] = denied
        result = self.check()
        self.assertEqual('access_denied', result.get('failureCode'))
        self.assertNotIn('synthetic-private', str(result))

    def test_unknown_guard_text_is_redacted(self):
        def unknown(*args):
            raise ValueError('synthetic-private-message')
        self.ns['guard_files'] = unknown
        result = self.check()
        self.assertEqual('probe_operation_failed', result.get('failureCode'))
        self.assertNotIn('synthetic-private', str(result))

    def test_timeout_is_not_classified_as_generic_file_io(self):
        def timed_out(*args):
            raise TimeoutError('synthetic-private-message')
        self.ns['guard_files'] = timed_out
        self.assertEqual('deadline', self.check().get('failureCode'))

    def test_healthy_database_still_passes_without_data_changes(self):
        self.database().close()
        before = self.path.read_bytes()
        result = self.check()
        self.assertEqual('ok', result['quickCheck'])
        self.assertNotIn('failureCode', result)
        self.assertEqual(before, self.path.read_bytes())


if __name__ == '__main__':
    unittest.main(verbosity=2)
