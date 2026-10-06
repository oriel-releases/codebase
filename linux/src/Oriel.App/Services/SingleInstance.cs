using System.Net.Sockets;
using System.Text;

namespace Oriel.UI.Services;

/// <summary>
/// One Oriel per user. The first process listens on a Unix socket; a second launch (menu entry, the website's
/// "Open Oriel" link, a shortcut running <c>oriel --change-now</c>) sends its request there and exits.
/// Costs nothing while idle: the listener just waits in <c>accept</c>.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string Show = "show";
    public const string Change = "change";
    public const string Refresh = "refresh";
    public const string LinkPrefix = "link ";

    const int MaxMessage = 16 * 1024; // a Google ID token in a sign-in link is a couple of KB

    readonly Socket _listener;
    readonly string _path;
    readonly CancellationTokenSource _cts = new();

    SingleInstance(Socket listener, string path)
    {
        _listener = listener;
        _path = path;
    }

    /// <summary>Raised on a thread-pool thread for every message another launch sent.</summary>
    public event Action<string>? MessageReceived;

    static string SocketPath
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Path.GetTempPath();
            return Path.Combine(dir, $"oriel-{Environment.UserName}.sock");
        }
    }

    /// <summary>Hands a message to the running Oriel. False when none is running.</summary>
    public static bool TrySend(string message)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            {
                SendTimeout = 2000,
            };
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            socket.Send(Encoding.UTF8.GetBytes(message.Replace('\n', ' ') + "\n"));
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Becomes the running instance, or returns null if another one got there first.</summary>
    public static SingleInstance? TryListen()
    {
        var path = SocketPath;
        try
        {
            // A socket file with nobody behind it (crash, reboot of a long-lived /tmp) is stale; its owner would have answered.
            if (File.Exists(path)) File.Delete(path);
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(path));
            socket.Listen(8);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return new SingleInstance(socket, path);
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void StartListening() => _ = Task.Run(AcceptLoopAsync);

    async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptAsync(_cts.Token);
                _ = Task.Run(() => HandleAsync(client));
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
        }
    }

    async Task HandleAsync(Socket client)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var buffer = new byte[MaxMessage];
                var length = 0;
                while (length < buffer.Length)
                {
                    var read = await client.ReceiveAsync(buffer.AsMemory(length), timeout.Token);
                    if (read == 0) break;
                    length += read;
                    if (Array.IndexOf(buffer, (byte)'\n', 0, length) >= 0) break;
                }
                var text = Encoding.UTF8.GetString(buffer, 0, length).Trim();
                if (text.Length > 0) MessageReceived?.Invoke(text);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // A half-open client: nothing to do.
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Dispose();
        try { File.Delete(_path); } catch (IOException) { }
        _cts.Dispose();
    }
}
