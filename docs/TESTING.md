# Verification plan

## CI で毎回確認する範囲

Worker は TypeScript typecheck、Node test、bundle、Wrangler deploy dry-run を通します。Windows は Release build と Core tests を通します。Android は debug APK を assemble します。3系統の成功後に Windows 配布 ZIP を生成します。

Windows Core tests では AES-GCM metadata/frame round-trip、RSA-OAEP key wrap、RSA request signature、zero-byte frame、SHA-256、Windows filename sanitization を確認します。

## 本番リリース前に必要な E2E

CI は実 Cloudflare 資源を作成しないため、次の検証は別に行います。

1. 未設定 Windows へインストールし、service が終了せず `not_connected` で待機すること。
2. 「Cloudflareと連携」から OAuth login し、利用者アカウントに R2・D1・Worker が自動作成されること。
3. repair を複数回実行して同じ資源を再利用し、無関係な資源を変更しないこと。
4. Android を2台以上 pairing し、一方だけ失効して他方が継続利用できること。
5. 小容量、0 byte、複数ファイル、モバイル回線で Android -> Worker -> R2 -> Worker -> Windows が完了すること。
6. upload 中に Android 通信断・再起動、download 中に Windows 通信断・再起動を行い、途中から再開すること。
7. 復号後の平文 SHA-256 が完全一致した場合だけ正式保存し、5分後に R2 object が削除されること。
8. cancel 中にオフラインにして cleanup が再試行されること。
9. 約9.5-10GBのファイルで multipart part 数、Range resume、最終 hash、R2 cleanup を確認すること。
10. 無料枠安全値を 89%、90%、95% 相当にして、新規 upload だけ停止し、既存 download/delete が継続すること。
11. recovery bundle を別 Windows へ復元し、正しい recovery code では鍵が戻り、誤った code では復元できないこと。OAuth は再ログインになること。
12. pending transfer がある状態で unlink が拒否され、完了後は installation 専用 Worker/D1/R2 だけ削除されること。

本番 OAuth E2E、MSI、コード署名、正式 Android 署名は CI build 成功とは別のリリース証拠として記録します。
