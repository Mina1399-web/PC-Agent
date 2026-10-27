using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Win32;

public sealed class DevActionTargetPaths
{
    public string RunId { get; private set; }
    public string ServiceName { get; private set; }
    public string Root { get; private set; }
    public string App { get; private set; }
    public string Data { get; private set; }
    public string Executable { get; private set; }

    internal DevActionTargetPaths(string runId, string serviceName, string root, string app, string data, string executable)
    {
        RunId = runId;
        ServiceName = serviceName;
        Root = root;
        App = app;
        Data = data;
        Executable = executable;
    }
}

public static class DevActionTargetScope
{
    public static DevActionTargetPaths Resolve(string runId, string commonApplicationData)
    {
        Guid parsed;
        if (String.IsNullOrEmpty(runId) || runId.Length != 32 ||
            !Guid.TryParseExact(runId, "N", out parsed) || parsed == Guid.Empty ||
            !String.Equals(parsed.ToString("N"), runId, StringComparison.Ordinal))
            throw new ArgumentException("invalid_run_id", "runId");
        if (String.IsNullOrEmpty(commonApplicationData) || !Path.IsPathRooted(commonApplicationData))
            throw new ArgumentException("invalid_common_application_data", "commonApplicationData");

        string basePath = Path.GetFullPath(commonApplicationData).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (basePath.Length < 3 || basePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException("invalid_common_application_data", "commonApplicationData");
        string root = Path.GetFullPath(Path.Combine(basePath, "HostAgent-DEV-Acceptance-" + runId));
        string app = Path.GetFullPath(Path.Combine(root, "app"));
        string data = Path.GetFullPath(Path.Combine(root, "data"));
        string executable = Path.GetFullPath(Path.Combine(app, "HostAgent.DevActionTarget.exe"));
        if (!IsUnder(root, basePath) || !IsUnder(app, root) || !IsUnder(data, root) || !IsUnder(executable, app))
            throw new ArgumentException("scope_path_rejected", "runId");
        return new DevActionTargetPaths(runId, "0HostAgentDevAcceptance_" + runId, root, app, data, executable);
    }

    private static bool IsUnder(string child, string parent)
    {
        string prefix = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DevActionTargetEnvironment
{
    private const string ExpectedComputerName = "AGENTPC";
    private const string ExpectedServicePrefix = "0HostAgentDevAcceptance_";
    private const string ProgramDataPath = @"C:\ProgramData";

    internal static string Check(DevActionTargetPaths paths, bool requireServiceIdentity)
    {
        if (!String.Equals(Environment.MachineName, ExpectedComputerName, StringComparison.OrdinalIgnoreCase))
            return "vm_name_mismatch";
        if (!IsVirtualBoxGuest()) return "virtualbox_guest_unverified";
        if (!NoReparsePath(paths.Root, true) || !NoReparsePath(paths.App, true) ||
            !NoReparsePath(paths.Data, true) || !NoReparsePath(paths.Executable, false) ||
            !File.Exists(paths.Executable) || Directory.Exists(paths.Executable)) return "path_reparse_or_missing";
        string expectedProgramData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        if (!String.Equals(expectedProgramData.TrimEnd('\\'), ProgramDataPath, StringComparison.OrdinalIgnoreCase) ||
            !String.Equals(Path.GetFullPath(paths.Root), Path.GetFullPath(Path.Combine(ProgramDataPath, Path.GetFileName(paths.Root))), StringComparison.OrdinalIgnoreCase))
            return "program_data_scope_mismatch";

        string expectedAccount = @"NT SERVICE\" + paths.ServiceName;
        RegistryData registration;
        string registrationError = ReadAndValidateRegistration(paths, expectedAccount, out registration);
        if (registrationError != null) return registrationError;
        string aclError = ValidateAcls(paths, expectedAccount);
        if (aclError != null) return aclError;

        if (requireServiceIdentity)
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (identity == null || !String.Equals(identity.Name, expectedAccount, StringComparison.OrdinalIgnoreCase))
                    return "service_identity_mismatch";
            }
            string currentImage = Path.GetFullPath(typeof(DevActionTargetEnvironment).Assembly.Location);
            if (!String.Equals(currentImage, paths.Executable, StringComparison.OrdinalIgnoreCase)) return "image_path_mismatch";
        }
        return null;
    }

    internal static RegistryData ReadRegistration(DevActionTargetPaths paths, out string failure)
    {
        string account = @"NT SERVICE\" + paths.ServiceName;
        RegistryData result;
        failure = ReadAndValidateRegistration(paths, account, out result);
        return result;
    }

    private static string ReadAndValidateRegistration(DevActionTargetPaths paths, string expectedAccount, out RegistryData data)
    {
        data = new RegistryData();
        try
        {
            string subkey = @"SYSTEM\CurrentControlSet\Services\" + paths.ServiceName;
            using (RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(subkey, false))
            {
                if (key == null) return "service_registration_missing";
                object image = key.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                object account = key.GetValue("ObjectName", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                object type = key.GetValue("Type", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                object start = key.GetValue("Start", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (!(image is string) || !(account is string) || !(type is int) || !(start is int)) return "service_registration_invalid";
                string expectedImage = "\"" + paths.Executable + "\" run " + paths.RunId;
                if (!String.Equals((string)image, expectedImage, StringComparison.OrdinalIgnoreCase)) return "service_image_path_mismatch";
                if (!String.Equals((string)account, expectedAccount, StringComparison.OrdinalIgnoreCase)) return "service_account_mismatch";
                if ((int)type != 16) return "service_type_mismatch";
                if ((int)start != 2 && (int)start != 4) return "service_start_mode_mismatch";
                data.ImagePath = (string)image;
                data.Account = (string)account;
                data.Type = (int)type;
                data.Start = (int)start;
                return null;
            }
        }
        catch { return "service_registration_unreadable"; }
    }

    private static string ValidateAcls(DevActionTargetPaths paths, string expectedAccount)
    {
        SecurityIdentifier serviceSid;
        try { serviceSid = (SecurityIdentifier)new NTAccount(expectedAccount).Translate(typeof(SecurityIdentifier)); }
        catch { return "service_sid_unresolved"; }
        string[] appPaths = new string[] { paths.Root, paths.App, paths.Executable };
        for (int i = 0; i < appPaths.Length; i++)
        {
            string error = ValidateServiceReadOnlyAcl(appPaths[i], serviceSid, i == 2);
            if (error != null) return error;
        }
        return ValidateDataFullControlAcl(paths.Data, serviceSid);
    }

    private static string ValidateServiceReadOnlyAcl(string path, SecurityIdentifier serviceSid, bool file)
    {
        try
        {
            AuthorizationRuleCollection rules;
            if (file) rules = File.GetAccessControl(path, AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier));
            else rules = Directory.GetAccessControl(path, AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier));
            bool readExecute = false;
            const FileSystemRights writeMask = FileSystemRights.WriteData | FileSystemRights.AppendData |
                FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete |
                FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership |
                FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles;
            foreach (FileSystemAccessRule rule in rules)
            {
                SecurityIdentifier sid = rule.IdentityReference as SecurityIdentifier;
                if (sid == null || !sid.Equals(serviceSid)) continue;
                if (rule.AccessControlType == AccessControlType.Deny && (rule.FileSystemRights & FileSystemRights.ReadAndExecute) != 0)
                    return "app_acl_read_denied";
                if (rule.AccessControlType != AccessControlType.Allow) continue;
                if ((rule.FileSystemRights & writeMask) != 0) return "app_acl_service_write_allowed";
                if ((rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute)
                    readExecute = true;
            }
            return readExecute ? null : "app_acl_service_read_execute_missing";
        }
        catch { return "app_acl_unreadable"; }
    }

    private static string ValidateDataFullControlAcl(string path, SecurityIdentifier serviceSid)
    {
        try
        {
            AuthorizationRuleCollection rules = Directory.GetAccessControl(path, AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier));
            bool fullControl = false;
            foreach (FileSystemAccessRule rule in rules)
            {
                SecurityIdentifier sid = rule.IdentityReference as SecurityIdentifier;
                if (sid != null && sid.Equals(serviceSid) && rule.AccessControlType == AccessControlType.Allow &&
                    (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
                    fullControl = true;
                if (sid != null && sid.Equals(serviceSid) && rule.AccessControlType == AccessControlType.Deny && rule.FileSystemRights != 0)
                    return "data_acl_service_access_denied";
            }
            return fullControl ? null : "data_acl_service_full_control_missing";
        }
        catch { return "data_acl_unreadable"; }
    }

    private static bool IsVirtualBoxGuest()
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS", false))
            {
                if (key == null) return false;
                string manufacturer = Convert.ToString(key.GetValue("SystemManufacturer", ""), CultureInfo.InvariantCulture);
                string product = Convert.ToString(key.GetValue("SystemProductName", ""), CultureInfo.InvariantCulture);
                if (manufacturer.IndexOf("innotek", StringComparison.OrdinalIgnoreCase) < 0 &&
                    manufacturer.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) < 0 &&
                    product.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) < 0) return false;
                if (product.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            using (ServiceController guest = new ServiceController("VBoxService"))
                return guest.Status == ServiceControllerStatus.Running;
        }
        catch { return false; }
    }

    private static bool NoReparsePath(string path, bool requireLeaf)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string current = Path.GetPathRoot(full);
            string relative = full.Substring(current.Length);
            string[] parts = relative.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                current = Path.Combine(current, parts[i]);
                bool last = i == parts.Length - 1;
                if (!Directory.Exists(current) && !(last && File.Exists(current))) return false;
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (last && requireLeaf && (attributes & FileAttributes.Directory) == 0) return false;
            }
            return true;
        }
        catch { return false; }
    }

    internal static bool NoReparseDataPath(string path)
    {
        return NoReparsePath(path, true);
    }

    internal sealed class RegistryData
    {
        internal string ImagePath;
        internal string Account;
        internal int Type;
        internal int Start;
    }
}

internal static class DevActionTargetJson
{
    internal static string Failure(string code)
    {
        return "{\"scope\":\"dev_action_target\",\"valid\":false,\"failureCode\":\"" + Escape(code) + "\"}";
    }

    internal static string Success(DevActionTargetPaths paths, DevActionTargetEnvironment.RegistryData registration)
    {
        return "{\"scope\":\"dev_action_target\",\"valid\":true,\"runId\":\"" + Escape(paths.RunId) +
            "\",\"serviceName\":\"" + Escape(paths.ServiceName) + "\",\"root\":\"" + Escape(paths.Root) +
            "\",\"app\":\"" + Escape(paths.App) + "\",\"data\":\"" + Escape(paths.Data) +
            "\",\"executable\":\"" + Escape(paths.Executable) + "\",\"vm\":\"AGENTPC\",\"virtualBoxGuestAdditions\":\"Running\",\"serviceAccount\":\"" +
            Escape(registration.Account) + "\",\"serviceType\":" + registration.Type.ToString(CultureInfo.InvariantCulture) +
            ",\"serviceStart\":" + registration.Start.ToString(CultureInfo.InvariantCulture) + "}";
    }

    private static string Escape(string value)
    {
        if (value == null) return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}

internal sealed class DevActionTargetService : ServiceBase
{
    private const double HardMemoryFloorMiB = 512.0;
    private const double WarningMemoryFloorMiB = 768.0;
    private const long HardDiskFloorBytes = 10L * 1024L * 1024L * 1024L;
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(20);
    private readonly DevActionTargetPaths _paths;
    private readonly string _lifecycleId;
    private readonly ManualResetEvent _stopped = new ManualResetEvent(false);
    private readonly Stopwatch _lifetime = new Stopwatch();
    private Thread _watchdog;
    private int _stopping;
    private int _warningWritten;

    internal DevActionTargetService(DevActionTargetPaths paths)
    {
        _paths = paths;
        _lifecycleId = Guid.NewGuid().ToString("N");
        ServiceName = paths.ServiceName;
        CanStop = true;
        CanPauseAndContinue = false;
        CanShutdown = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        string failure = DevActionTargetEnvironment.Check(_paths, true);
        if (failure != null) Environment.FailFast("dev_action_target_startup_guard:" + failure);
        if (!String.Equals(ServiceName, _paths.ServiceName, StringComparison.Ordinal))
            Environment.FailFast("dev_action_target_service_arguments_invalid");
        try { WriteMarker("started", null); }
        catch { Environment.FailFast("dev_action_target_start_marker_failed"); }
        double initialFreeMiB;
        string initialFailure = CheckResources(out initialFreeMiB);
        if (initialFailure != null) Environment.FailFast("dev_action_target_initial_guard:" + initialFailure);
        if (initialFreeMiB < WarningMemoryFloorMiB)
        {
            try
            {
                Interlocked.Exchange(ref _warningWritten, 1);
                WriteMarker("memory-warning", "free_memory_below_768_mib");
            }
            catch { Environment.FailFast("dev_action_target_warning_marker_failed"); }
        }
        _lifetime.Start();
        _watchdog = new Thread(WatchdogLoop);
        _watchdog.IsBackground = true;
        _watchdog.Name = "DevActionTargetGuard";
        _watchdog.Start();
    }

    protected override void OnStop()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        try { WriteMarker("stopped", null); }
        catch { Environment.FailFast("dev_action_target_stop_marker_failed"); }
        _lifetime.Stop();
        _stopped.Set();
        if (_watchdog != null && Thread.CurrentThread != _watchdog && !_watchdog.Join(TimeSpan.FromSeconds(5)))
            Environment.FailFast("dev_action_target_watchdog_shutdown_timeout");
    }

    protected override void OnShutdown()
    {
        OnStop();
        base.OnShutdown();
    }

    private void WatchdogLoop()
    {
        while (Volatile.Read(ref _stopping) == 0)
        {
            double freeMiB;
            string resourceFailure = CheckResources(out freeMiB);
            if (freeMiB > HardMemoryFloorMiB && freeMiB < WarningMemoryFloorMiB && Interlocked.Exchange(ref _warningWritten, 1) == 0)
            {
                try { WriteMarker("memory-warning", "free_memory_below_768_mib"); }
                catch { Environment.FailFast("dev_action_target_warning_marker_failed"); }
            }
            if (resourceFailure != null) Environment.FailFast("dev_action_target_guard:" + resourceFailure);
            if (_lifetime.Elapsed >= MaximumLifetime)
            {
                try
                {
                    using (ServiceController self = new ServiceController(_paths.ServiceName)) self.Stop();
                }
                catch { Environment.FailFast("dev_action_target_lifetime_stop_failed"); }
                if (!_stopped.WaitOne(ShutdownGrace)) Environment.FailFast("dev_action_target_shutdown_timeout");
                return;
            }
            Thread.Sleep(250);
        }
    }

    private static string CheckResources(out double freeMiB)
    {
        MemoryStatus status = new MemoryStatus();
        status.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
        if (!GlobalMemoryStatusEx(ref status))
        {
            freeMiB = Double.NaN;
            return "memory_probe_failed";
        }
        freeMiB = status.AvailablePhysical / 1048576.0;
        if (Double.IsNaN(freeMiB) || Double.IsInfinity(freeMiB) || freeMiB <= HardMemoryFloorMiB)
            return "hard_memory_floor_512_mib";
        try
        {
            long available = new DriveInfo("C:\\").AvailableFreeSpace;
            if (available < HardDiskFloorBytes) return "disk_floor_10_gib";
        }
        catch { return "disk_probe_failed"; }
        return null;
    }

    private void WriteMarker(string phase, string code)
    {
        string path = Path.Combine(_paths.Data, phase + "-" + _lifecycleId + ".json");
        if (!DevActionTargetEnvironment.NoReparseDataPath(_paths.Data)) throw new IOException();
        string text = "{\"schema\":1,\"scope\":\"dev_action_target\",\"runId\":\"" + _paths.RunId +
            "\",\"serviceName\":\"" + _paths.ServiceName + "\",\"lifecycleId\":\"" + _lifecycleId + "\",\"phase\":\"" + phase + "\",\"utc\":\"" +
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\",\"processId\":" +
            Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) +
            (code == null ? "" : ",\"code\":\"" + code + "\"") + "}";
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        if (bytes.Length == 0 || bytes.Length > 4096) throw new InvalidDataException();
        using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}

public static class DevActionTargetDataGuard
{
    public static bool NoReparsePathForData(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) return false;
            FileAttributes attributes = File.GetAttributes(full);
            return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch { return false; }
    }
}

public static class DevActionTargetProgram
{
    public static int Main(string[] args)
    {
        if (args == null || args.Length != 2 || (args[0] != "run" && args[0] != "validate"))
        {
            Console.WriteLine(DevActionTargetJson.Failure("invalid_arguments"));
            return 2;
        }
        DevActionTargetPaths paths;
        try { paths = DevActionTargetScope.Resolve(args[1], Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)); }
        catch (ArgumentException)
        {
            Console.WriteLine(DevActionTargetJson.Failure("invalid_arguments"));
            return 2;
        }
        if (args[0] == "validate")
        {
            string failure = DevActionTargetEnvironment.Check(paths, false);
            if (failure != null)
            {
                Console.WriteLine(DevActionTargetJson.Failure(failure));
                return 1;
            }
            failure = CheckPreflightResources();
            if (failure != null)
            {
                Console.WriteLine(DevActionTargetJson.Failure(failure));
                return 1;
            }
            string registrationError;
            DevActionTargetEnvironment.RegistryData registration = DevActionTargetEnvironment.ReadRegistration(paths, out registrationError);
            if (registrationError != null)
            {
                Console.WriteLine(DevActionTargetJson.Failure(registrationError));
                return 1;
            }
            Console.WriteLine(DevActionTargetJson.Success(paths, registration));
            return 0;
        }

        try
        {
            if (!String.Equals(args[0], "run", StringComparison.Ordinal) ||
                !String.Equals(args[1], paths.RunId, StringComparison.Ordinal))
            {
                Console.WriteLine(DevActionTargetJson.Failure("invalid_arguments"));
                return 2;
            }
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (identity == null || !String.Equals(identity.Name, @"NT SERVICE\" + paths.ServiceName, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(DevActionTargetJson.Failure("service_identity_mismatch"));
                    return 1;
                }
            }
            ServiceBase.Run(new DevActionTargetService(paths));
            return 0;
        }
        catch
        {
            Console.WriteLine(DevActionTargetJson.Failure("service_start_failed"));
            return 1;
        }
    }

    internal static string CheckPreflightResources()
    {
        MemoryStatus status = new MemoryStatus();
        status.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
        if (!GlobalMemoryStatusEx(ref status)) return "memory_probe_failed";
        double freeMiB = status.AvailablePhysical / 1048576.0;
        if (Double.IsNaN(freeMiB) || Double.IsInfinity(freeMiB) || freeMiB <= 768.0) return "initial_memory_not_above_768_mib";
        try
        {
            if (new DriveInfo("C:\\").AvailableFreeSpace < 10L * 1024L * 1024L * 1024L)
                return "disk_floor_10_gib";
        }
        catch { return "disk_probe_failed"; }
        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
