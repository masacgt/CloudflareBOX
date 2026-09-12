package com.cloudflarebox.app

import android.content.Context
import org.json.JSONObject

data class PairingConfig(
    val apiBase: String,
    val androidDeviceId: String,
    val windowsDeviceId: String,
    val windowsEncryptionSpkiB64: String,
)

object PairingStore {
    private const val PREF = "cloudflarebox_pairing"
    private const val KEY = "config"

    fun load(context: Context): PairingConfig? {
        val raw = context.getSharedPreferences(PREF, Context.MODE_PRIVATE).getString(KEY, null) ?: return null
        val j = JSONObject(raw)
        return PairingConfig(
            j.getString("apiBase"),
            j.getString("androidDeviceId"),
            j.getString("windowsDeviceId"),
            j.getString("windowsEncryptionSpkiB64"),
        )
    }

    fun save(context: Context, config: PairingConfig) {
        val j = JSONObject()
            .put("apiBase", config.apiBase)
            .put("androidDeviceId", config.androidDeviceId)
            .put("windowsDeviceId", config.windowsDeviceId)
            .put("windowsEncryptionSpkiB64", config.windowsEncryptionSpkiB64)
        context.getSharedPreferences(PREF, Context.MODE_PRIVATE).edit().putString(KEY, j.toString()).apply()
    }

    fun clear(context: Context) {
        context.getSharedPreferences(PREF, Context.MODE_PRIVATE).edit().clear().apply()
    }
}
