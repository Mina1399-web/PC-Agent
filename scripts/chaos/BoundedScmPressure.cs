using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

public static class BoundedScmPressure
{
    public static string Decide(double freeMiB, double elapsedSeconds, bool parentAlive, bool marker, bool cancelled)
    {
        if (double.IsNaN(freeMiB) || double.IsInfinity(freeMiB) || freeMiB <= 512) return "hard_abort";
        if (!parentAlive) return "parent_lost";
        if (cancelled) return "cancelled";
        if (elapsedSeconds >= 60) return "deadline";
        if (marker) return "marker";
        return freeMiB <= 752 ? "hold" : "allocate";
    }

    public static double FreeMiB()
    {
        var status = new MemoryStatus();
        status.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
        if (!GlobalMemoryStatusEx(ref status)) return double.NaN;
        return status.AvailablePhysical / 1048576d;
    }

    // No SCM calls. The child cannot allocate until its parent assigns the job and opens the gate.
    public static void Run(int parentId, long parentTicks, string gate, string marker, string cancel)
    {
        var clock = Stopwatch.StartNew();
        Func<bool> parentAlive = delegate {
            try { using (var p = Process.GetProcessById(parentId)) return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == parentTicks; }
            catch { return false; }
        };
        var supervisor = new Thread(delegate() {
            while (true) {
                var action = Decide(FreeMiB(), clock.Elapsed.TotalSeconds, parentAlive(), File.Exists(marker), File.Exists(cancel));
                if (action != "allocate" && action != "hold") {
                    Console.WriteLine(action);
                    Environment.Exit(action == "marker" ? 0 : action == "hard_abort" ? 21 : 22);
                }
                Thread.Sleep(25);
            }
        });
        supervisor.IsBackground = true;
        supervisor.Start();
        while (!File.Exists(gate)) Thread.Sleep(25);
        var blocks = new List<byte[]>();
        try {
            while (true) {
                var action = Decide(FreeMiB(), clock.Elapsed.TotalSeconds, parentAlive(), File.Exists(marker), File.Exists(cancel));
                if (action == "allocate") {
                    // 4 MiB per 100 ms, hard cap 4096 MiB. Touch every page; retain all blocks.
                    if (blocks.Count >= 1024) { Console.WriteLine("allocation_cap"); Environment.Exit(23); }
                    var block = new byte[4 * 1024 * 1024];
                    for (int offset = 0; offset < block.Length; offset += 4096) block[offset] = 1;
                    blocks.Add(block);
                }
                else if (action != "hold") {
                    Console.WriteLine(action);
                    Environment.Exit(action == "marker" ? 0 : action == "hard_abort" ? 21 : 22);
                }
                Thread.Sleep(100);
            }
        }
        catch { Console.WriteLine("allocation_failure"); Environment.Exit(24); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, Extended;
    }
    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

// Parent-held, unnamed job: closing the handle (also on parent exit) kills only assigned children.
public sealed class ScmPressureJob : IDisposable
{
    private IntPtr handle;
    public ScmPressureJob()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        var limits = new ExtendedLimits();
        limits.Basic.Flags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; no breakaway.
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits)))) {
            Dispose(); throw new System.ComponentModel.Win32Exception();
        }
    }
    public void Assign(Process child)
    {
        if (!AssignProcessToJobObject(handle, child.Handle)) throw new System.ComponentModel.Win32Exception();
    }
    public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref ExtendedLimits limits, uint size);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
