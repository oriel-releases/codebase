package uz.akbarjon.oriel.work

import android.app.job.JobParameters
import android.app.job.JobService
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import uz.akbarjon.oriel.Oriel
import uz.akbarjon.oriel.core.ChangeResult
import uz.akbarjon.oriel.core.Scheduler

/** Does the background work the [Scheduler] asked for, then lets the system put the app back to sleep. */
class ChangeJob : JobService() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val running = mutableMapOf<Int, Job>()

    override fun onStartJob(params: JobParameters): Boolean {
        running[params.jobId] = scope.launch {
            val retry = when (params.jobId) {
                Scheduler.JOB_CHANGE -> (Oriel.wallpapers.scheduled() as? ChangeResult.Failed)?.transient == true
                Scheduler.JOB_FOLLOW -> {
                    // The background check: the account's settings and photo, in that order.
                    uz.akbarjon.oriel.core.SettingsSync.pull()
                    uz.akbarjon.oriel.core.FavoritesSync.pull()
                    !Oriel.following.check()
                }
                else -> false
            }
            jobFinished(params, retry)
        }
        return true
    }

    override fun onStopJob(params: JobParameters): Boolean {
        running.remove(params.jobId)?.cancel()
        return true // stopped by the system (network lost, quota): try again later
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }
}
