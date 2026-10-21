"""Fixed PDH metadata probe; no privilege adjustment or service/configuration writes."""
import ctypes as C
from ctypes import wintypes as W
import json
import os
import sys
import time

kernel = C.WinDLL('kernel32', use_last_error=True)
advapi = C.WinDLL('advapi32', use_last_error=True)
kernel.OpenProcess.argtypes = (W.DWORD, W.BOOL, W.DWORD)
kernel.OpenProcess.restype = W.HANDLE
kernel.CloseHandle.argtypes = (W.HANDLE,)
kernel.CloseHandle.restype = W.BOOL
kernel.GetCurrentThread.restype = W.HANDLE
kernel.LocalFree.argtypes = (W.HANDLE,)
advapi.OpenProcessToken.argtypes = (W.HANDLE, W.DWORD, C.POINTER(W.HANDLE))
advapi.OpenThreadToken.argtypes = (W.HANDLE, W.DWORD, W.BOOL, C.POINTER(W.HANDLE))
advapi.DuplicateTokenEx.argtypes = (W.HANDLE, W.DWORD, W.LPVOID, W.DWORD, W.DWORD, C.POINTER(W.HANDLE))
advapi.GetTokenInformation.argtypes = (W.HANDLE, W.DWORD, W.LPVOID, W.DWORD, C.POINTER(W.DWORD))
advapi.ConvertSidToStringSidW.argtypes = (W.LPVOID, C.POINTER(W.LPWSTR))
advapi.ImpersonateLoggedOnUser.argtypes = (W.HANDLE,)
advapi.RevertToSelf.restype = W.BOOL


class ProbeError(Exception):
    def __init__(self, stage, code=None):
        self.stage, self.code = stage, C.get_last_error() if code is None else code


def sid_of(token):
    required = W.DWORD()
    advapi.GetTokenInformation(token, 1, None, 0, C.byref(required))
    if not 0 < required.value < 65536:
        raise ProbeError('token_user_size')
    buffer = C.create_string_buffer(required.value)
    if not advapi.GetTokenInformation(token, 1, buffer, required, C.byref(required)):
        raise ProbeError('token_user')
    sid = C.cast(buffer, C.POINTER(W.LPVOID))[0]
    text = W.LPWSTR()
    if not advapi.ConvertSidToStringSidW(sid, C.byref(text)):
        raise ProbeError('token_sid')
    try:
        return text.value
    finally:
        kernel.LocalFree(C.cast(text, W.HANDLE))


def thread_sid():
    token = W.HANDLE()
    if not advapi.OpenThreadToken(kernel.GetCurrentThread(), 8, True, C.byref(token)):
        if C.get_last_error() == 1008:  # ERROR_NO_TOKEN: normal non-impersonated thread
            return None
        raise ProbeError('thread_token')
    try:
        return sid_of(token)
    finally:
        kernel.CloseHandle(token)


def process_capabilities():
    # Counts only: no PID, process name, path, memory amount or CPU times leave
    # the child. These are fixed read-access probes, not a process executor.
    kernel.K32EnumProcesses.argtypes = (C.POINTER(W.DWORD), W.DWORD, C.POINTER(W.DWORD))
    kernel.K32EnumProcesses.restype = W.BOOL
    kernel.GetProcessTimes.argtypes = (W.HANDLE,) + (C.POINTER(W.FILETIME),) * 4
    kernel.GetProcessTimes.restype = W.BOOL
    pids, used = (W.DWORD * 4096)(), W.DWORD()
    result = dict(enumerated=0, examined=0, cpuReadOk=0, accessDenied=0, processUnavailable=0, otherUnavailable=0, truncated=False)
    if not kernel.K32EnumProcesses(pids, C.sizeof(pids), C.byref(used)):
        result['enumerationUnavailable'] = True
        return result
    result['enumerated'] = min(4096, used.value // C.sizeof(W.DWORD))
    deadline = time.monotonic() + 3
    result['truncated'] = used.value >= C.sizeof(pids)
    for pid in pids[:result['enumerated']]:
        if time.monotonic() >= deadline:
            result['truncated'] = True
            break
        result['examined'] += 1
        process = kernel.OpenProcess(0x1000, False, pid)
        if not process:
            code = C.get_last_error()
        else:
            try:
                times = [W.FILETIME() for _ in range(4)]
                code = 0 if kernel.GetProcessTimes(process, *(C.byref(t) for t in times)) else C.get_last_error()
            finally:
                kernel.CloseHandle(process)
        # PID 0 predictably returns 87 while alive; neither code proves exit.
        key = 'cpuReadOk' if code == 0 else 'accessDenied' if code == 5 else 'processUnavailable' if code in (87, 1168) else 'otherUnavailable'
        result[key] += 1
    return result


def counters():
    # Load only inside the selected context; use a fresh child for each probe.
    pdh = C.WinDLL('pdh')
    pdh.PdhOpenQueryW.argtypes = (W.LPCWSTR, C.c_size_t, C.POINTER(W.HANDLE))
    pdh.PdhAddEnglishCounterW.argtypes = (W.HANDLE, W.LPCWSTR, C.c_size_t, C.POINTER(W.HANDLE))
    pdh.PdhCollectQueryData.argtypes = (W.HANDLE,)
    pdh.PdhCloseQuery.argtypes = (W.HANDLE,)
    class Value(C.Structure):
        _fields_ = [('status', W.DWORD), ('value', C.c_double)]
    pdh.PdhGetFormattedCounterValue.argtypes = (W.HANDLE, W.DWORD, C.POINTER(W.DWORD), C.POINTER(Value))
    for name in ('PdhOpenQueryW', 'PdhAddEnglishCounterW', 'PdhCollectQueryData', 'PdhCloseQuery', 'PdhGetFormattedCounterValue'):
        getattr(pdh, name).restype = W.DWORD
    hx = lambda code: '0x%08X' % code
    query = W.HANDLE()
    status = pdh.PdhOpenQueryW(None, 0, C.byref(query))
    result = {'openQuery': hx(status), 'counters': [], 'samples': []}
    if status:
        return result
    try:
        handles = []
        for key, leaf in (('read', 'Disk Read Bytes/sec'), ('write', 'Disk Write Bytes/sec'), ('latency', 'Avg. Disk sec/Transfer')):
            handle = W.HANDLE()
            code = pdh.PdhAddEnglishCounterW(query, '\\PhysicalDisk(_Total)\\' + leaf, 0, C.byref(handle))
            result['counters'].append({'id': key, 'addStatus': hx(code)})
            handles.append(handle if code == 0 else None)
        if any(handles):
            result['samples'].append(hx(pdh.PdhCollectQueryData(query)))
            time.sleep(1)
            result['samples'].append(hx(pdh.PdhCollectQueryData(query)))
            for row, handle in zip(result['counters'], handles):
                if handle:
                    value, kind = Value(), W.DWORD()
                    code = pdh.PdhGetFormattedCounterValue(handle, 0x200, C.byref(kind), C.byref(value))
                    row['formatStatus'] = hx(code)
                    if not code:
                        row['dataStatus'] = hx(value.status)
        return result
    finally:
        result['closeQuery'] = hx(pdh.PdhCloseQuery(query))


def probe(pid, expected_sid):
    if thread_sid() is not None:
        raise ProbeError('preexisting_thread_token', 0)
    process, token, duplicate = None, W.HANDLE(), W.HANDLE()
    impersonated = False
    try:
        process = kernel.OpenProcess(0x1000, False, pid)  # QUERY_LIMITED_INFORMATION only
        if not process:
            raise ProbeError('open_process')
        if not advapi.OpenProcessToken(process, 0xA, C.byref(token)):  # QUERY | DUPLICATE
            raise ProbeError('open_process_token')
        if sid_of(token) != expected_sid:
            raise ProbeError('service_token_sid_mismatch', 0)
        if not advapi.DuplicateTokenEx(token, 0xC, None, 2, 2, C.byref(duplicate)):  # QUERY | IMPERSONATE
            raise ProbeError('duplicate_token')
        if not advapi.ImpersonateLoggedOnUser(duplicate):
            raise ProbeError('impersonate_token')
        impersonated = True
        if thread_sid() != expected_sid:
            raise ProbeError('effective_token_sid_mismatch', 0)
        result = counters()
        result['processCapabilities'] = process_capabilities()
        return result
    finally:
        if impersonated and not advapi.RevertToSelf():
            os._exit(74)  # Never continue under an unexpectedly retained identity.
        for handle in (duplicate, token, process):
            if handle:
                kernel.CloseHandle(handle)


def main():
    try:
        if len(sys.argv) == 4 and sys.argv[1] == 'service' and sys.argv[2].isdigit() and sys.argv[3].startswith('S-1-5-80-'):
            result = probe(int(sys.argv[2]), sys.argv[3])
            context = 'verified_service_token_impersonation_not_full_SCM_process'
        elif sys.argv[1:] == ['interactive']:
            if thread_sid() is not None:
                raise ProbeError('preexisting_thread_token', 0)
            result, context = counters(), 'elevated_interactive_operator'
            result['processCapabilities'] = process_capabilities()
        else:
            raise ProbeError('invalid_arguments', 0)
        if thread_sid() is not None:
            raise ProbeError('token_revert_not_confirmed', 0)
        print(json.dumps({'completed': True, 'context': context, 'native': result, 'tokenReverted': True,
                          'privilegesChanged': False, 'serviceControlPerformed': False}))
        return 0
    except ProbeError as error:
        print(json.dumps({'completed': False, 'stage': error.stage, 'win32': error.code,
                          'privilegesChanged': False, 'serviceControlPerformed': False}))
        return 1


if __name__ == '__main__':
    sys.exit(main())
