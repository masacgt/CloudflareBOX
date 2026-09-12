# CloudflareBOX 統合仕様書 v2

## 目的

Android から利用者自身の Cloudflare を一時中継にして、自宅の Windows 11 PC へ任意ファイルを送る一方向転送システムです。Windows は1台、Android は複数台を登録できます。1ファイル上限は 10,000,000,000 bytes です。

## 初期設定

Windows のインストール後、タスクトレイの「Cloudflareと連携」から Authorization Code + PKCE で利用者自身の Cloudflare アカウントへ接続します。アプリはインストール ID を基準に private R2、D1、Worker、D1 schema、R2/D1 binding、cron、workers.dev 設定を自動作成・修復します。利用者は R2 Access Key、Secret、D1 ID、Worker URL、setup token を入力しません。

修復は同じインストール専用資源だけを対象にします。連携解除は転送中データがないことを確認し、そのインストール専用の Worker、D1、R2 だけを削除します。アカウント共通の workers.dev サブドメインは他用途へ影響し得るため削除しません。

## 転送と再開

暗号化前の Part は 16MiB 固定です。Android は Worker の R2 binding 経由で R2 Multipart Upload を行い、完了済み Part を記録して通信断、アプリ終了、端末再起動後も途中から再開します。Windows は Worker の content API に `Range: bytes=<offset>-` を送り、暗号化一時ファイルの続きから取得します。Android/Windows は R2 S3 API へ直接接続せず、R2 Access Key / Secret は不要です。

キャンセル要求が通信断で完了しない場合は再試行可能な状態を保持します。モバイル回線でも転送できます。PC では元フォルダ構造を再現せず、固定保存先へ保存します。

## 暗号化

ファイルごとに32 byteのランダム鍵を生成し、各 Part を AES-256-GCM で独立暗号化します。Part は `version(1) | nonce(12) | ciphertext | tag(16)` です。ファイル鍵は Windows の RSA-3072 公開鍵へ RSA-OAEP-SHA256 でラップします。元ファイル名、URI、MIME、日時、平文 SHA-256 等は暗号化メタデータとして保持します。Cloudflare に平文ファイル名・本文・端末秘密鍵は保存しません。

Windows は復号後のファイル全体 SHA-256 が Android 側の値と完全一致した場合だけ正式保存します。同名・同内容は重複保存せず、同名・別内容は別名保存します。保存成功報告後5分待って R2 object を削除し、削除成功後を最終完了とします。

## 端末認証と複数 Android

Android は Android Keystore に RSA-2048 署名鍵を持ちます。Windows も端末署名鍵を持ちます。API は device ID、timestamp、nonce、SHA-256 body hash、RSA-SHA256 PKCS#1 v1.5 署名で認証し、nonce の再利用を拒否します。

各 Android は独立した device ID と署名鍵を持ち、Windows から個別に失効できます。追加 Android pairing は登録済み Windows の署名済み要求から開始します。

## Windows サービス

Windows は .NET サービスとタスクトレイ UI で構成します。サービスは Windows 起動時に開始し、Cloudflare 未連携なら終了せず `not_connected` で待機します。設定は受信周期ごとに読み直し、初回連携、修復、連携解除を再インストールなしで反映します。

## 復旧

Windows の署名秘密鍵・復号秘密鍵を失うと既存暗号化データを復号できません。そこで recovery bundle を利用者の recovery code で AES-GCM 保護して出力します。Cloudflare OAuth token は bundle に含めません。新しい PC では bundle を復元後、Cloudflare へ再ログインします。

## 無料枠保護

R2 Standard の storage、Class A、Class B を設定値として扱い、安全上限の90%予測で新規 upload を停止します。既存データの回収と削除は継続します。公開前には Cloudflare の現行無料枠を確認します。

## 配布

GitHub Actions で Worker bundle、Android debug APK、Windows ZIP を生成します。Windows ZIP は service、tray、Worker bundle、install/uninstall script を含みます。公開配布前に Cloudflare OAuth Public Client を登録し、Public Client ID を配布物へ同梱します。PKCE の公開クライアントとして Client Secret は同梱しません。

MSI、Windows コード署名、Android 正式署名・Play 配布、自動更新は正式リリース工程として残します。本番 Cloudflare へのデプロイと実ユーザー OAuth E2E は、ソースの CI 成功とは別に検証します。

## 正式リリース前の E2E

新規 Cloudflare 環境で OAuth 接続から自動構築、複数 Android、通信断、Android/Windows 再起動、途中再開、約10GB転送、SHA-256 一致、R2 削除、無料枠停止、端末失効、repair/unlink、recovery 移行まで確認します。
