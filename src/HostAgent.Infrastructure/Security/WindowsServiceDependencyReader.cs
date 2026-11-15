using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HostAgent.Infrastructure.Security;

public sealed record WindowsServiceDependencyStatus(string ServiceName, ServiceControllerStatus Status);

public static class WindowsServiceDependencyReader
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryConfig = 0x0001;
    private const int ErrorInsufficientBuffer = 122;
    private const int MaxDependencies = 32;
    private const int MaxConfigBytes = 1024 * 1024;

    public static IReadOnlyList<WindowsServiceDependencyStatus> ReadClosure(string rootServiceName)
    {
        ValidateServiceName(rootServiceName);
        var found = new Dictionary<string, WindowsServiceDependencyStatus>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit(rootServiceName);
        return found.Values.OrderBy(item => item.ServiceName, StringComparer.OrdinalIgnoreCase).ToArray();

        void Visit(string currentName)
        {
            if (!visiting.Add(currentName)) throw new InvalidDataException("service_dependency_cycle");
            try
            {
                foreach (var dependencyName in ReadDirectDependencyNames(currentName))
                {
                    if (visiting.Contains(dependencyName)) throw new InvalidDataException("service_dependency_cycle");
                    if (found.ContainsKey(dependencyName)) continue;
                    if (found.Count >= MaxDependencies) throw new InvalidDataException("service_dependency_limit");
                    using var dependency = new ServiceController(dependencyName, ".");
                    dependency.Refresh();
                    var state = new WindowsServiceDependencyStatus(dependencyName, dependency.Status);
                    found.Add(dependencyName, state);
                    Visit(dependencyName);
                }
            }
            finally { visiting.Remove(currentName); }
        }
    }

    public static string[] ParseDependencyNames(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var names = values.ToArray();
        if (names.Length > MaxDependencies) throw new InvalidDataException("service_dependency_limit");
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("service_dependency_invalid");
            if (name[0] == '+') throw new InvalidDataException("service_dependency_group_unsupported");
            ValidateServiceName(name);
        }
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidDataException("service_dependency_duplicate");
        return names;
    }

    private static string[] ReadDirectDependencyNames(string serviceName)
    {
        using var manager = Native.OpenScManager(null, null, ScManagerConnect);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var service = Native.OpenService(manager, serviceName, ServiceQueryConfig);
        if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        _ = Native.QueryServiceConfig(service, IntPtr.Zero, 0, out var requiredBytes);
        var firstError = Marshal.GetLastWin32Error();
        if (requiredBytes == 0 || requiredBytes > MaxConfigBytes || firstError != ErrorInsufficientBuffer)
            throw new Win32Exception(firstError);

        var buffer = Marshal.AllocHGlobal(checked((int)requiredBytes));
        try
        {
            if (!Native.QueryServiceConfig(service, buffer, requiredBytes, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var config = Marshal.PtrToStructure<QueryServiceConfigData>(buffer);
            if (config.Dependencies == IntPtr.Zero) return [];
            var dependencyOffset = config.Dependencies.ToInt64() - buffer.ToInt64();
            if (dependencyOffset < 0 || dependencyOffset >= requiredBytes)
                throw new InvalidDataException("service_dependency_list_malformed");
            var dependencyChars = checked((int)((requiredBytes - dependencyOffset) / sizeof(char)));
            var raw = ReadMultiString(config.Dependencies, dependencyChars);
            return ParseDependencyNames(raw);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string[] ReadMultiString(IntPtr value, int maxChars)
    {
        var results = new List<string>();
        var current = new StringBuilder();
        for (var index = 0; index < maxChars; index++)
        {
            var currentChar = (char)Marshal.ReadInt16(value, checked(index * sizeof(char)));
            if (currentChar == '\0')
            {
                if (current.Length == 0) return results.ToArray();
                results.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(currentChar);
        }
        throw new InvalidDataException("service_dependency_list_malformed");
    }

    private static void ValidateServiceName(string serviceName)
    {
        if (serviceName is not { Length: > 0 and <= 128 } ||
            !serviceName.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
            throw new InvalidDataException("service_dependency_invalid");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryServiceConfigData
    {
        public uint ServiceType;
        public uint StartType;
        public uint ErrorControl;
        public IntPtr BinaryPathName;
        public IntPtr LoadOrderGroup;
        public uint TagId;
        public IntPtr Dependencies;
        public IntPtr ServiceStartName;
        public IntPtr DisplayName;
    }

    private sealed class ScmSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ScmSafeHandle() : base(true) { }
        protected override bool ReleaseHandle() => Native.CloseServiceHandle(handle);
    }

    private static class Native
    {
        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ScmSafeHandle OpenScManager(string? machineName, string? databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ScmSafeHandle OpenService(ScmSafeHandle manager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryServiceConfig(ScmSafeHandle service, IntPtr config, uint bufferSize, out uint bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseServiceHandle(IntPtr handle);
    }
}
