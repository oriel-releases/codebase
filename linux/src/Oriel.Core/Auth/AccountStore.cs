using System.Text;
using System.Text.Json;
using Oriel.Core.Platform;

namespace Oriel.Core.Auth;

public interface IAccountStore
{
    Account? Load();
    void Save(Account account);
    void Clear();
}

/// <summary>
/// Keeps the account (and its refresh token) in the desktop's keyring through <c>secret-tool</c> (libsecret:
/// GNOME Keyring, KWallet, ...). Without a keyring it falls back to <c>auth.json</c>, readable by this user only.
/// </summary>
public sealed class SecretAccountStore(AppPaths paths, bool useKeyring = true) : IAccountStore
{
    const string Label = "Oriel account";

    public Account? Load()
    {
        var json = (useKeyring ? FromKeyring() : null) ?? FromFile();
        if (json is null) return null;
        try
        {
            var account = JsonSerializer.Deserialize<Account>(json);
            return account is { RefreshToken.Length: > 0, Uid.Length: > 0 } ? account : null;
        }
        catch (JsonException)
        {
            return null; // corrupt: sign in again
        }
    }

    public void Save(Account account)
    {
        var json = JsonSerializer.Serialize(account);
        if (useKeyring && ToKeyring(json))
        {
            DeleteFile(); // never leave a plain copy next to the safe one
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(paths.AccountFile)!);
        var tmp = paths.AccountFile + ".tmp";
        // Created private from the start, so the token is never readable by others, not even for a moment.
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) // Windows only ever runs the tests
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(tmp, options))
            stream.Write(Encoding.UTF8.GetBytes(json));
        File.Move(tmp, paths.AccountFile, overwrite: true);
    }

    public void Clear()
    {
        if (useKeyring) Proc.Run("secret-tool", "clear", "application", "oriel");
        DeleteFile();
    }

    static string? FromKeyring()
    {
        var r = Proc.Run("secret-tool", "lookup", "application", "oriel");
        return r is { Ok: true, Output.Length: > 0 } ? r.Output : null;
    }

    static bool ToKeyring(string json) =>
        Proc.Run("secret-tool", ["store", $"--label={Label}", "application", "oriel"], stdin: json) is { Ok: true };

    string? FromFile()
    {
        try { return File.Exists(paths.AccountFile) ? File.ReadAllText(paths.AccountFile) : null; }
        catch (IOException) { return null; }
    }

    void DeleteFile()
    {
        try { File.Delete(paths.AccountFile); } catch (IOException) { }
    }
}
