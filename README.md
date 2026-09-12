# CloudflareBOX

Android から利用者自身の Cloudflare を一時中継にして、自宅の Windows 11 PC へ暗号化ファイルを自動転送するシステムです。

転送方向は Android -> Cloudflare Worker/R2 -> Windows 11 の一方向です。1ファイル上限は 10,000,000,000 bytes、R2 は非公開です。Android 側で AES-256-GCM 暗号化し、ファイル鍵は Windows の RSA-3072 公開鍵へ RSA-OAEP-SHA256 でラップします。Windows が復号後のファイル全体 SHA-256 を照合し、完全一致して正式保存できた場合だけ、5分後に R2 から削除します。

ファイル本体は署名付き R2 URL を利用しません。Android と Windows は端末署名付き HTTPS で Worker に接続し、Worker の R2 binding を通して 16 MiB 単位の multipart upload と Range download を行います。R2 Access Key / Secret は利用者にもアプリにも不要です。

1台の Windows PC に複数の Android を登録できます。Android ごとに独立した device ID と署名鍵を持ち、個別に失効できます。アップロード・ダウンロード・キャンセルは通信断や再起動後に再開できます。

## 利用者向けセットアップ

Windows 配布 ZIP は .NET ランタイム込みの self-contained 形式です。利用者が .NET を別途導入する必要はありません。

1. `CloudflareBOX-Windows.zip` を展開します。
2. `Install-CloudflareBOX.cmd` をダブルクリックします。
3. Windows の UAC が表示されたら許可します。
4. インストール後、タスクトレイの CloudflareBOX を開き、「Cloudflareと連携」を押します。
5. ブラウザで利用者自身の Cloudflare アカウントへログインし、CloudflareBOX に必要な権限を許可します。
6. OAuth で複数の Cloudflare アカウントが利用可能な場合だけ、Windows にアカウント選択画面が表示されます。R2・D1・Worker を作成する1アカウントを選択します。
7. CloudflareBOX が、そのインストール専用の R2・D1・Worker、schema、binding、cron と必要な workers.dev 設定を自動作成または修復します。
8. Windows の「Androidを追加」からペアリング情報を発行し、Android アプリへ登録します。2台目以降も同じ操作で追加できます。

Windows service はインストール直後から起動し、Cloudflare 未連携の間は待機します。複数アカウントの選択待ちになった場合も終了せず待機します。選択とCloudflare構築が完了すると共有設定を読み込み、再インストールせず受信を開始します。

Cloudflare ダッシュボードで R2 や D1 を手作業することは通常ありません。修復処理は同じ installation ID を基準に繰り返し実行でき、連携解除ではそのインストール専用の Worker・D1・R2 だけを削除します。未完了転送がある場合は削除を拒否し、アカウント共通の workers.dev サブドメインは削除しません。

## 復旧

Windows の署名鍵・復号鍵を失うと、Cloudflare 上の暗号化済みファイルは復号できません。そのため、Windows から暗号化された復旧ファイルを出力できます。復旧ファイルは利用者の復旧コードで保護し、Cloudflare OAuth トークンは含めません。新しい PC では復旧ファイルを読み込んだ後、Cloudflare へ再ログインします。

## 配布 ZIP の内容

GitHub Actions の `cloudflarebox-windows` artifact には、次を含む `CloudflareBOX-Windows.zip` が入ります。

- `service/`: Windows 常駐 service と self-contained .NET runtime
- `tray/`: タスクトレイ UI と self-contained .NET runtime
- `worker/cloudflarebox-worker.mjs`: Cloudflare Worker bundle
- `install.ps1`: 実際のインストール処理
- `uninstall.ps1`: 管理者 PowerShell から実行するアンインストール処理
- `Install-CloudflareBOX.cmd`: 利用者向けの通常インストール入口
- `START-HERE.txt`: 配布 ZIP 内の導入案内
- `oauth-client-id.txt`: `CLOUDFLAREBOX_OAUTH_CLIENT_ID` が設定されている場合だけ同梱

CI では Service/Tray の実行ファイルと `coreclr.dll`、Worker bundle、各 installer ファイルの存在を確認してから ZIP を生成します。

## CI

GitHub Actions は次を検証・生成します。

- Worker: TypeScript typecheck、Node test、bundle、Wrangler deploy dry-run
- Windows: Release build、Core tests
- Android: debug APK build
- Windows package: installer PowerShell 構文確認、Service/Tray の `win-x64` self-contained publish、Worker bundle 組み込み、必須ファイル確認、ZIP 作成

Windows Core tests では暗号処理に加え、Range 再開時の `206 Content-Range`、開始位置、全体サイズ、残り長さを検証し、不正な resume 応答を拒否します。

## 公開配布前に必要なもの

Cloudflare OAuth の Public Client を正式登録し、デスクトップ向け Authorization Code + PKCE（S256）、token endpoint authentication `none`、loopback redirect `http://127.0.0.1:53682/oauth/callback/` を設定する必要があります。

OAuth Client には少なくとも次の scope を登録します。

- `account.read`: OAuth で許可された Cloudflare アカウントの取得と選択
- `workers-scripts.write`: Worker、workers.dev、cron の作成・更新・削除
- `workers-r2.write`: installation 専用 R2 bucket の作成・確認・削除
- `d1.write`: installation 専用 D1 の作成・schema 適用・確認・削除

公開 Client ID は GitHub repository variable `CLOUDFLAREBOX_OAUTH_CLIENT_ID` に設定します。Client Secret はデスクトップ配布物へ同梱しません。

現時点では、実ユーザーの Cloudflare アカウントを使った OAuth 自動構築の本番 E2E、MSI 化、Windows コード署名、Android 正式署名・Play 配布は未完了です。CI 成功だけを公開配布可能の根拠にはしません。

詳細仕様は `SPEC.md`、構成・API・導入・試験は `docs/` を参照してください。
