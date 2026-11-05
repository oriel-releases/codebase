package uz.akbarjon.oriel.work

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import uz.akbarjon.oriel.core.Scheduler

/** The schedule's tick: sets the next alarm, and the change that is now due goes to [ChangeJob] (which waits for a network). */
class AlarmReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        Scheduler.reschedule(context)
    }
}

/** Alarms don't survive a restart, an update or a clock/time zone change: set them again (and catch up if one was missed). */
class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        Scheduler.reschedule(context)
    }
}
