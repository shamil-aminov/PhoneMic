package sh.aminov.phonemic.data

import android.content.Context
import android.net.Uri
import androidx.core.content.edit
import sh.aminov.phonemic.net.Protocol
import java.util.UUID

/** What the PC's QR code says: where to find it and the token it expects. */
data class Pairing(
    val addresses: List<String>,
    val port: Int,
    val token: String,
    val pcName: String,
) {
    companion object {
        /** Parses `phonemic://pair?a=1.2.3.4,5.6.7.8&p=50505&t=TOKEN&n=NAME`. */
        fun parse(text: String): Pairing? {
            val uri = runCatching { Uri.parse(text.trim()) }.getOrNull() ?: return null
            if (uri.scheme != "phonemic" || uri.host != "pair") return null
            val token = uri.getQueryParameter("t")?.takeIf { it.isNotBlank() } ?: return null
            val addresses = uri.getQueryParameter("a").orEmpty().split(',').map { it.trim() }.filter { it.isNotEmpty() }
            val port = uri.getQueryParameter("p")?.toIntOrNull() ?: Protocol.DEFAULT_PORT
            return Pairing(addresses, port, token, uri.getQueryParameter("n") ?: "ПК")
        }
    }
}

class Prefs(context: Context) {
    private val prefs = context.getSharedPreferences("phonemic", Context.MODE_PRIVATE)

    var pairing: Pairing?
        get() {
            val token = prefs.getString("token", null) ?: return null
            return Pairing(
                addresses = prefs.getString("addresses", "").orEmpty().split(',').filter { it.isNotEmpty() },
                port = prefs.getInt("port", Protocol.DEFAULT_PORT),
                token = token,
                pcName = prefs.getString("pcName", "ПК").orEmpty(),
            )
        }
        set(value) = prefs.edit {
            if (value == null) {
                remove("token"); remove("addresses"); remove("port"); remove("pcName"); remove("lastAddress")
            } else {
                putString("token", value.token)
                putString("addresses", value.addresses.joinToString(","))
                putInt("port", value.port)
                putString("pcName", value.pcName)
            }
        }

    /** The address that answered last time, tried first on reconnect. */
    var lastAddress: String?
        get() = prefs.getString("lastAddress", null)
        set(value) = prefs.edit { putString("lastAddress", value) }

    /** Lets the PC tell a reconnect of this phone apart from a different phone. */
    val deviceId: String
        get() = prefs.getString("deviceId", null) ?: UUID.randomUUID().toString().also { id ->
            prefs.edit { putString("deviceId", id) }
        }

    var noiseSuppression: Boolean
        get() = prefs.getBoolean("noiseSuppression", false)
        set(value) = prefs.edit { putBoolean("noiseSuppression", value) }
}
