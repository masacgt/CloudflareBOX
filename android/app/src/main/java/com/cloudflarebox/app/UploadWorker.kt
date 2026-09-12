package com.cloudflarebox.app

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.os.BatteryManager
import androidx.work.CoroutineWorker
import androidx.work.ForegroundInfo
import androidx.work.WorkerParameters
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
import kotlin.math.min

class UploadWorker(appContext: Context, params: WorkerParameters) : CoroutineWorker(appContext, params) {
    private val db = TransferDb(appContext)
    private val queueId = inputData.getLong("queueId", -1L)

    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        if (queueId < 0) return@withContext Result.failure()
        val config = PairingStore.load(applicationContext) ?: return@withContext Result.failure()
        val battery = applicationContext.getSystemService(BatteryManager::class.java)
            .getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY)
        if (battery in 0..14) {
            db.setState(queueId, "WAITING_BATTERY")
            return@withContext Result.retry()
        }
        setForeground(createForegroundInfo(0))
        try {
            var item = db.get(queueId) ?: return@withContext Result.failure()
            if (item.state == "CANCELED") return@withContext Result.success()
            val api = ApiClient(config)
            if (item.protectedFileKey == null) prepare(item, config)
            item = db.get(queueId) ?: return@withContext Result.failure()
            if (item.remoteId == null) createRemote(item, api)
            item = db.get(queueId) ?: return@withContext Result.failure()
            uploadParts(item, api)
            item = db.get(queueId) ?: return@withContext Result.failure()
            if (item.state == "CANCELED" || item.state == "PAUSED") return@withContext Result.success()
            api.completeUpload(item.remoteId!!)
            db.setState(queueId, "R2_READY")
            setForeground(createForegroundInfo(100))
            Result.success()
        } catch (_: PausedException) {
            db.setState(queueId, "PAUSED")
            Result.success()
        } catch (e: Exception) {
            val state = db.get(queueId)?.state
            if (state == "PAUSED" || state == "CANCELED") return@withContext Result.success()
            if (runAttemptCount >= 8) {
                db.setState(queueId, "FAILED", e.message)
                Result.failure()
            } else {
                db.setState(queueId, "RETRYING", e.message)
                Result.retry()
            }
        }
    }

    private fun prepare(item: QueueItem, config: PairingConfig) {
        require(item.sizeBytes in 0..10_000_000_000L) { "1ファイル10GBを超えています" }
        db.setState(item.id, "PREPARING")
        val digest = MessageDigest.getInstance("SHA-256")
        openSource(item.source).use { input ->
            val buffer = ByteArray(1024 * 1024)
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                if (read > 0) digest.update(buffer, 0, read)
            }
        }
        val sha = digest.digest().joinToString("") { "%02x".format(it) }
        val fileKey = CryptoManager.newFileKey()
        try {
            val metadata = JSONObject()
                .put("OriginalName", item.displayName)
                .put("OriginalLocation", item.source)
                .put("MimeType", item.mime ?: JSONObject.NULL)
                .put("CreatedAtUnixMs", JSONObject.NULL)
                .put("ModifiedAtUnixMs", System.currentTimeMillis())
                .put("PlaintextSha256", sha)
                .put("FormatVersion", 1)
            val encryptedMetadata = CryptoManager.encryptMetadata(fileKey, metadata.toString().toByteArray(Charsets.UTF_8))
            val wrapped = CryptoManager.wrapForWindows(fileKey, config.windowsEncryptionSpkiB64)
            db.savePrepared(
                item.id,
                CryptoManager.protectLocal(fileKey),
                sha,
                wrapped,
                encryptedMetadata.nonceB64,
                encryptedMetadata.cipherB64,
            )
        } finally {
            fileKey.fill(0)
        }
    }

    private suspend fun createRemote(item: QueueItem, api: ApiClient) {
        val body = JSONObject()
            .put("sizeBytes", item.sizeBytes)
            .put("plaintextSha256", item.plaintextSha256)
            .put("wrappedKeyB64", item.wrappedKeyB64)
            .put("metadataNonceB64", item.metadataNonceB64)
            .put("metadataCipherB64", item.metadataCipherB64)
            .put("priority", 1)
        val response = api.createTransfer(body)
        db.saveRemote(item.id, response.getString("id"), response.getLong("partSizeBytes"), response.getInt("partCount"))
    }

    private suspend fun uploadParts(item: QueueItem, api: ApiClient) {
        val remoteId = item.remoteId ?: error("remote transfer missing")
        val fileKey = CryptoManager.unprotectLocal(item.protectedFileKey ?: error("file key missing"))
        try {
            var nextPart = maxOf(1, item.nextPart)
            while (nextPart <= item.partCount) {
                val current = db.get(item.id) ?: error("queue item missing")
                if (current.state == "PAUSED") throw PausedException()
                if (current.state == "CANCELED") return
                val urls = api.partUrls(remoteId, listOf(nextPart)).getJSONArray("urls")
                val uploadUrl = urls.getJSONObject(0).getString("url")
                val plainOffset = if (item.partSize == 0L) 0L else (nextPart - 1L) * item.partSize
                val plainLength = if (item.sizeBytes == 0L) 0L else min(item.partSize, item.sizeBytes - plainOffset)
                val partFile = File(applicationContext.cacheDir, "cbx-${item.id}-$nextPart.part")
                try {
                    createEncryptedPart(item.source, plainOffset, plainLength, fileKey, partFile)
                    val encryptedHash = hashFile(partFile)
                    val etag = uploadPart(uploadUrl, partFile)
                    api.reportPart(remoteId, nextPart, etag, encryptedHash, partFile.length())
                } finally {
                    partFile.delete()
                }
                nextPart++
                val progress = (((nextPart - 1L) * 100L) / item.partCount.coerceAtLeast(1)).toInt().coerceIn(0, 100)
                db.saveProgress(item.id, nextPart, progress)
                setForeground(createForegroundInfo(progress))
            }
        } finally {
            fileKey.fill(0)
        }
    }

    private fun createEncryptedPart(source: String, offset: Long, length: Long, fileKey: ByteArray, target: File) {
        val nonce = ByteArray(12).also { SecureRandom().nextBytes(it) }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(fileKey, "AES"), GCMParameterSpec(128, nonce))
        target.outputStream().buffered(1024 * 1024).use { output ->
            output.write(byteArrayOf(1))
            output.write(nonce)
            openSource(source).use { input ->
                skipFully(input, offset)
                var remaining = length
                val buffer = ByteArray(1024 * 1024)
                while (remaining > 0) {
                    val read = input.read(buffer, 0, min(buffer.size.toLong(), remaining).toInt())
                    if (read < 0) throw java.io.EOFException("source ended early")
                    if (read == 0) continue
                    val encrypted = cipher.update(buffer, 0, read)
                    if (encrypted != null && encrypted.isNotEmpty()) output.write(encrypted)
                    remaining -= read
                }
            }
            output.write(cipher.doFinal())
        }
    }

    private fun uploadPart(url: String, file: File): String {
        val connection = URL(url).openConnection() as HttpURLConnection
        connection.requestMethod = "PUT"
        connection.doOutput = true
        connection.connectTimeout = 30_000
        connection.readTimeout = 120_000
        connection.setFixedLengthStreamingMode(file.length())
        connection.setRequestProperty("Content-Type", "application/octet-stream")
        file.inputStream().buffered(1024 * 1024).use { input ->
            connection.outputStream.use { output -> input.copyTo(output, 1024 * 1024) }
        }
        val code = connection.responseCode
        if (code !in 200..299) {
            val text = connection.errorStream?.bufferedReader()?.use { it.readText() }.orEmpty()
            throw IllegalStateException("R2 upload $code: $text")
        }
        return connection.getHeaderField("ETag")?.trim()?.takeIf { it.isNotBlank() }
            ?: throw IllegalStateException("R2 did not return ETag")
    }

    private fun openSource(source: String): InputStream {
        return if (source.startsWith("content://")) {
            applicationContext.contentResolver.openInputStream(android.net.Uri.parse(source))
                ?: throw IllegalStateException("source cannot be opened")
        } else {
            FileInputStream(File(source.removePrefix("file://")))
        }
    }

    private fun skipFully(input: InputStream, bytes: Long) {
        var remaining = bytes
        val scratch = ByteArray(64 * 1024)
        while (remaining > 0) {
            val skipped = input.skip(remaining)
            if (skipped > 0) {
                remaining -= skipped
                continue
            }
            val read = input.read(scratch, 0, min(scratch.size.toLong(), remaining).toInt())
            if (read < 0) throw java.io.EOFException("source ended before requested offset")
            remaining -= read
        }
    }

    private fun hashFile(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().buffered(1024 * 1024).use { input ->
            val buffer = ByteArray(1024 * 1024)
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                if (read > 0) digest.update(buffer, 0, read)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    private fun createForegroundInfo(progress: Int): ForegroundInfo {
        val manager = applicationContext.getSystemService(NotificationManager::class.java)
        val channelId = "cloudflarebox-transfer"
        manager.createNotificationChannel(NotificationChannel(channelId, "CloudflareBOX 転送", NotificationManager.IMPORTANCE_LOW))
        val notification = Notification.Builder(applicationContext, channelId)
            .setContentTitle("CloudflareBOX")
            .setContentText(if (progress >= 100) "R2への送信完了" else "送信中 $progress%")
            .setSmallIcon(android.R.drawable.stat_sys_upload)
            .setOnlyAlertOnce(true)
            .setOngoing(progress < 100)
            .setProgress(100, progress.coerceIn(0, 100), false)
            .build()
        return ForegroundInfo(queueId.coerceIn(1, Int.MAX_VALUE.toLong()).toInt(), notification)
    }

    private class PausedException : RuntimeException()
}
