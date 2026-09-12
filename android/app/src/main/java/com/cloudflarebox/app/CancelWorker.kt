package com.cloudflarebox.app

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class CancelWorker(appContext: Context, params: WorkerParameters) : CoroutineWorker(appContext, params) {
    private val db = TransferDb(appContext)
    private val queueId = inputData.getLong("queueId", -1L)

    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        if (queueId < 0) return@withContext Result.failure()
        val item = db.get(queueId) ?: return@withContext Result.success()
        val remoteId = item.remoteId
        if (remoteId == null) {
            db.setState(queueId, "CANCELED")
            return@withContext Result.success()
        }
        val config = PairingStore.load(applicationContext) ?: return@withContext Result.retry()
        try {
            ApiClient(config).cancel(remoteId)
            db.setState(queueId, "CANCELED")
            Result.success()
        } catch (e: Exception) {
            db.setState(queueId, "CANCEL_PENDING", e.message)
            Result.retry()
        }
    }
}
