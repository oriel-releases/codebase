package uz.akbarjon.oriel.data

import android.content.ContentValues
import android.content.Context
import android.database.Cursor
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.withContext

/**
 * Every photo Oriel applied, one row per photo (applying it again moves it to the top). Only metadata: no image
 * files are kept, so History costs no storage. A deleted photo stays as a hidden row so it is never picked again.
 */
class History(context: Context) : SQLiteOpenHelper(context, "history.db", null, 1) {
    private val _version = MutableStateFlow(0)
    /** Bumped after every change, so screens know to reload. */
    val version: StateFlow<Int> = _version.asStateFlow()

    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL(
            """CREATE TABLE history (
                photo_id TEXT PRIMARY KEY, width INTEGER, height INTEGER, likes INTEGER, description TEXT, color TEXT,
                raw_url TEXT NOT NULL, page_url TEXT, photographer TEXT, photographer_url TEXT, topic TEXT,
                applied_at INTEGER NOT NULL, favorite INTEGER NOT NULL DEFAULT 0, deleted INTEGER NOT NULL DEFAULT 0)""",
        )
        db.execSQL("CREATE INDEX history_applied ON history(deleted, applied_at DESC)")
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) = Unit

    /** Records a photo as applied now (keeps its favorite flag if it was applied before). */
    suspend fun add(photo: Photo, topic: String?, at: Long = System.currentTimeMillis()) = write {
        val values = ContentValues().apply {
            put("photo_id", photo.id); put("width", photo.width); put("height", photo.height); put("likes", photo.likes)
            put("description", photo.description); put("color", photo.color); put("raw_url", photo.rawUrl)
            put("page_url", photo.pageUrl); put("photographer", photo.photographer); put("photographer_url", photo.photographerUrl)
            put("topic", topic); put("applied_at", at); put("deleted", 0)
        }
        val db = writableDatabase
        if (db.update("history", values, "photo_id = ?", arrayOf(photo.id)) == 0) db.insert("history", null, values)
    }

    suspend fun list(favoritesOnly: Boolean = false): List<HistoryEntry> = read {
        val where = if (favoritesOnly) "deleted = 0 AND favorite = 1" else "deleted = 0"
        readableDatabase.query("history", null, where, null, null, null, "applied_at DESC", "500").use { c ->
            buildList { while (c.moveToNext()) add(c.entry()) }
        }
    }

    /** This photo's entry (deleted ones too), or null when it was never shown here. */
    suspend fun get(photoId: String): HistoryEntry? = read {
        readableDatabase.query("history", null, "photo_id = ?", arrayOf(photoId), null, null, null, "1").use { c ->
            if (c.moveToFirst()) c.entry() else null
        }
    }

    suspend fun latest(): HistoryEntry? = read {
        readableDatabase.query("history", null, "deleted = 0", null, null, null, "applied_at DESC", "1").use { c ->
            if (c.moveToFirst()) c.entry() else null
        }
    }

    /** True if the photo was ever applied here (deleted ones too), so it isn't picked again. */
    suspend fun wasUsed(photoId: String): Boolean = read {
        readableDatabase.rawQuery("SELECT 1 FROM history WHERE photo_id = ?", arrayOf(photoId)).use { it.moveToFirst() }
    }

    suspend fun setFavorite(photoId: String, favorite: Boolean) = write {
        writableDatabase.update("history", ContentValues().apply { put("favorite", if (favorite) 1 else 0) }, "photo_id = ?", arrayOf(photoId))
    }

    suspend fun delete(photoId: String) = write {
        writableDatabase.update("history", ContentValues().apply { put("deleted", 1); put("favorite", 0) }, "photo_id = ?", arrayOf(photoId))
    }

    suspend fun clear() = write { writableDatabase.delete("history", null, null) }

    private suspend fun <T> read(block: () -> T): T = withContext(Dispatchers.IO) { block() }

    private suspend fun write(block: () -> Any?) {
        withContext(Dispatchers.IO) { block() }
        _version.value++
    }

    private fun Cursor.entry(): HistoryEntry {
        fun s(name: String) = getString(getColumnIndexOrThrow(name))
        fun i(name: String) = getInt(getColumnIndexOrThrow(name))
        return HistoryEntry(
            id = getLong(getColumnIndexOrThrow("applied_at")),
            photo = Photo(
                id = s("photo_id"), width = i("width"), height = i("height"), likes = i("likes"), description = s("description"),
                color = s("color"), rawUrl = s("raw_url"), pageUrl = s("page_url") ?: "", photographer = s("photographer") ?: "",
                photographerUrl = s("photographer_url") ?: "", downloadLocation = null,
            ),
            topic = s("topic"),
            appliedAt = getLong(getColumnIndexOrThrow("applied_at")),
            isFavorite = i("favorite") == 1,
        )
    }
}
