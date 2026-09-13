package com.cloudflarebox.app

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.OpenableColumns
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ElevatedCard
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.lifecycleScope
import androidx.work.Constraints
import androidx.work.Data
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class MainActivity : ComponentActivity() {
    private val db by lazy { TransferDb(this) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        handleShareIntent(intent)
        db.list().filter { it.state == "CANCEL_PENDING" }.forEach { scheduleCancel(it.id) }
        setContent {
            CloudflareBoxTheme {
                Surface(Modifier.fillMaxSize()) { CloudflareBoxScreen() }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleShareIntent(intent)
    }

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    private fun CloudflareBoxScreen() {
        var pairingText by remember { mutableStateOf("") }
        var message by remember { mutableStateOf("") }
        var paired by remember { mutableStateOf(PairingStore.load(this) != null) }
        var queue by remember { mutableStateOf(emptyList<QueueItem>()) }
        val scope = rememberCoroutineScope()
        val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
            uris.forEach { uri ->
                try { contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION) } catch (_: Exception) { }
                enqueueUri(uri)
            }
        }

        LaunchedEffect(Unit) {
            while (true) {
                val latest = withContext(Dispatchers.IO) { db.list() }
                refreshCompletedTransfers(latest)
                queue = withContext(Dispatchers.IO) { db.list() }
                delay(5000)
            }
        }

        Scaffold(
            topBar = {
                TopAppBar(
                    title = {
                        Column {
                            Text("CFBox", fontWeight = FontWeight.SemiBold)
                            Text(
                                "スマホから自宅PCへ安全に転送",
                                style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    },
                    colors = TopAppBarDefaults.topAppBarColors(
                        containerColor = MaterialTheme.colorScheme.surface,
                    ),
                )
            },
        ) { padding ->
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                contentPadding = PaddingValues(
                    start = 16.dp,
                    top = padding.calculateTopPadding() + 8.dp,
                    end = 16.dp,
                    bottom = 24.dp,
                ),
                verticalArrangement = Arrangement.spacedBy(16.dp),
            ) {
                item {
                    ElevatedCard(
                        colors = CardDefaults.elevatedCardColors(
                            containerColor = MaterialTheme.colorScheme.primaryContainer,
                        ),
                    ) {
                        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Text(
                                if (paired) "接続準備完了" else "セットアップが必要です",
                                style = MaterialTheme.typography.titleLarge,
                            )
                            Text(
                                if (paired) "Windows PCとペアリング済みです。ファイルを選択して送信できます。"
                                else "Windowsアプリに表示されたペアリングJSONを入力してください。",
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onPrimaryContainer,
                            )
                        }
                    }
                }

                if (!paired) {
                    item {
                        ElevatedCard {
                            Column(
                                Modifier.padding(18.dp),
                                verticalArrangement = Arrangement.spacedBy(12.dp),
                            ) {
                                Text("Windowsとペアリング", style = MaterialTheme.typography.titleMedium)
                                OutlinedTextField(
                                    value = pairingText,
                                    onValueChange = { pairingText = it },
                                    modifier = Modifier.fillMaxWidth(),
                                    minLines = 5,
                                    label = { Text("WindowsのペアリングJSON") },
                                    supportingText = { Text("Windowsアプリの「初回ペアリングJSONをコピー」から貼り付けます。") },
                                )
                                Button(
                                    onClick = {
                                        scope.launch {
                                            try {
                                                val config = ApiClient.pair(pairingText.trim())
                                                PairingStore.save(this@MainActivity, config)
                                                paired = true
                                                db.list()
                                                    .filter { it.state == "QUEUED" || it.state == "FAILED" || it.state == "WAITING_BATTERY" }
                                                    .forEach { schedule(it.id) }
                                                message = "ペアリングが完了しました"
                                            } catch (e: Exception) {
                                                message = e.message ?: "ペアリングに失敗しました"
                                            }
                                        }
                                    },
                                    enabled = pairingText.isNotBlank(),
                                    modifier = Modifier.fillMaxWidth(),
                                ) {
                                    Text("ペアリングを完了")
                                }
                            }
                        }
                    }
                } else {
                    item {
                        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                            Button(
                                onClick = { picker.launch(arrayOf("*/*")) },
                                modifier = Modifier.weight(1f),
                            ) {
                                Text("ファイルを選択")
                            }
                            OutlinedButton(
                                onClick = {
                                    scope.launch {
                                        try {
                                            val config = PairingStore.load(this@MainActivity) ?: return@launch
                                            val state = ApiClient(config).status()
                                            message = "PC状態を取得しました: ${state.optJSONObject("windows")?.optString("name", "Windows") ?: "未接続"}"
                                        } catch (e: Exception) {
                                            message = e.message ?: "状態確認に失敗しました"
                                        }
                                    }
                                },
                                modifier = Modifier.weight(1f),
                            ) {
                                Text("状態確認")
                            }
                        }
                    }
                }

                if (message.isNotBlank()) {
                    item {
                        Surface(
                            color = MaterialTheme.colorScheme.secondaryContainer,
                            shape = MaterialTheme.shapes.small,
                        ) {
                            Text(
                                message,
                                modifier = Modifier.padding(12.dp),
                                style = MaterialTheme.typography.bodyMedium,
                            )
                        }
                    }
                }

                item {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Row {
                            Text("転送", style = MaterialTheme.typography.titleLarge, modifier = Modifier.weight(1f))
                            Text(
                                "${queue.size}件",
                                style = MaterialTheme.typography.labelLarge,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                        HorizontalDivider()
                    }
                }

                if (queue.isEmpty()) {
                    item {
                        ElevatedCard {
                            Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                Text("転送待ちはありません", style = MaterialTheme.typography.titleMedium)
                                Text(
                                    if (paired) "ファイルを選択すると、ここに進捗が表示されます。"
                                    else "ペアリング後にファイルを選択できます。",
                                    style = MaterialTheme.typography.bodyMedium,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                        }
                    }
                } else {
                    items(queue, key = { it.id }) { item -> TransferCard(item) }
                }
            }
        }
    }

    @Composable
    private fun TransferCard(item: QueueItem) {
        val stateLabel = when (item.state) {
            "R2_READY" -> "PCへ到着待ち"
            "PC_SAVED" -> "PC保存完了"
            "UPLOADING" -> "送信中"
            "PREPARING" -> "準備中"
            "RETRYING" -> "再試行中"
            "FAILED" -> "失敗"
            "PAUSED" -> "一時停止"
            "WAITING_BATTERY" -> "充電待ち"
            "CANCELED", "CANCEL_PENDING" -> "キャンセル済み"
            else -> item.state
        }
        val stateColor = when (item.state) {
            "R2_READY", "PC_SAVED" -> MaterialTheme.colorScheme.tertiaryContainer
            "FAILED", "CANCELED", "CANCEL_PENDING" -> MaterialTheme.colorScheme.errorContainer
            "PAUSED", "WAITING_BATTERY" -> MaterialTheme.colorScheme.secondaryContainer
            else -> MaterialTheme.colorScheme.primaryContainer
        }

        ElevatedCard {
            Column(
                Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Row {
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Text(item.displayName, style = MaterialTheme.typography.titleMedium)
                        Text(
                            formatBytes(item.sizeBytes),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    Surface(
                        color = stateColor,
                        shape = MaterialTheme.shapes.small,
                    ) {
                        Text(
                            stateLabel,
                            modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp),
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.onSurface,
                        )
                    }
                }

                LinearProgressIndicator(
                    progress = { item.progress / 100f },
                    modifier = Modifier.fillMaxWidth(),
                )

                Row {
                    Text(
                        "${item.progress}%",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.weight(1f),
                    )
                    Text(
                        "ID ${item.id}",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.outline,
                    )
                }

                if (item.state == "RETRYING") {
                    Text(
                        "通信が一時的に中断されました。自動的に再試行しています。",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                } else if (!item.error.isNullOrBlank()) {
                    Text(
                        item.error,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                    )
                }

                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    when (item.state) {
                        "PAUSED", "RETRYING", "FAILED", "WAITING_BATTERY" -> FilledTonalButton(onClick = {
                            db.setState(item.id, "QUEUED")
                            schedule(item.id)
                        }) { Text("再開") }
                        "R2_READY", "PC_SAVED", "CANCELED", "CANCEL_PENDING" -> Unit
                        else -> OutlinedButton(onClick = { db.setState(item.id, "PAUSED") }) { Text("一時停止") }
                    }
                    if (item.state != "CANCELED" && item.state != "R2_READY" && item.state != "PC_SAVED" && item.state != "CANCEL_PENDING") {
                        OutlinedButton(onClick = {
                            WorkManager.getInstance(this@MainActivity).cancelUniqueWork(workName(item.id))
                            db.setState(item.id, "CANCEL_PENDING")
                            scheduleCancel(item.id)
                        }) { Text("キャンセル") }
                    }
                }
            }
        }
    }

    private suspend fun refreshCompletedTransfers(items: List<QueueItem>) {
        val config = PairingStore.load(this) ?: return
        val api = ApiClient(config)
        items.filter { it.state == "R2_READY" && !it.remoteId.isNullOrBlank() }.forEach { item ->
            try {
                val state = api.transferStatus(item.remoteId!!).optString("state")
                if (state == "DELETE_PENDING" || state == "COMPLETE") {
                    db.setState(item.id, "PC_SAVED")
                }
            } catch (_: Exception) {
                // A temporary status-check failure must not change the upload state.
            }
        }
    }

    private fun handleShareIntent(intent: Intent?) {
        if (intent == null) return
        val uris = mutableListOf<Uri>()
        when (intent.action) {
            Intent.ACTION_SEND -> streamExtra(intent)?.let(uris::add)
            Intent.ACTION_SEND_MULTIPLE -> streamExtras(intent).let(uris::addAll)
        }
        uris.forEach { uri ->
            try {
                if ((intent.flags and Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION) != 0) {
                    contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
                }
            } catch (_: Exception) { }
            enqueueUri(uri)
        }
    }

    private fun enqueueUri(uri: Uri) {
        lifecycleScope.launch(Dispatchers.IO) {
            val metadata = queryMetadata(uri)
            if (metadata.second !in 0..10_000_000_000L) return@launch
            val id = db.enqueue(uri.toString(), metadata.first, metadata.second, contentResolver.getType(uri))
            if (PairingStore.load(this@MainActivity) != null) schedule(id)
        }
    }

    private fun queryMetadata(uri: Uri): Pair<String, Long> {
        var name = uri.lastPathSegment ?: "file"
        var size = -1L
        contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { cursor ->
            if (cursor.moveToFirst()) {
                val nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                val sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE)
                if (nameIndex >= 0 && !cursor.isNull(nameIndex)) name = cursor.getString(nameIndex)
                if (sizeIndex >= 0 && !cursor.isNull(sizeIndex)) size = cursor.getLong(sizeIndex)
            }
        }
        if (size < 0) size = contentResolver.openAssetFileDescriptor(uri, "r")?.use { it.length } ?: -1L
        require(size >= 0) { "ファイルサイズを取得できません" }
        return name to size
    }

    private fun schedule(id: Long) {
        val request = OneTimeWorkRequestBuilder<UploadWorker>()
            .setInputData(Data.Builder().putLong("queueId", id).build())
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
            .build()
        WorkManager.getInstance(this).enqueueUniqueWork(workName(id), ExistingWorkPolicy.REPLACE, request)
    }

    private fun scheduleCancel(id: Long) {
        val request = OneTimeWorkRequestBuilder<CancelWorker>()
            .setInputData(Data.Builder().putLong("queueId", id).build())
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
            .build()
        WorkManager.getInstance(this).enqueueUniqueWork(cancelWorkName(id), ExistingWorkPolicy.REPLACE, request)
    }

    private fun workName(id: Long) = "cloudflarebox-upload-$id"
    private fun cancelWorkName(id: Long) = "cloudflarebox-cancel-$id"

    @Suppress("DEPRECATION")
    private fun streamExtra(intent: Intent): Uri? = if (Build.VERSION.SDK_INT >= 33) {
        intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java)
    } else {
        intent.getParcelableExtra(Intent.EXTRA_STREAM)
    }

    @Suppress("DEPRECATION")
    private fun streamExtras(intent: Intent): List<Uri> = if (Build.VERSION.SDK_INT >= 33) {
        intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri::class.java) ?: emptyList()
    } else {
        intent.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM) ?: emptyList()
    }

    private fun formatBytes(bytes: Long): String = when {
        bytes >= 1_000_000_000L -> "%.2f GB".format(bytes / 1_000_000_000.0)
        bytes >= 1_000_000L -> "%.1f MB".format(bytes / 1_000_000.0)
        bytes >= 1_000L -> "%.1f KB".format(bytes / 1_000.0)
        else -> "$bytes B"
    }
}
