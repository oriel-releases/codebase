package uz.akbarjon.oriel.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import uz.akbarjon.oriel.core.UpdateState
import uz.akbarjon.oriel.core.Updater

/** Whether the Install item shows in the navigation bar: an update is downloaded and waits for a tap. */
@Composable
fun updateWaiting(): Boolean {
    val update by Updater.state.collectAsState()
    return update is UpdateState.ReadyToInstall
}
