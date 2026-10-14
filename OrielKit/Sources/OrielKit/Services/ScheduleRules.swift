import Foundation

/// When the scheduled change happens. Every device of an account changes at the same moments: the daily time in the
/// shared time zone, then every interval from there, starting over at each day's daily time (same rules as Windows).
public enum ScheduleRules {
    /// Longest sleep between checks: catches sleep/wake and clock changes without system hooks.
    public static let maxWait: TimeInterval = 10 * 60
    /// A timer firing a moment before the tick still counts as that tick.
    static let slack: TimeInterval = 30

    static func calendar(_ s: OrielSettings) -> Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = s.zone
        return c
    }

    /// The daily time on the day `date` falls on (the hour after, when a clock change skipped it).
    static func anchor(onDayOf date: Date, _ s: OrielSettings, _ cal: Calendar) -> Date {
        let (h, m) = s.dailyHourMinute
        return cal.date(bySettingHour: h, minute: m, second: 0, of: date, matchingPolicy: .nextTime,
                        repeatedTimePolicy: .first, direction: .forward) ?? date
    }

    static func day(_ date: Date, adding days: Int, _ cal: Calendar) -> Date {
        cal.date(byAdding: .day, value: days, to: cal.startOfDay(for: date))!
    }

    /// The latest tick at or before `now`.
    public static func previousTick(_ s: OrielSettings, _ now: Date) -> Date {
        let cal = calendar(s)
        var anchor = anchor(onDayOf: now, s, cal)
        if anchor > now { anchor = Self.anchor(onDayOf: day(now, adding: -1, cal), s, cal) }
        guard let step = s.interval else { return anchor }
        let steps = (now.timeIntervalSince(anchor) / step).rounded(.down)
        return anchor.addingTimeInterval(steps * step)
    }

    /// The first tick after `now`.
    public static func nextTick(_ s: OrielSettings, _ now: Date) -> Date {
        let cal = calendar(s)
        let prev = previousTick(s, now)
        let nextDayAnchor = anchor(onDayOf: day(prev, adding: 1, cal), s, cal)
        guard let step = s.interval else { return nextDayAnchor }
        let sameDay = anchor(onDayOf: prev, s, cal)
        let nextAnchor = sameDay > prev ? sameDay : nextDayAnchor
        return min(prev.addingTimeInterval(step), nextAnchor)
    }

    /// The tick a change made now belongs to.
    public static func dueTick(_ s: OrielSettings, _ now: Date) -> Date { previousTick(s, now.addingTimeInterval(slack)) }

    static func baseline(_ state: OrielState) -> Date? {
        if let applied = state.lastAppliedAt, state.scheduleStartedAt.map({ applied >= $0 }) ?? true { return applied }
        return state.scheduleStartedAt
    }

    /// True when the current tick's change was already made (or the schedule started after it).
    public static func isFresh(_ s: OrielSettings, _ state: OrielState, _ now: Date) -> Bool {
        guard let done = baseline(state) else { return false }
        return done >= dueTick(s, now)
    }

    /// Should the scheduled change happen now? Yes once a tick has passed without one (also one missed while off).
    public static func isDue(_ s: OrielSettings, _ state: OrielState, _ now: Date, isStartupCheck: Bool) -> Bool {
        if s.paused || isFresh(s, state, now) { return false }
        if let retry = state.nextRetryAt, now < retry { return false }
        // Missed while the Mac was off: caught up at login only when "change at login" is on.
        if isStartupCheck && !s.applyAtLogin { return false }
        return true
    }

    /// How long the scheduler can sleep before the next check (1 s ... maxWait).
    public static func nextCheckIn(_ s: OrielSettings, _ state: OrielState, _ now: Date) -> TimeInterval {
        if s.paused { return maxWait }
        var due = isFresh(s, state, now) ? nextTick(s, now) : now
        if let retry = state.nextRetryAt, retry > due { due = retry }
        return min(max(due.timeIntervalSince(now), 1), maxWait)
    }
}
