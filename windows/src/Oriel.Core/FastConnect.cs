using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Oriel.Core;

/// <summary>
/// Connects like a browser ("Happy Eyeballs", RFC 8305): every address of the host, IPv6 and IPv4 taking turns, each
/// started a moment after the last, and the first to connect wins. .NET on its own tries one address after another and
/// waits for each to time out, so on a network where IPv6 is broken (it looks fine, then hangs) every request to an
/// IPv6-capable host stalled for many seconds: Firestore writes gave up, so the extension and /me never saw a new
/// wallpaper, photo downloads timed out and signing out took ages. Browsers race, so they never noticed.
/// </summary>
public static class FastConnect
{
    /// <summary>How long one attempt gets before the next one starts alongside it.</summary>
    static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(250);

    /// <summary>The shared HTTP handler: connections race, and are renewed now and then so a network change is picked up.</summary>
    public static SocketsHttpHandler Handler() => new()
    {
        ConnectCallback = ConnectAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
    };

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        var ordered = Interleave(addresses);
        if (ordered.Count == 0) throw new SocketException((int)SocketError.HostNotFound);

        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var running = new List<Task<Socket>>();
        Exception? lastError = null;
        var next = 0;
        while (true)
        {
            if (next < ordered.Count) running.Add(TryConnectAsync(ordered[next++], port, race.Token));
            if (running.Count == 0) throw lastError ?? new SocketException((int)SocketError.HostUnreachable);

            // Wait for an attempt to finish, or (while addresses are left) for the moment to start the next one.
            var stagger = next < ordered.Count ? Task.Delay(Stagger, ct) : Task.Delay(Timeout.Infinite, ct);
            var finished = await Task.WhenAny(running.Append<Task>(stagger));
            if (finished == stagger)
            {
                ct.ThrowIfCancellationRequested();
                continue;
            }

            var attempt = (Task<Socket>)finished;
            running.Remove(attempt);
            if (attempt.IsCompletedSuccessfully)
            {
                race.Cancel();
                // Attempts still under way give up; one that connects anyway is closed.
                foreach (var other in running)
                    _ = other.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); }, TaskScheduler.Default);
                return new NetworkStream(attempt.Result, ownsSocket: true);
            }
            lastError = attempt.Exception?.InnerException ?? lastError;
            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>IPv6 first, then IPv4, then taking turns, as RFC 8305 recommends.</summary>
    internal static List<IPAddress> Interleave(IEnumerable<IPAddress> addresses)
    {
        var v6 = new Queue<IPAddress>(addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6));
        var v4 = new Queue<IPAddress>(addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork));
        var result = new List<IPAddress>(v6.Count + v4.Count);
        while (v6.Count > 0 || v4.Count > 0)
        {
            if (v6.Count > 0) result.Add(v6.Dequeue());
            if (v4.Count > 0) result.Add(v4.Dequeue());
        }
        return result;
    }

    static async Task<Socket> TryConnectAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(address, port, ct);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
