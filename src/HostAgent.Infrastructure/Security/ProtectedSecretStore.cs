using HostAgent.Core.Contracts;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
namespace HostAgent.Infrastructure.Security;
public sealed class ProtectedSecretStore(string directory, SecurityIdentifier identity) : ISecretStore
{
    private string FilePath(SecretName name)
    {
        if (!Enum.IsDefined(name)) throw new ArgumentOutOfRangeException(nameof(name));
        using var current = WindowsIdentity.GetCurrent();
        if (current.User is null || !identity.Equals(current.User)) throw new UnauthorizedAccessException("secret_identity_mismatch");
        return Path.Combine(HostAgentDataDirectory.EnsureSecure(directory, identity), name + ".dpapi");
    }
    public async ValueTask WriteAsync(SecretName name, string value, CancellationToken cancellationToken)
    {
        var path = FilePath(name);
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 4096)
            throw new ArgumentException("secret_invalid", nameof(value));
        HelperProtectedFiles.ValidateExisting(path, identity);
        var plain = Encoding.UTF8.GetBytes(value);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = CurrentUserDpapi.Transform(plain, name, protect: true);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(encrypted, cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    public async ValueTask<string?> ReadAsync(SecretName name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var path = FilePath(name);
            HelperProtectedFiles.ValidateExisting(path, identity);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > 16_384) return null;
            var encrypted = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(encrypted, cancellationToken);
            var plain = CurrentUserDpapi.Transform(encrypted, name, protect: false);
            try { return plain.Length is > 0 and <= 4096 ? new UTF8Encoding(false, true).GetString(plain) : null; }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or DecoderFallbackException)
        { return null; }
    }
}

internal static partial class CurrentUserDpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [LibraryImport("crypt32.dll", SetLastError = true)]
    private static partial int CryptProtectData(ref Blob input, IntPtr description, ref Blob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [LibraryImport("crypt32.dll", SetLastError = true)]
    private static partial int CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
    internal static unsafe byte[] Transform(byte[] input, SecretName name, bool protect)
    {
        var entropyBytes = Encoding.UTF8.GetBytes("HostAgent.V1.CurrentUser." + name);
        fixed (byte* data = input)
        fixed (byte* entropyData = entropyBytes)
        {
            var blob = new Blob { Length = input.Length, Data = (IntPtr)data };
            var entropy = new Blob { Length = entropyBytes.Length, Data = (IntPtr)entropyData };
            Blob result;
            // UI forbidden; no CRYPTPROTECT_LOCAL_MACHINE flag. User identity/profile owns the key.
            var ok = protect ? CryptProtectData(ref blob, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out result) :
                CryptUnprotectData(ref blob, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out result);
            if (ok == 0) throw new CryptographicException("secret_protection_failed");
            try
            {
                var output = new byte[result.Length];
                Marshal.Copy(result.Data, output, 0, output.Length);
                return output;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(new Span<byte>((void*)result.Data, result.Length));
                _ = LocalFree(result.Data);
            }
        }
    }
}
