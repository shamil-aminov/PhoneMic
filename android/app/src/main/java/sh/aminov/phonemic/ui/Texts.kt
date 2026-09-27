package sh.aminov.phonemic.ui

import android.content.Context
import sh.aminov.phonemic.R
import sh.aminov.phonemic.audio.LinkState
import sh.aminov.phonemic.audio.Refusal
import sh.aminov.phonemic.net.TransportKind

/** Words for engine state, shared by the screen and the notification. */
object Texts {
    fun pcName(context: Context, name: String): String =
        name.ifEmpty { context.getString(R.string.computer_unnamed) }

    /** "USB", or "Wi-Fi · 12 ms" once a round trip has been measured. */
    fun link(context: Context, link: LinkState.Connected): String {
        val transport = if (link.transport == TransportKind.USB) "USB" else "Wi-Fi"
        return link.rttMs?.let { context.getString(R.string.link_with_rtt, transport, it) } ?: transport
    }

    fun refusal(context: Context, refusal: Refusal): String = context.getString(
        when (refusal) {
            Refusal.WrongToken -> R.string.refused_wrong_token
            Refusal.Busy -> R.string.refused_busy
        },
    )
}
