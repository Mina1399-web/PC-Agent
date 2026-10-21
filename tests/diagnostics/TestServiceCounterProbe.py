import ctypes as C
import importlib.util
import os
import pathlib
import unittest
from unittest.mock import patch

path = pathlib.Path(__file__).parents[2] / 'scripts/diagnostics/Read-ServiceDiskCounters.py'
spec = importlib.util.spec_from_file_location('counter_probe', path)
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class ServiceCounterProbeTests(unittest.TestCase):
    def current_sid(self):
        process = probe.kernel.OpenProcess(0x1000, False, os.getpid())
        token = probe.W.HANDLE()
        try:
            self.assertTrue(probe.advapi.OpenProcessToken(process, 8, C.byref(token)))
            return probe.sid_of(token)
        finally:
            if token:
                probe.kernel.CloseHandle(token)
            if process:
                probe.kernel.CloseHandle(process)

    def test_fixed_native_probe_runs_under_copied_own_token_and_reverts(self):
        self.assertIsNone(probe.thread_sid())
        result = probe.probe(os.getpid(), self.current_sid())
        self.assertIsNone(probe.thread_sid())
        self.assertRegex(result['openQuery'], r'^0x[0-9A-F]{8}$')
        self.assertEqual(result['closeQuery'], '0x00000000')
        self.assertEqual([c['id'] for c in result['counters']], ['read', 'write', 'latency'])
        for counter in result['counters']:
            self.assertRegex(counter['addStatus'], r'^0x[0-9A-F]{8}$')
            self.assertNotIn('value', counter)
        if any(c['addStatus'] == '0x00000000' for c in result['counters']):
            self.assertEqual(len(result['samples']), 2)
        process = result['processCapabilities']
        self.assertGreater(process['examined'], 0)
        self.assertEqual(process['examined'], sum(process[k] for k in ('cpuReadOk', 'accessDenied', 'processUnavailable', 'otherUnavailable')))
        self.assertLessEqual(process['examined'], 4096)
        self.assertNotIn('pids', process)

    def test_wrong_token_subject_is_rejected_without_impersonation(self):
        self.assertIsNone(probe.thread_sid())
        with self.assertRaises(probe.ProbeError) as error:
            probe.probe(os.getpid(), 'S-1-5-80-0')
        self.assertEqual(error.exception.stage, 'service_token_sid_mismatch')
        self.assertIsNone(probe.thread_sid())

    def test_idle_pid_invalid_parameter_is_not_reported_as_confirmed_process_exit(self):
        def enumerate_idle(pids, size, used):
            pids[0] = 0
            C.cast(used, C.POINTER(probe.W.DWORD))[0] = 4
            return True
        def inaccessible(*args):
            C.set_last_error(87)
            return None
        with patch.object(probe.kernel, 'K32EnumProcesses', enumerate_idle), patch.object(probe.kernel, 'OpenProcess', inaccessible):
            result = probe.process_capabilities()
        self.assertEqual(result['examined'], 1)
        self.assertEqual(result['processUnavailable'], 1)
        self.assertEqual(result['cpuReadOk'], 0)
        self.assertNotIn('processGone', result)

    def test_inaccessible_process_is_classified_without_impersonation(self):
        with self.assertRaises(probe.ProbeError) as error:
            probe.probe(0, self.current_sid())
        self.assertEqual(error.exception.stage, 'open_process')
        self.assertGreater(error.exception.code, 0)
        self.assertIsNone(probe.thread_sid())


if __name__ == '__main__':
    unittest.main(verbosity=2)
