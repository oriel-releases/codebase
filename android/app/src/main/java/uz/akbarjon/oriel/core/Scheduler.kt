package uz.akbarjon.oriel.core

import android.app.AlarmManager
import android.app.PendingIntent
import android.app.job.JobInfo
import android.app.job.JobScheduler
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.os.Build
import android.util.Log
import java.util.concurrent.TimeUnit
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.work.AlarmReceiver
import uz.akbarjon.oriel.work.ChangeJob

/**
 * Wakes Oriel only when there is something to do, so it costs no battery in between:
 *
 * - Own photos: one alarm at the next tick (inexact but allowed while idle, so no special permission). The alarm
 *   only queues [ChangeJob], which runs once a network is there and retries with backoff when offline.
 * - Sync: a periodic job (every [FOLLOW_MINUTES] min, only with a network and enough battery) reads one tiny
 *   document, the account's `sync/current` (or a followed friend's), and shows a photo another device set. Opening
 *   the app checks right away too. Locked (paused): no alarm, only the sync check (a photo set by hand elsewhere
 *   still shows here). Nothing at all runs while signed out.
 */
object Scheduler {
    private const val TAG = "Oriel"
    const val JOB_CHANGE = 1
    const val JOB_FOLLOW = 2
    private const val FOLLOW_MINUTES = 15L

    /**
     * The schedule starts fresh from now (sign-in, or the user edited it): the next change is the next tick, and
     * nothing "missed" under the old settings is caught up.
     */
    fun restart(context: Context) {
        Oriel.store.updateState { it.copy(scheduleStartedAt = System.currentTimeMillis()) }
        reschedule(context)
    }

    fun reschedule(context: Context) {
        val s = Oriel.store.settings.value
        val signedIn = Oriel.auth.isSignedIn
        val alarms = context.getSystemService(AlarmManager::class.java)
        val jobs = context.getSystemService(JobScheduler::class.java)

        alarms.cancel(alarmIntent(context))
        if (signedIn && !s.paused && !s.isFollowing) {
            val at = Schedule.next(s).toEpochMilli()
            alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, alarmIntent(context))
            Log.i(TAG, "Next change at ${java.time.Instant.ofEpochMilli(at)}")
            // Missed one (phone was off, or the app just started): catch up now.
            if (Schedule.isDue(s, Oriel.store.state.value.scheduleBaseline)) runChangeSoon(context)
        }

        // The account's photo (or the friend's) is checked every so often: a change on another device shows up here.
        if (signedIn) {
            if (jobs.getPendingJob(JOB_FOLLOW) == null) {
                jobs.schedule(JobInfo.Builder(JOB_FOLLOW, ComponentName(context, ChangeJob::class.java))
                    .setPeriodic(TimeUnit.MINUTES.toMillis(FOLLOW_MINUTES), TimeUnit.MINUTES.toMillis(10))
                    .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
                    .setRequiresBatteryNotLow(true)
                    .setPersisted(true)
                    .build())
            }
        } else {
            jobs.cancel(JOB_FOLLOW)
        }
    }

    /** Queues the scheduled change to run as soon as there is a network (once: a queued or running one is kept). */
    fun runChangeSoon(context: Context) {
        val jobs = context.getSystemService(JobScheduler::class.java)
        if (jobs.getPendingJob(JOB_CHANGE) != null) return
        val builder = JobInfo.Builder(JOB_CHANGE, ComponentName(context, ChangeJob::class.java))
            .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
            .setBackoffCriteria(TimeUnit.MINUTES.toMillis(2), JobInfo.BACKOFF_POLICY_EXPONENTIAL)
        // Expedited: runs right away even in Doze, within the system's small quota.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            try {
                if (jobs.schedule(builder.setExpedited(true).build()) == JobScheduler.RESULT_SUCCESS) return
            } catch (e: Exception) {
                Log.i(TAG, "Expedited job refused: ${e.message}")
            }
            builder.setExpedited(false)
        }
        jobs.schedule(builder.build())
    }

    fun cancelAll(context: Context) {
        context.getSystemService(AlarmManager::class.java).cancel(alarmIntent(context))
        context.getSystemService(JobScheduler::class.java).cancelAll()
    }

    private fun alarmIntent(context: Context): PendingIntent = PendingIntent.getBroadcast(
        context, 0, Intent(context, AlarmReceiver::class.java),
        PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
    )
}
