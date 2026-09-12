# Architecture

```text
Android 1..N
  | HTTPS + device RSA signature
  | encrypted 16MiB parts
  v
Cloudflare Worker -------- D1
  |  R2 binding             | devices / pairing / transfer state / usage
  v                         |
private R2 <----------------+
  |
  | Worker content API + Range resume
  v
Windows 11 Service ---- local files/history
        |
        +---- Tray UI
```

Android と Windows は R2 の S3 API へ直接接続しません。Worker が R2 binding を使って upload、download、delete を行うため、利用者へ R2 Access Key / Secret を配布しません。Cloudflare には暗号化済み本文と暗号化メタデータだけを置きます。

Windows の「Cloudflareと連携」は OAuth Authorization Code + PKCE を開始し、利用者自身の Cloudflare アカウント内にインストール専用 R2・D1・Worker を作成します。repair は同じ installation ID の資源を再利用し、不足・構成差分を直します。unlink は未完了 transfer がない場合だけインストール専用資源を削除します。

Windows service は設定がない状態でも常駐し、`not_connected` で待機します。設定を受信周期ごとに読み直すため、Cloudflare 接続後にサービスを再登録する必要はありません。

主な transfer state は `UPLOADING -> R2_READY -> PC_DOWNLOADING -> DELETE_PENDING -> COMPLETE` です。キャンセル cleanup は `CANCELING` を経由し、Android 側は通信断時に `CANCEL_PENDING` を保持します。

ファイル鍵はファイルごとの AES-256-GCM 鍵で、Windows RSA-3072 公開鍵に RSA-OAEP-SHA256 でラップします。Windows が平文 SHA-256 の完全一致を確認して正式保存した後だけ削除待ちへ進みます。
