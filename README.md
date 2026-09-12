# CloudflareBOX

Android から利用者自身の Cloudflare を一時中継にして、自宅の Windows 11 PC へ暗号化ファイルを自動転送するシステムです。

転送方向は Android -> Cloudflare Worker/R2 -> Windows 11 の一方向です。1ファイル上限は 10,000,000,000 bytes、R2 は非公開です。Android 側で AES-256-GCM 暗号化し、ファイル鍵は Windows の RSA-3072 公開鍵へ RSA-OAEP-SHA256 でラップします。Windows が復号後のファイル全体 SHA-256 を照合し、完全一致して正式保存できた場合だけ、5分後に R2 から削除します。

ファイル本体は署名付き R2 URL を利用しません。Android と Windows は端末署名付き HTTPS で Worker に接続し、Worker の R2 binding を通して 16MiB 単位の multipart upload と Range download を行います。R2 Access Key / Secret は利用者にもアプリにも不要です。

1台の Windows PC に複数の Android を登録できます。Android ごとに独立した device ID と署名鍵を持ち、個別に失効できます。アップロード・ダウンロード・キャンセルは通信断や再起動後に再開できます。

## 利用者向けセットアップ

想定する配布版では次の順序です。

1. Windows 11 に CloudflareBOX をインストールします。
2. タスクトレイの「Cloudflareと連携」を押します。
3. ブラウザで利用者自身の Cloudflare アカウントへログインして許可します。
4. CloudflareBOX がそのインストール専用の R2・D1・Worker と必要な workers.dev 設定を自動作成または修復します。
5. Android アプリをインストールし、Windows が発行したペアリング情報で登録します。
6. 2台目以降の Android は「Androidを追加」から登録します。

Cloudflare ダッシュボードで R2 や D1 を手作業することは通常ありません。修復処理は同じインストール ID を基準に繰り返し実行でき、連携解除ではそのインストール専用の Worker・D1・R2 だけを削除します。転送中データがある場合は削除を拒否します。アカウント共通の workers.dev サブドメインは他の Worker でも利用され得るため削除しません。

## 復旧

Windows の署名鍵・復号鍵を失うと、Cloudflare 上の暗号化済みファイルは復号できません。そのため、Windows から暗号化された復旧ファイルを出力できます。復旧ファイルは利用者の復旧コードで保護し、Cloudflare OAuth トークンは含めません。新しい PC では復旧ファイルを読み込んだ後、Cloudflare へ再ログインします。

## 構成

- `cloudflare/`: Worker API、D1 schema、R2 一時保存・cleanup
- `android/`: Kotlin / Jetpack Compose / WorkManager Android アプリ
- `win11/`: .NET Windows サービス、トレイ UI、PowerShell インストーラー
- `docs/`: API、構成、導入、検証方針
- `.github/workflows/ci.yml`: Worker、Windows、Android の検証と配布用 artifact 作成

GitHub Actions は Worker bundle、Android debug APK、Windows ZIP パッケージを生成します。Windows ZIP には `service`、`tray`、`worker`、`install.ps1`、`uninstall.ps1` を含めます。

## 公開配布前に必要なもの

Cloudflare OAuth の Public Client を正式登録し、その Client ID を GitHub repository variable `CLOUDFLAREBOX_OAUTH_CLIENT_ID` に設定する必要があります。Client Secret は PKCE のデスクトップ公開クライアントには同梱しません。

現時点では実ユーザーの Cloudflare アカウントを使った OAuth 自動構築の本番 E2E、MSI 化、Windows コード署名、Android 正式署名・Play 配布は未完了です。ソースのビルド成功と本番配布可能性は分けて扱います。

詳細仕様は `SPEC.md`、構成・API・導入・試験は `docs/` を参照してください。
