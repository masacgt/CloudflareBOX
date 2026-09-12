package com.cloudflarebox.app

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper

data class QueueItem(
    val id: Long,
    val source: String,
    val displayName: String,
    val sizeBytes: Long,
    val mime: String?,
    val state: String,
    val progress: Int,
    val remoteId: String?,
    val partSize: Long,
    val partCount: Int,
    val nextPart: Int,
    val protectedFileKey: String?,
    val plaintextSha256: String?,
    val wrappedKeyB64: String?,
    val metadataNonceB64: String?,
    val metadataCipherB64: String?,
    val error: String?,
)

class TransferDb(context: Context) : SQLiteOpenHelper(context, "cloudflarebox.db", null, 1) {
    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL("""CREATE TABLE queue(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            source TEXT NOT NULL,
            display_name TEXT NOT NULL,
            size_bytes INTEGER NOT NULL,
            mime TEXT,
            state TEXT NOT NULL,
            progress INTEGER NOT NULL DEFAULT 0,
            remote_id TEXT,
            part_size INTEGER NOT NULL DEFAULT 0,
            part_count INTEGER NOT NULL DEFAULT 0,
            next_part INTEGER NOT NULL DEFAULT 1,
            protected_file_key TEXT,
            plaintext_sha256 TEXT,
            wrapped_key_b64 TEXT,
            metadata_nonce_b64 TEXT,
            metadata_cipher_b64 TEXT,
            error TEXT,
            created_at INTEGER NOT NULL
        )""")
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) = Unit

    fun enqueue(source: String, displayName: String, sizeBytes: Long, mime: String?): Long {
        val values = ContentValues().apply {
            put("source", source)
            put("display_name", displayName)
            put("size_bytes", sizeBytes)
            put("mime", mime)
            put("state", "QUEUED")
            put("created_at", System.currentTimeMillis())
        }
        return writableDatabase.insertOrThrow("queue", null, values)
    }

    fun get(id: Long): QueueItem? {
        readableDatabase.query("queue", null, "id=?", arrayOf(id.toString()), null, null, null).use { c ->
            return if (c.moveToFirst()) fromCursor(c) else null
        }
    }

    fun list(limit: Int = 100): List<QueueItem> {
        val out = mutableListOf<QueueItem>()
        readableDatabase.query("queue", null, null, null, null, null, "created_at DESC", limit.toString()).use { c ->
            while (c.moveToNext()) out += fromCursor(c)
        }
        return out
    }

    fun savePrepared(id: Long, protectedFileKey: String, plaintextSha256: String, wrappedKeyB64: String, metadataNonceB64: String, metadataCipherB64: String) {
        val values = ContentValues().apply {
            put("protected_file_key", protectedFileKey)
            put("plaintext_sha256", plaintextSha256)
            put("wrapped_key_b64", wrappedKeyB64)
            put("metadata_nonce_b64", metadataNonceB64)
            put("metadata_cipher_b64", metadataCipherB64)
            put("state", "PREPARED")
        }
        writableDatabase.update("queue", values, "id=?", arrayOf(id.toString()))
    }

    fun saveRemote(id: Long, remoteId: String, partSize: Long, partCount: Int) {
        val values = ContentValues()
        values.put("remote_id", remoteId)
        values.put("part_size", partSize)
        values.put("part_count", partCount)
        values.put("next_part", 1)
        values.put("state", "UPLOADING")
        writableDatabase.update("queue", values, "id=?", arrayOf(id.toString()))
    }

    fun saveProgress(id: Long, nextPart: Int, progress: Int) {
        val values = ContentValues()
        values.put("next_part", nextPart)
        values.put("progress", progress)
        values.put("state", "UPLOADING")
        writableDatabase.update("queue", values, "id=?", arrayOf(id.toString()))
    }

    fun setState(id: Long, state: String, error: String? = null) {
        val values = ContentValues()
        values.put("state", state)
        values.put("error", error)
        writableDatabase.update("queue", values, "id=?", arrayOf(id.toString()))
    }

    private fun fromCursor(c: android.database.Cursor): QueueItem = QueueItem(
        id = c.getLong(c.getColumnIndexOrThrow("id")),
        source = c.getString(c.getColumnIndexOrThrow("source")),
        displayName = c.getString(c.getColumnIndexOrThrow("display_name")),
        sizeBytes = c.getLong(c.getColumnIndexOrThrow("size_bytes")),
        mime = c.getString(c.getColumnIndexOrThrow("mime")),
        state = c.getString(c.getColumnIndexOrThrow("state")),
        progress = c.getInt(c.getColumnIndexOrThrow("progress")),
        remoteId = c.getString(c.getColumnIndexOrThrow("remote_id")),
        partSize = c.getLong(c.getColumnIndexOrThrow("part_size")),
        partCount = c.getInt(c.getColumnIndexOrThrow("part_count")),
        nextPart = c.getInt(c.getColumnIndexOrThrow("next_part")),
        protectedFileKey = c.getString(c.getColumnIndexOrThrow("protected_file_key")),
        plaintextSha256 = c.getString(c.getColumnIndexOrThrow("plaintext_sha256")),
        wrappedKeyB64 = c.getString(c.getColumnIndexOrThrow("wrapped_key_b64")),
        metadataNonceB64 = c.getString(c.getColumnIndexOrThrow("metadata_nonce_b64")),
        metadataCipherB64 = c.getString(c.getColumnIndexOrThrow("metadata_cipher_b64")),
        error = c.getString(c.getColumnIndexOrThrow("error")),
    )
}
