using System.Security.Cryptography;
using System.Text.Json;

namespace Oriel.Core.Auth;

public interface IAccountStore
{
    Account? Load();
    void Save(Account account);
    void Clear();
}

/// <summary>Keeps the account (and its refresh token) in <c>auth.bin</c>, encrypted for this Windows user (DPAPI).</summary>
public sealed class DpapiAccountStore(AppPaths paths) : IAccountStore
{
    public Account? Load()
    {
        try
        {
            if (!File.Exists(paths.AccountFile)) return null;
            var json = ProtectedData.Unprotect(File.ReadAllBytes(paths.AccountFile), null, DataProtectionScope.CurrentUser);
            var account = JsonSerializer.Deserialize<Account>(json);
            return account is { RefreshToken.Length: > 0, Uid.Length: > 0 } ? account : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            // Unreadable (copied from another user or PC, or corrupt): sign in again.
            return null;
        }
    }

    public void Save(Account account)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.AccountFile)!);
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(account), null, DataProtectionScope.CurrentUser);
        var tmp = paths.AccountFile + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, paths.AccountFile, overwrite: true);
    }

    public void Clear()
    {
        try { File.Delete(paths.AccountFile); } catch (IOException) { }
    }
}
