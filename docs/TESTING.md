# Verification plan

## CI で毎回確認する範囲

Worker は TypeScript typecheck、Node test、bundle、Wrangler deploy dry-run を通します。Windows は Release build と Core tests を通します。Android は debug APK を assemble します。これらが成功した後、Windows 配布 ZIP を生成します。

Windows Core tests では少なくとも次を確認します。

- AES-GCM metadata/frame round-trip
- RSA-OAEP key wrap
- RSA request signature
- zero-byte frame
- SHA-256
- Windows filename sanitization
- Range download の正常 resume
- `206 Content-Range` の開始位置検証
- `Content-Range` の全体サイズ検証
- 残り `Content-Length` 検証
- resume offset が不正な応答の拒否

Windows package job では `install.ps1` と `uninstall.ps1` の PowerShell 構文を解析し、Service と Tray を `win-x64 --self-contained true` で publish します。Worker artifact を組み込み、次の必須ファイルを確認してから `CloudflareBOX-Windows.zip` を作成します。

- `service/CloudflareBox.Service.exe`
- `service/coreclr.dll`
- `tray/CloudflareBox.Tray.exe`
- `tray/coreclr.dll`
- `worker/cloudflarebox-worker.mjs`
- `install.ps1`
- `uninstall.ps1`
- `Install-CloudflareBOX.cmd`
- `START-HERE.txt`

## 直近の確認済み CI

2026-09-12 の GitHub Actions run `34673821422`、commit `f2a8637affc788bfd1bb07e8ac6bca42fafd6492` では `worker`、`windows`、`android`、`windows-package` の4 job がすべて成功しました。

同 run の `cloudflarebox-windows` artifact は 88,294,621 bytes で、package assemble の必須ファイル確認を通過しています。成果物として `cloudflarebox-worker`、`cloudflarebox-android-debug`、`cloudflarebox-windows` の3 artifact が生成されています。

## 本番リリース前に必要な E2E

CI は実 Cloudflare 資源を作成しないため、次の検証は別に行います。

1. 未設定 Windows で `Install-CloudflareBOX.cmd` を実行し、UAC 後に Service と Tray が導入されること。
2. .NET Runtime を別途導入していない Windows 11 でも self-contained Service/Tray が起動すること。
3. Cloudflare 未連携でも Service が終了せず待機すること。
4. 「Cloudflareと連携」から OAuth login し、利用者アカウントに R2・D1・Worker・schema・bindings・cron が自動構築されること。
5. repair を複数回実行して同じ installation 資源を再利用し、無関係な資源を変更しないこと。
6. Android を2台以上 pairing し、一方だけ失効して他方が継続利用できること。
7. 小容量、0 byte、複数ファイル、モバイル回線で Android -> Worker -> R2 -> Worker -> Windows が完了すること。
8. upload 中に Android 通信断・再起動、download 中に Windows 通信断・再起動を行い、途中から再開すること。
9. Range resume 時にサーバーの開始位置や全体サイズが食い違う場合、Windows が追記せず安全に失敗すること。
10. 復号後の平文 SHA-256 が完全一致した場合だけ正式保存し、5分後に R2 object が削除されること。
11. cancel 中にオフラインにして cleanup が再試行されること。
12. 約9.5-10GBのファイルで multipart part 数、Range resume、最終 hash、R2 cleanup を確認すること。
13. 無料枠安全値を 89%、90%、95% 相当にして、新規 upload だけ停止し、既存 download/delete が継続すること。
14. recovery bundle を別 Windows へ復元し、正しい recovery code では鍵が戻り、誤った code では復元できないこと。OAuth は再ログインになること。
15. pending transfer がある状態で unlink が拒否され、完了後は installation 専用 Worker/D1/R2 だけ削除されること。

本番 OAuth E2E、MSI、Windows コード署名、正式 Android 署名は CI build 成功とは別のリリース証拠として記録します。
