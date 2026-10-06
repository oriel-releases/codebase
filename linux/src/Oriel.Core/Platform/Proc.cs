using System.Diagnostics;
using System.Text;

namespace Oriel.Core.Platform;

public sealed record ProcResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Runs the small command-line tools Oriel leans on (gsettings, systemctl, xrandr, ...). Never goes through a shell.</summary>
public static class Proc
{
    static readonly Dictionary<string, string?> Found = [];

    /// <summary>Full path of an executable on PATH, or null. Remembered, so repeated checks cost nothing.</summary>
    public static string? Which(string name)
    {
        lock (Found)
        {
            if (Found.TryGetValue(name, out var cached)) return cached;
            string? hit = null;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) { hit = candidate; break; }
            }
            return Found[name] = hit;
        }
    }

    public static bool Has(string name) => Which(name) is not null;

    /// <summary>Runs a tool to completion. Null when the tool isn't installed or couldn't start.</summary>
    public static ProcResult? Run(string tool, IEnumerable<string> args, string? stdin = null, int timeoutMs = 15_000)
    {
        if (Which(tool) is not { } exe) return null;
        try
        {
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info);
            if (p is null) return null;

            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            if (stdin is not null)
            {
                p.StandardInput.Write(stdin);
                p.StandardInput.Close();
            }
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new ProcResult(-1, "", "timed out");
            }
            return new ProcResult(p.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    public static ProcResult? Run(string tool, params string[] args) => Run(tool, (IEnumerable<string>)args);

    /// <summary>Starts a tool and doesn't wait for it (xdg-open, notify-send). Returns false if it couldn't start.</summary>
    public static bool Start(string tool, params string[] args)
    {
        if (Which(tool) is not { } exe) return false;
        try
        {
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) info.ArgumentList.Add(a);
            var p = Process.Start(info);
            if (p is null) return false;
            // Reap the child without keeping anything open.
            _ = Task.Run(() => { try { p.WaitForExit(); } finally { p.Dispose(); } });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
