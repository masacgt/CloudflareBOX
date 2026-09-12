# Verification plan

## CI で毎回確認する範囲

Worker は TypeScript typecheck、Node の実テスト、bundle、Wrangler deploy dry-run を通します。Windows は Release build と Core tests を通します。Android は debug APK を assemble します。3系統の成功後に Windows 配布 ZIP を生成します。

Windows Core tests では AES-GCM metadata/frame round-trip、RSA-OAEP key wrap、RSA request signature、zero-byte frame、SHA-256、Windows filename sanitization に加えて、Range download の正常な途中再開と不正な `Content-Range` の拒否を確認します。不正な再開応答では既存の部分ファイルを変更しないことも確認します。

Windows package job は `install.ps1` / `uninstall.ps1` の PowerShell 構文を解析し、service/tray を `win-x64 --self-contained true` で publish します。その後 `CloudflareBox.Service.exe`、`CloudflareBox.Tray.exe`、両側の `coreclr.dll`、Worker bundle、installer、`Install-CloudflareBOX.cmd`、`START-HERE.txt` が揃っている場合だけ ZIP を作成します。

## 本番リリース前に必要な E2E

CI は実 Cloudflare 資源を作成しないため、次の検証は別に行います。

1. .NET Runtime を入れていない Windows 11 で ZIP を展開し、`Install-CloudflareBOX.cmd` から UAC を経てインストールできること。
2. 未設定 Windows で service が終了せず Cloudflare 連携待ちになること。
3. 「Cloudflareと連携」から OAuth login し、利用者アカウントに R2・D1・Worker・workers.dev 設定が自動作成されること。
4. repair を複数回実行して同じ資源を再利用し、無関係な資源を変更しないこと。
5. Android を2台以上 pairing し、一方だけ失効して他方が継続利用できること。
6. 小容量、0 byte、複数ファイル、モバイル回線で Android -> Worker -> R2 -> Worker -> Windows が完了すること。
7. upload 中に Android 通信断・再起動、download 中に Windows 通信断・再起動を行い、正しい `Content-Range` で途中から再開すること。
8. 復号後の平文 SHA-256 が完全一致した場合だけ正式保存し、5分後に R2 object が削除されること。
9. cancel 中にオフラインにして cleanup が再試行されること。
10. 約9.5-10GBのファイルで multipart part 数、Range resume、最終 hash、R2 cleanup を確認すること。
11. 無料枠安全値を 89%、90%、95% 相当にして、新規 upload だけ停止し、既存 download/delete が継続すること。
12. recovery bundle を別 Windows へ復元し、正しい recovery code では鍵が戻り、誤った code では復元できないこと。OAuth は再ログインになること。
13. pending transfer がある状態で unlink が拒否され、完了後は installation 専用 Worker/D1/R2 だけ削除されること。

本番 OAuth E2E、Windows コード署名、MSI、正式 Android 署名は CI build 成功とは別のリリース証拠として記録します。
