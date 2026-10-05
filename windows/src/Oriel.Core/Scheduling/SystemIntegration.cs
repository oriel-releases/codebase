using System.Diagnostics;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Oriel.Core.Scheduling;

public interface IStartupRegistration
{
    void Set(bool enabled, string exePath);
}

/// <summary>"Start with Windows" for an unpackaged app: HKCU Run key. Starts minimized to the tray.</summary>
public sealed class StartupRegistration : IStartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Oriel";

    public void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{exePath}\" --background");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

public interface IScheduledTasks
{
    /// <summary>
    /// Daily run at <paramref name="time"/> (repeating every <paramref name="repeatEvery"/> through the day
    /// for a slideshow), plus one at sign-in when <paramref name="atSignIn"/>.
    /// </summary>
    bool Register(string exePath, TimeOnly time, bool atSignIn, TimeSpan? repeatEvery = null);

    /// <summary>One-shot run after a failed change (offline, rate limited). Replaces any earlier retry.</summary>
    bool ScheduleRetry(string exePath, DateTimeOffset at);

    /// <summary>Removes every Oriel task.</summary>
    void Unregister();
}

/// <summary>
/// Task Scheduler fallback for when the tray app isn't running: "Oriel.exe --apply-today" at the
/// daily time (and at sign-in if asked); the run takes a few seconds and exits. A missed run
/// (PC off or asleep) starts as soon as possible afterwards. No admin needed: per-user tasks.
/// </summary>
public sealed class TaskSchedulerRegistration(ILogger<TaskSchedulerRegistration> log) : IScheduledTasks
{
    public const string DailyTask = @"Oriel\ApplyToday";
    public const string RetryTask = @"Oriel\Retry";

    public bool Register(string exePath, TimeOnly time, bool atSignIn, TimeSpan? repeatEvery = null)
    {
        var start = DateTime.Today.Add(time.ToTimeSpan()).ToString("yyyy-MM-ddTHH:mm:ss");
        var repetition = repeatEvery is { } every
            ? $"<Repetition><Interval>PT{(int)every.TotalMinutes}M</Interval><Duration>P1D</Duration></Repetition>"
            : "";
        var trigger = $"""
            <CalendarTrigger>
              {repetition}
              <StartBoundary>{start}</StartBoundary>
              <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
            </CalendarTrigger>
            """;
        if (atSignIn)
            // The delay keeps Oriel out of the way while Windows is busy signing in.
            trigger += $"""
                <LogonTrigger>
                  <UserId>{SecurityElement.Escape(Environment.UserDomainName + "\\" + Environment.UserName)}</UserId>
                  <Delay>PT1M</Delay>
                </LogonTrigger>
                """;
        return RegisterTask(DailyTask, "Oriel: daily wallpaper change.", trigger, exePath);
    }

    public bool ScheduleRetry(string exePath, DateTimeOffset at)
    {
        var trigger = $"""
            <TimeTrigger>
              <StartBoundary>{at.LocalDateTime:yyyy-MM-ddTHH:mm:ss}</StartBoundary>
            </TimeTrigger>
            """;
        return RegisterTask(RetryTask, "Oriel: retry a wallpaper change that failed.", trigger, exePath);
    }

    public void Unregister()
    {
        RunSchtasks($"/Delete /TN \"{DailyTask}\" /F", quietIfMissing: true);
        RunSchtasks($"/Delete /TN \"{RetryTask}\" /F", quietIfMissing: true);
    }

    bool RegisterTask(string name, string description, string triggers, string exePath)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{description}</Description></RegistrationInfo>
              <Triggers>
            {triggers}
              </Triggers>
              <Principals><Principal id="Author"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
                <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exePath)}</Command><Arguments>--apply-today</Arguments></Exec>
              </Actions>
            </Task>
            """;

        var file = Path.Combine(Path.GetTempPath(), $"oriel-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{name}\" /XML \"{file}\" /F");
        }
        finally
        {
            File.Delete(file);
        }
    }

    bool RunSchtasks(string args, bool quietIfMissing = false)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            })!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(15_000);
            if (p.ExitCode != 0 && !quietIfMissing)
                log.LogWarning("schtasks {Args} failed: {Error}", args.Split(' ')[0], err.Trim());
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "schtasks failed");
            return false;
        }
    }
}
