package com.cloudflarebox.app

import android.content.Intent
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.ElevatedCard
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.launch

class QrPairingActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (PairingStore.load(this) != null) {
            openMain()
            return
        }
        setContent {
            CloudflareBoxTheme { Surface(Modifier.fillMaxSize()) { PairingScreen() } }
        }
    }

    @Composable
    private fun PairingScreen() {
        var deviceName by remember { mutableStateOf(Build.MODEL) }
        var status by remember { mutableStateOf("WindowsアプリでペアリングQRを表示してください。") }
        var busy by remember { mutableStateOf(false) }
        val scope = rememberCoroutineScope()
        val scanner = rememberLauncherForActivityResult(ScanContract()) { result ->
            val contents = result.contents ?: return@rememberLauncherForActivityResult
            busy = true
            status = "QRコードを読み取りました。Windows PCでこの端末を承認してください。"
            scope.launch {
                try {
                    val config = ApiClient.pair(contents, deviceName)
                    PairingStore.save(this@QrPairingActivity, config)
                    status = "ペアリングが完了しました。"
                    openMain()
                } catch (e: Exception) {
                    status = e.message ?: "ペアリングに失敗しました。"
                    busy = false
                }
            }
        }

        Column(
            Modifier.fillMaxSize().padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp),
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text("CFBox", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.SemiBold)
                Text("Windows PCとペアリング", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            ElevatedCard {
                Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text("QRコードで接続", style = MaterialTheme.typography.titleLarge)
                    Text("PCに表示されたQRコードを読み取ります。QRコードは5分間、1回だけ使用できます。")
                    OutlinedTextField(
                        value = deviceName,
                        onValueChange = { deviceName = it.take(80) },
                        modifier = Modifier.fillMaxWidth(),
                        label = { Text("端末名") },
                        singleLine = true,
                    )
                    Button(
                        onClick = {
                            scanner.launch(ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setPrompt("CFBoxのQRコードを読み取ってください").setBeepEnabled(false).setOrientationLocked(false))
                        },
                        enabled = !busy,
                        modifier = Modifier.fillMaxWidth(),
                    ) { Text("QRコードを読み取る") }
                    OutlinedButton(onClick = { openMain() }, enabled = !busy, modifier = Modifier.fillMaxWidth()) {
                        Text("JSONを手動入力")
                    }
                }
            }
            Surface(color = MaterialTheme.colorScheme.secondaryContainer, shape = MaterialTheme.shapes.medium) {
                Text(status, Modifier.padding(14.dp))
            }
        }
    }

    private fun openMain() {
        startActivity(Intent(this, MainActivity::class.java))
        finish()
    }
}
