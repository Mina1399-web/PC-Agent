using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace HostAgent.Infrastructure.Maintenance;

internal sealed record FileProof(string Identity, long Bytes, DateTimeOffset ModifiedAtUtc, string Sha256);

internal static class MaintenanceSecurity
{
    internal static string UserSid { get { using var identity = WindowsIdentity.GetCurrent(); return identity.User?.Value ?? throw new UnauthorizedAccessException("maintenance_identity"); } }
    internal static bool NormalUser { get { using var identity = WindowsIdentity.GetCurrent(); return !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); } }
    internal static bool SafeName(string name)
    {
        if (!Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9._ -]{0,119}\z") || name.EndsWith('.') || name.EndsWith(' ')) return false;
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL") && !Regex.IsMatch(stem, @"\A(?:COM|LPT)[1-9]\z");
    }
    internal static void DemandPrivate(FileSystemSecurity security)
    {
        var user = UserSid;
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || owner.Value != user) throw new UnauthorizedAccessException("maintenance_owner");
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            if (((SecurityIdentifier)rule.IdentityReference).Value is not ("S-1-5-18" or "S-1-5-32-544") && rule.IdentityReference.Value != user)
                throw new UnauthorizedAccessException("maintenance_permissions");
        }
    }
    internal static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException;
}

// Pin every ancestor without delete sharing, then inspect the opened object.
// This avoids checking a path and later following a replaced junction.
internal sealed class PinnedDirectory : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    internal uint Volume { get; private set; }
    internal static PinnedDirectory Open(string path, bool requirePrivate = false)
    {
        var pinned = new PinnedDirectory();
        try
        {
            var paths = new Stack<string>();
            for (var current = new DirectoryInfo(path); current is not null; current = current.Parent) paths.Push(current.FullName);
            foreach (var part in paths)
            {
                var handle = MaintenanceNative.CreateFile(part, 0x20080, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
                pinned._handles.Add(handle);
                var info = MaintenanceNative.Info(handle);
                if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || (info.Attributes & (uint)FileAttributes.Directory) == 0)
                    throw new UnauthorizedAccessException("maintenance_directory");
                pinned.Volume = info.Volume;
            }
            if (requirePrivate) MaintenanceSecurity.DemandPrivate(new DirectoryInfo(path).GetAccessControl());
            return pinned;
        }
        catch { pinned.Dispose(); throw; }
    }
    public void Dispose() { for (var index = _handles.Count - 1; index >= 0; index--) _handles[index].Dispose(); }
}

internal sealed class PinnedFile : IDisposable
{
    private readonly FileStream _stream;
    internal SafeFileHandle Handle => _stream.SafeFileHandle;
    internal MaintenanceNative.FileInfo Info { get; }
    private PinnedFile(FileStream stream, MaintenanceNative.FileInfo info) { _stream = stream; Info = info; }
    internal static PinnedFile Open(string path, bool forMove = false)
    {
        var handle = MaintenanceNative.CreateFile(path, 0x80000000u | (forMove ? 0x10000u : 0u), 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        FileStream? stream = null;
        try
        {
            var info = MaintenanceNative.Info(handle);
            const FileAttributes excluded = FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.System | FileAttributes.Offline | FileAttributes.ReadOnly;
            if ((info.Attributes & (uint)excluded) != 0 || info.Links != 1 || MaintenanceNative.GetFileType(handle) != 1)
                throw new UnauthorizedAccessException("maintenance_file_type");
            stream = new FileStream(handle, FileAccess.Read, 4096, false);
            MaintenanceSecurity.DemandPrivate(stream.GetAccessControl());
            return new PinnedFile(stream, info);
        }
        catch { if (stream is not null) stream.Dispose(); else handle.Dispose(); throw; }
    }
    internal async ValueTask<FileProof> ProofAsync(CancellationToken token)
    {
        _stream.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(_stream, token));
        return new FileProof(Info.Identity, Info.Bytes, Info.ModifiedAtUtc, hash);
    }
    internal byte[] ReadSmall(int maximumBytes)
    {
        if (Info.Bytes is <= 0 || Info.Bytes > maximumBytes) throw new IOException("maintenance_journal_limit");
        var data = new byte[checked((int)Info.Bytes)];
        _stream.Position = 0;
        _stream.ReadExactly(data);
        return data;
    }
    public void Dispose() => _stream.Dispose();
}

internal static class MaintenanceMutationLock
{
    internal static FileStream? TryAcquire(string root)
    {
        var path = Path.Combine(root, "maintenance.lock");
        var handle = MaintenanceNative.CreateFile(path, 0xC0000000u, 0, IntPtr.Zero, 4, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (code is 32 or 33) return null;
            throw new Win32Exception(code);
        }
        try
        {
            var info = MaintenanceNative.Info(handle);
            if ((info.Attributes & ((uint)FileAttributes.Directory | (uint)FileAttributes.ReparsePoint)) != 0
                || info.Links != 1 || MaintenanceNative.GetFileType(handle) != 1) throw new UnauthorizedAccessException("maintenance_lock_type");
            MaintenanceSecurity.DemandPrivate(new FileInfo(path).GetAccessControl());
            return new FileStream(handle, FileAccess.ReadWrite, 1, false);
        }
        catch { handle.Dispose(); throw; }
    }
}

internal static partial class MaintenanceNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTime { internal uint Low, High; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileInfo
    {
        internal uint Attributes;
        internal NativeTime Creation, Access, Write;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        internal readonly long Bytes => checked((long)(((ulong)SizeHigh << 32) | SizeLow));
        internal readonly DateTimeOffset ModifiedAtUtc => new(DateTime.FromFileTimeUtc(unchecked((long)(((ulong)Write.High << 32) | Write.Low))));
        internal readonly string Identity => $"{Volume:X8}:{IndexHigh:X8}{IndexLow:X8}";
    }
    internal static FileInfo Info(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return info;
    }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial SafeFileHandle CreateFile(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    internal static void Delete(SafeFileHandle handle)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, 1);
            if (!SetFileInformationByHandle(handle, 4, buffer, 4))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint GetFileType(SafeFileHandle handle);

    internal static void Rename(SafeFileHandle handle, string destination)
    {
        var path = System.Text.Encoding.Unicode.GetBytes(destination);
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + sizeof(int);
        // Keep the structure's WCHAR[1] terminator storage even though FileNameLength excludes it.
        var size = checked(nameOffset + path.Length + sizeof(char));
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            for (var index = 0; index < size; index++) Marshal.WriteByte(buffer, index, 0);
            Marshal.WriteIntPtr(buffer, rootOffset, IntPtr.Zero);
            Marshal.WriteInt32(buffer, lengthOffset, path.Length);
            Marshal.Copy(path, 0, IntPtr.Add(buffer, nameOffset), path.Length);
            if (!SetFileInformationByHandle(handle, 3, buffer, (uint)size))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, IntPtr info, uint size);
}
