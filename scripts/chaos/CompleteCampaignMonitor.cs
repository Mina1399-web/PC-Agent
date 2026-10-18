using System;
using System.Runtime.InteropServices;
using System.Threading;
// Independent 250ms read-only safety monitor; no service/process control or writes.
public sealed class CompleteCampaignMonitor : IDisposable {
 private string failure;
 private readonly ManualResetEvent done = new ManualResetEvent(false);
 private readonly Thread worker;
 public CompleteCampaignMonitor() { worker=new Thread(Run);worker.IsBackground=true;worker.Name="DEV campaign read-only safety";worker.Start(); }
 public static string Evaluate(double freeMiB) {
  if(double.IsNaN(freeMiB)||double.IsInfinity(freeMiB)||freeMiB<=512)return "hard_free_memory_floor_512_mib";
  return freeMiB<768?"soft_free_memory_floor_768_mib":null;
 }
 public string FailureCode {get {return Volatile.Read(ref failure);}}
 public void Observe(double freeMiB) {var code=Evaluate(freeMiB);if(code!=null)Interlocked.CompareExchange(ref failure,code,null);}
 void Run() {
  try {
   do {
    var status=new MemoryStatus();status.Length=(uint)Marshal.SizeOf(typeof(MemoryStatus));
    Observe(GlobalMemoryStatusEx(ref status)?status.AvailablePhysical/1048576d:double.NaN);
    ulong available,total,free;
    if(!GetDiskFreeSpaceEx("C:\\",out available,out total,out free))Interlocked.CompareExchange(ref failure,"disk_reading_unavailable",null);
    else if(available<20UL*1024*1024*1024)Interlocked.CompareExchange(ref failure,"disk_free_below_20_gib",null);
   }while(!done.WaitOne(250));
  }catch {Interlocked.CompareExchange(ref failure,"resource_monitor_unavailable",null);}
 }
 public void Dispose() {done.Set();if(!worker.Join(3000))throw new InvalidOperationException("resource_monitor_shutdown_unconfirmed");done.Dispose();}
 [StructLayout(LayoutKind.Sequential)]struct MemoryStatus {public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,Extended;}
 [DllImport("kernel32.dll")]static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool GetDiskFreeSpaceEx(string path,out ulong available,out ulong total,out ulong free);
}
