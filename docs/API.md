# CloudflareBOX API

`/health` と初回 pairing bootstrap を除く `/api/v1/*` は端末署名を要求します。

署名ヘッダーは `X-CB-Device-Id`、`X-CB-Timestamp`、`X-CB-Nonce`、`X-CB-Signature` です。署名方式は RSA PKCS#1 v1.5 + SHA-256、正規化対象は `METHOD\nPATH_AND_QUERY\nSHA256_HEX(BODY)\nTIMESTAMP\nNONCE` です。Worker は timestamp の許容範囲と nonce 再利用を検証します。

主な route:

- `GET /health`
- `POST /api/v1/pairing/start` 初回 Windows pairing 開始
- `POST /api/v1/pairing/complete` Android pairing 完了
- `GET /api/v1/pairing/status`
- `GET /api/v1/status`
- `POST /api/v1/status/probe`
- `GET /api/v1/status/probe/:id`
- `GET /api/v1/diagnostics` Windows 診断
- `GET /api/v1/devices` 登録端末一覧
- `POST /api/v1/devices/pairing` 追加 Android pairing 開始
- `POST /api/v1/devices/:id/revoke` Android 個別失効
- `POST /api/v1/transfers` transfer 作成
- `PUT /api/v1/transfers/:id/parts/:partNumber` 暗号化 Part を Worker 経由で R2 へ保存。`X-CB-Part-Sha256` を検証
- `POST /api/v1/transfers/:id/complete-upload` R2 multipart 完了
- `GET /api/v1/transfers/pending` Windows の未回収 transfer 一覧
- `GET /api/v1/transfers/:id/content` Worker 経由の暗号化本文取得。`Range: bytes=<offset>-` に対応
- `POST /api/v1/transfers/:id/pc-complete` Windows の平文 SHA-256 検証・正式保存完了報告
- `POST /api/v1/transfers/:id/cancel` cancel/cleanup 開始
- `POST /api/v1/pc/commands/poll`
- `POST /api/v1/pc/commands/:id/ack`
- `GET /admin`

Android/Windows 向け API は R2 の署名付き S3 URL を返しません。ファイル本体も Worker API で端末認証したうえで R2 binding へ接続します。
