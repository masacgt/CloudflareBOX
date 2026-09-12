package com.cloudflarebox.app

import android.os.Build
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.util.UUID

class ApiClient(private val config: PairingConfig) {
    suspend fun status(): JSONObject = signed("GET", "status", null)

    suspend fun createTransfer(body: JSONObject): JSONObject = signed("POST", "transfers", body)

    suspend fun partUrls(transferId: String, numbers: List<Int>): JSONObject =
        signed("POST", "transfers/$transferId/parts/urls", JSONObject().put("partNumbers", JSONArray(numbers)))

    suspend fun reportPart(transferId: String, partNumber: Int, etag: String, encryptedSha256: String, sizeBytes: Long): JSONObject =
        signed("POST", "transfers/$transferId/parts/report", JSONObject()
            .put("partNumber", partNumber)
            .put("etag", etag)
            .put("encryptedSha256", encryptedSha256)
            .put("sizeBytes", sizeBytes))

    suspend fun completeUpload(transferId: String): JSONObject = signed("POST", "transfers/$transferId/complete-upload", JSONObject())

    suspend fun cancel(transferId: String): JSONObject = signed("POST", "transfers/$transferId/cancel", JSONObject())

    private suspend fun signed(method: String, relative: String, json: JSONObject?): JSONObject = withContext(Dispatchers.IO) {
        val url = URL(config.apiBase.trimEnd('/') + "/" + relative)
        val body = json?.toString()?.toByteArray(Charsets.UTF_8) ?: ByteArray(0)
        val timestamp = (System.currentTimeMillis() / 1000L).toString()
        val nonce = UUID.randomUUID().toString()
        val bodyHash = sha256Hex(body)
        val pathAndQuery = url.path + (url.query?.let { "?$it" } ?: "")
        val canonical = listOf(method.uppercase(), pathAndQuery, bodyHash, timestamp, nonce).joinToString("\n").toByteArray(Charsets.UTF_8)
        val connection = url.openConnection() as HttpURLConnection
        connection.requestMethod = method
        connection.connectTimeout = 20_000
        connection.readTimeout = 120_000
        connection.setRequestProperty("Accept", "application/json")
        connection.setRequestProperty("X-CB-Device-Id", config.androidDeviceId)
        connection.setRequestProperty("X-CB-Timestamp", timestamp)
        connection.setRequestProperty("X-CB-Nonce", nonce)
        connection.setRequestProperty("X-CB-Signature", CryptoManager.sign(canonical))
        if (body.isNotEmpty()) {
            connection.doOutput = true
            connection.setRequestProperty("Content-Type", "application/json")
            connection.outputStream.use { it.write(body) }
        }
        readJson(connection)
    }

    private fun readJson(connection: HttpURLConnection): JSONObject {
        val code = connection.responseCode
        val stream = if (code in 200..299) connection.inputStream else connection.errorStream
        val text = stream?.bufferedReader()?.use { it.readText() }.orEmpty()
        if (code !in 200..299) throw IllegalStateException("API $code: $text")
        return if (text.isBlank()) JSONObject() else JSONObject(text)
    }

    private fun sha256Hex(bytes: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }

    companion object {
        suspend fun pair(rawPayload: String): PairingConfig = withContext(Dispatchers.IO) {
            val outer = JSONObject(rawPayload)
            val payload = if (outer.has("qrPayload")) outer.getJSONObject("qrPayload") else outer
            val apiBase = payload.getString("apiBase")
            val pairingId = payload.getString("pairingId")
            val code = payload.getString("code")
            val windowsDeviceId = payload.getString("windowsDeviceId")
            val windowsKey = payload.getString("windowsEncryptionPublicKeySpkiB64")
            val body = JSONObject()
                .put("pairingId", pairingId)
                .put("code", code)
                .put("name", Build.MODEL)
                .put("signingPublicKeySpkiB64", CryptoManager.signingPublicSpkiB64())
            val connection = URL(apiBase.trimEnd('/') + "/pairing/complete").openConnection() as HttpURLConnection
            connection.requestMethod = "POST"
            connection.doOutput = true
            connection.connectTimeout = 20_000
            connection.readTimeout = 30_000
            connection.setRequestProperty("Content-Type", "application/json")
            connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            val responseCode = connection.responseCode
            val stream = if (responseCode in 200..299) connection.inputStream else connection.errorStream
            val text = stream?.bufferedReader()?.use { it.readText() }.orEmpty()
            if (responseCode !in 200..299) throw IllegalStateException("Pairing $responseCode: $text")
            val response = JSONObject(text)
            PairingConfig(apiBase, response.getString("androidDeviceId"), windowsDeviceId, windowsKey)
        }
    }
}
