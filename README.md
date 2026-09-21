# CFBox

Androidスマートフォンから、自分のCloudflareアカウントを中継して、自宅のWindows 11 PCへファイルを送る一方向転送システムです。

## 現在の仕様

- Android → Cloudflare Worker/R2 → Windows 11 の一方向転送
- Androidのみ対応
- 1ファイル上限は10,000,000,000 bytes（10 GB）
- あらゆるファイル形式に対応
- R2バケットは非公開
- Android側でAES-256-GCM暗号化
- ファイル鍵はWindowsのRSA-3072公開鍵でRSA-OAEP-SHA256により保護
- Windowsでファイル全体のSHA-256を照合し、完全一致した場合だけ正式保存
- 保存確認後、暗号化データをR2から削除
- 通信断、アプリ再起動、PC再起動後の転送再開に対応
- Wi-Fiとモバイル回線のどちらでも転送可能
- 1台のWindows PCに複数のAndroid端末を登録可能
- Android端末ごとに個別の端末ID・署名鍵を持ち、個別に失効可能
- フォルダ構造は転送せず、Windowsの固定保存先へ保存
- Windowsは常駐サービスとタスクトレイUIで動作
- R2 Access KeyやSecretの手入力は不要

ファイル本体は署名付きR2 URLを利用しません。AndroidとWindowsは端末署名付きHTTPSでWorkerに接続し、WorkerのR2 bindingを通して16 MiB単位のmultipart uploadとRange downloadを行います。

## 配布版

最新の配布版はGitHub Releaseの[v0.1.4](https://github.com/masacgt/CloudflareBOX/releases/tag/v0.1.4)です。

- [Android APK](https://github.com/masacgt/CloudflareBOX/releases/download/v0.1.4/CFBox-Android-v0.1.4.apk)
- Windows 11は`CFBox-Setup-<version>.exe`を通常配布用とし、ZIPは検証・復旧用として併記します。`v0.1.4`へEXEを追加する場合はRelease workflowを再実行します。
- [Windows 11 ZIP（検証・復旧用）](https://github.com/masacgt/CloudflareBOX/releases/download/v0.1.4/CFBox-Windows-v0.1.4.zip)
- [SHA256SUMS.txt](https://github.com/masacgt/CloudflareBOX/releases/download/v0.1.4/SHA256SUMS.txt)

Android APKはRelease署名済みです。署名鍵は将来の更新に必要なため、配布者が安全に保管します。

WindowsのEXEとZIP内バイナリは現在テスト用の自己署名コード署名です。そのため、Windows SmartScreenや証明書に関する警告が表示されます。一般配布で警告を減らすには、認証局のコード署名証明書へ切り替える必要があります。

このリポジトリが非公開の場合、Releaseのダウンロードにはリポジトリへのアクセス権が必要です。

## 利用者向けセットアップ

Windows配布は.NETランタイム込みのself-contained形式です。利用者が.NETを別途インストールする必要はありません。通常は`CFBox-Setup-<version>.exe`を使用します。ZIPは検証・復旧用です。

1. `CFBox-Setup-<version>.exe`をダブルクリックします。
2. Windowsのユーザーアカウント制御が表示されたら「はい」を選びます。
3. セットアップ完了後、タスクトレイのCFBoxを開きます。
4. 「Cloudflareと連携」を押します。
5. ブラウザで利用者自身のCloudflareアカウントへログインし、必要な権限を許可します。
6. Cloudflareアカウントが複数ある場合は、CFBoxで使用するアカウントを選択します。
7. Cloudflareとの構築が完了したら、Windowsの「Androidを追加」を押します。
8. Windowsに表示されたQRコードをAndroidアプリで読み取り、ペアリングを承認します。
9. Androidアプリで「ファイルを選択」から転送します。

ZIPを使用する場合だけ、`CFBox-Windows-<version>.zip`を展開して`Install-CloudflareBOX.cmd`を実行します。

CloudflareダッシュボードでR2・D1・Workerを手作業で作成する必要はありません。CFBoxがインストール専用のR2・D1・Worker、D1スキーマ、binding、cron、workers.dev設定を自動作成・更新します。

利用者が入力する必要がないものは、R2 Access Key、R2 Secret、R2バケット名、D1データベースID、Worker名、API URLです。

## Android端末を追加する

1台目と同じ手順で、Windowsの「Androidを追加」から端末ごとのQRコードを発行します。

複数のAndroid端末を登録できます。端末ごとに独立した認証情報を持つため、不要になった端末だけを失効できます。

## 復旧

Windowsの署名鍵・復号鍵を失うと、Cloudflare上の暗号化済みファイルを復号できません。

Windowsアプリの「復旧情報を保存」で、暗号化された復旧ファイルを安全な場所へ保存してください。復旧ファイルは復旧コードで保護され、Cloudflare OAuthトークンは含みません。

新しいWindows PCでは、復旧情報を読み込んだ後にCloudflareへ再連携します。

## Cloudflare連携に必要なもの

利用者自身のCloudflareアカウントが必要です。CFBoxはOAuthで連携し、必要なリソースをそのアカウント内に構築します。

OAuth Clientは配布者が用意し、Windows配布物の`oauth-client-id.txt`としてEXEまたはZIP内に公開Client IDを同梱します。Client Secretは配布物へ同梱しません。

必要なOAuth scopeは次のとおりです。

- `account-settings.read`: 利用可能なCloudflareアカウントの取得と選択
- `workers-scripts.write`: Worker、workers.dev、cronの作成・更新・削除
- `workers-r2.write`: 専用R2バケットの作成・確認・削除
- `d1.write`: 専用D1の作成・スキーマ適用・確認・削除

## Cloudflareリソースの削除

Cloudflare連携を解除すると、そのインストール専用のWorker・D1・R2だけを削除します。

未完了転送がある場合は削除を拒否します。アカウント共通のworkers.devサブドメインは削除しません。

## Windows配布物の内容

通常配布用EXEは、次のZIP相当の内容を1本の`CFBox-Setup.exe`にまとめます。ZIP版も検証・復旧用として残します。

- `service/`: Windows常駐サービスとself-contained .NET runtime
- `tray/`: タスクトレイUIとself-contained .NET runtime
- `worker/cloudflarebox-worker.mjs`: Cloudflare Worker bundle
- `install.ps1`: インストール処理
- `uninstall.ps1`: アンインストール処理
- `Install-CloudflareBOX.cmd`: 通常インストール入口
- `START-HERE.txt`: 導入案内
- `oauth-client-id.txt`: 配布用OAuth Client ID

## GitHub Actions

[CFBox Release workflow](https://github.com/masacgt/CloudflareBOX/actions/workflows/release.yml)は、手動実行で次を行います。

- Workerのtypecheck、テスト、bundle
- Android Release署名APKの作成
- Windows self-containedアプリのpublish
- WindowsバイナリのPFX署名
- Windows `CFBox-Setup.exe`と検証・復旧用ZIPの作成
- SHA-256ファイルの作成
- GitHub ReleaseへのAPK・EXE・ZIP・SHA-256の登録

Releaseワークフローには次のActions Secretsが必要です。

- `ANDROID_KEYSTORE_BASE64`
- `ANDROID_KEYSTORE_PASSWORD`
- `ANDROID_KEY_ALIAS`
- `ANDROID_KEY_PASSWORD`
- `WINDOWS_SIGNING_PFX_BASE64`
- `WINDOWS_SIGNING_PFX_PASSWORD`

Windows配布にはRepository variable `CLOUDFLAREBOX_OAUTH_CLIENT_ID`も必要です。

署名設定の詳細は[`docs/RELEASE_SIGNING.md`](docs/RELEASE_SIGNING.md)を参照してください。

## 自分用の固定版

自分用のテスト環境を残すため、[`personal-v0.5`ブランチ](https://github.com/masacgt/CloudflareBOX/tree/personal-v0.5)を固定しています。

配布版の変更はこのブランチへ反映しません。

## 開発資料

- [SPEC.md](SPEC.md): 詳細仕様
- [docs/](docs/): 構成、API、導入、テスト、署名設定
