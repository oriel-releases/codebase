package uz.akbarjon.oriel.data

import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** An HTTP answer, read completely (bodies here are small JSON). */
class HttpResponse(val code: Int, val body: String, private val headers: Map<String, List<String>>) {
    val ok get() = code in 200..299
    fun header(name: String): String? =
        headers.entries.firstOrNull { it.key.equals(name, ignoreCase = true) }?.value?.firstOrNull()
}

/**
 * The platform's HttpURLConnection, nothing more: no HTTP library to ship or keep in memory. Responses are cached on
 * disk by the HttpResponseCache installed in [uz.akbarjon.oriel.OrielApp] (thumbnails mostly). Throws IOException
 * when offline.
 */
object Http {
    private const val TIMEOUT_MS = 15_000
    private const val CONNECT_TIMEOUT_MS = 5_000

    suspend fun get(url: String, headers: Map<String, String> = emptyMap()): HttpResponse =
        send("GET", url, headers, null)

    suspend fun post(url: String, body: String, contentType: String = "application/json", headers: Map<String, String> = emptyMap()) =
        send("POST", url, headers + ("Content-Type" to contentType), body)

    /** A POST whose answer takes a while to make (an AI picture: often 10 s, sometimes a minute). */
    suspend fun postSlow(url: String, body: String, headers: Map<String, String> = emptyMap()) =
        send("POST", url, headers + ("Content-Type" to "application/json"), body, readTimeoutMs = 90_000)

    suspend fun delete(url: String, headers: Map<String, String> = emptyMap()) = send("DELETE", url, headers, null)

    suspend fun put(url: String, body: String, contentType: String = "application/json") =
        send("PUT", url, mapOf("Content-Type" to contentType), body)

    /** HttpURLConnection has no PATCH: a POST the Realtime Database treats as one. */
    suspend fun patch(url: String, body: String) =
        send("POST", url, mapOf("Content-Type" to "application/json", "X-HTTP-Method-Override" to "PATCH"), body)

    private suspend fun send(method: String, url: String, headers: Map<String, String>, body: String?,
        readTimeoutMs: Int = TIMEOUT_MS): HttpResponse =
        withContext(Dispatchers.IO) {
            val conn = open(url)
            conn.readTimeout = readTimeoutMs
            try {
                conn.requestMethod = method
                headers.forEach { (k, v) -> conn.setRequestProperty(k, v) }
                if (body != null) {
                    conn.doOutput = true
                    val bytes = body.toByteArray()
                    conn.setFixedLengthStreamingMode(bytes.size)
                    conn.outputStream.use { it.write(bytes) }
                }
                val code = conn.responseCode
                val stream = if (code >= 400) conn.errorStream else conn.inputStream
                val text = stream?.bufferedReader()?.use { it.readText() } ?: ""
                HttpResponse(code, text, conn.headerFields.filterKeys { it != null })
            } finally {
                conn.disconnect()
            }
        }

    /** Streams [url] into [target] (via a temporary file, so a broken download never leaves half a file). */
    suspend fun download(url: String, target: File, onProgress: ((done: Long, total: Long) -> Unit)? = null) = withContext(Dispatchers.IO) {
        val conn = open(url)
        conn.useCaches = false // full-size images are used once; keep them out of the cache
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
            val part = File(target.path + ".part")
            val total = conn.contentLengthLong
            conn.inputStream.use { input ->
                part.outputStream().use { out ->
                    val buffer = ByteArray(64 * 1024)
                    var done = 0L
                    while (true) {
                        val n = input.read(buffer)
                        if (n < 0) break
                        out.write(buffer, 0, n)
                        done += n
                        onProgress?.invoke(done, total)
                    }
                }
            }
            if (!part.renameTo(target)) throw IOException("Couldn't save the photo")
        } finally {
            conn.disconnect()
        }
    }

    /** Raw bytes, through the disk cache (thumbnails). */
    suspend fun bytes(url: String): ByteArray = withContext(Dispatchers.IO) {
        val conn = open(url)
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
            conn.inputStream.use { it.readBytes() }
        } finally {
            conn.disconnect()
        }
    }

    private fun open(url: String) = (URL(url).openConnection() as HttpURLConnection).apply {
        // Connecting gets a few seconds only: Android tries a host's addresses one after another, and on a network where
        // IPv6 is broken (it hangs) a long wait here stalled every request before IPv4 was tried. Reading keeps the long one.
        connectTimeout = CONNECT_TIMEOUT_MS
        readTimeout = TIMEOUT_MS
    }
}
