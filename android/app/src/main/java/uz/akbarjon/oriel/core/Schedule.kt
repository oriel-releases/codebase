package uz.akbarjon.oriel.core

import java.time.Duration
import java.time.Instant
import java.time.LocalTime
import java.time.ZonedDateTime
import uz.akbarjon.oriel.data.Settings

/**
 * When changes happen. Every tick is anchored at the daily time in the stored time zone (e.g. 09:00 Asia/Tashkent),
 * so every device of the account computes the same moments; with an interval, ticks repeat every N minutes from
 * that anchor. java.time takes care of daylight saving.
 */
object Schedule {
    fun dailyTime(s: Settings): LocalTime = runCatching { LocalTime.parse(s.dailyTime) }.getOrDefault(LocalTime.of(9, 0))

    /** The most recent tick at or before [now]. */
    fun previous(s: Settings, now: Instant = Instant.now()): Instant {
        val zone = s.zone()
        val today = ZonedDateTime.of(now.atZone(zone).toLocalDate(), dailyTime(s), zone).toInstant()
        val anchor = if (today.isAfter(now)) today.atZone(zone).minusDays(1).toInstant() else today
        if (s.intervalMinutes <= 0) return anchor
        val step = Duration.ofMinutes(s.intervalMinutes.toLong()).seconds
        val steps = Duration.between(anchor, now).seconds / step
        return anchor.plusSeconds(steps * step)
    }

    /** The first tick after [now]. */
    fun next(s: Settings, now: Instant = Instant.now()): Instant {
        val prev = previous(s, now)
        if (s.intervalMinutes <= 0) return prev.atZone(s.zone()).plusDays(1).toInstant()
        val candidate = prev.plusSeconds(s.intervalMinutes * 60L)
        // The interval restarts at each day's anchor, so a step never runs past tomorrow's daily time.
        val sameDay = ZonedDateTime.of(prev.atZone(s.zone()).toLocalDate(), dailyTime(s), s.zone())
        val nextAnchor = (if (sameDay.toInstant().isAfter(prev)) sameDay else sameDay.plusDays(1)).toInstant()
        return if (candidate.isAfter(nextAnchor)) nextAnchor else candidate
    }

    /** An alarm a moment before the tick still counts as that tick. */
    private const val SLACK_SECONDS = 30L

    /** The tick a change made now belongs to. */
    fun dueTick(s: Settings, now: Instant = Instant.now()): Instant = previous(s, now.plusSeconds(SLACK_SECONDS))

    /** The tick's name, the same on every device: "2026-10-14T09:00" in the shared time zone. */
    fun slotId(s: Settings, tick: Instant): String =
        tick.atZone(s.zone()).format(java.time.format.DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm"))

    /** True when the latest tick has passed without a change (also after the phone was off at that time). */
    fun isDue(s: Settings, lastAppliedAt: Long, now: Instant = Instant.now()): Boolean =
        lastAppliedAt < previous(s, now).toEpochMilli()
}
