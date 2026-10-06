using Oriel.Core.Settings;

namespace Oriel.Core.Scheduling;

/// <summary>
/// Keeps "Start at login" and the systemd timer fallback in line with the settings.
/// Only touches the autostart entry / systemd timer when a relevant setting actually changes.
/// </summary>
public sealed class ScheduleSync(ISettingsStore store, IStartupRegistration startup, IScheduledTasks tasks)
{
    readonly Lock _gate = new();
    (TimeOnly Time, bool AtLogin, bool Paused, bool StartAtLogin, int Interval)? _synced;

    /// <param name="force">Re-register even if nothing changed, e.g. so both point at the exe that's running now.</param>
    public void Sync(string exePath, bool force = false)
    {
        lock (_gate)
        {
            var s = store.Load();
            var wanted = (s.GetDailyTime(), s.ApplyAtLogin, s.Paused, s.StartAtLogin, s.IntervalMinutes);
            if (!force && _synced == wanted) return;

            startup.Set(s.StartAtLogin, exePath);

            // Paused: no task at all, so nothing runs. When the app starts at login it handles
            // sign-in itself, so the task only needs a sign-in trigger when it doesn't.
            if (!s.Paused)
                tasks.Register(exePath, wanted.Item1, atSignIn: s.ApplyAtLogin && !s.StartAtLogin, s.Interval);
            else
                tasks.Unregister();
            _synced = wanted;
        }
    }
}
