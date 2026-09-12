# CloudflareBOX 統合仕様書 v1

## 1. 目的

Android端末から任意ファイルをCloudflare R2へ暗号化して一時アップロードし、自宅Windows 11 PCが自動回収する。一方向転送のみ。利用者、Android、Windows PCは各1台を基本とする。

## 2. 転送

1ファイル上限は10,000,000,000 bytes。複数ファイル・フォルダ・Android共有メニューから送信可能。フォルダ構造はPCでは再現しない。R2 Multipart Uploadを利用し、ファイルサイズに応じ64/128/256MiBを基準にPartサイズを自動選択する。通信断・アプリ終了・Android/Windows再起動後に再開する。

## 3. 暗号化

平文をCloudflareへ送らない。ファイルごとに32byteのランダム鍵を生成し、各PartをAES-256-GCMで独立暗号化する。Part形式は `version(1) | nonce(12) | ciphertext | tag(16)`。ファイル鍵はWindowsのRSA-3072公開鍵へRSA-OAEP-SHA256でラップする。元ファイル名・元URI・作成/更新日時・MIME・平文SHA-256は暗号化メタデータに格納する。R2キーはランダムUUID。秘密鍵はCloudflareへ保存しない。

## 4. 完了判定

PCは暗号化オブジェクトを一時保存して再開可能にする。復号平文は一時ファイルへ出力し、SHA-256が暗号化メタデータ内の値と完全一致した場合だけ正式名へ移動する。同名・同内容は再保存せず、同名・別内容は `name (1).ext` 形式にする。成功通知後5分待ってR2を削除し、R2削除成功後に最終完了とする。

## 5. Android

Kotlin + Jetpack Compose。WorkManagerを利用。バックグラウンド、画面消灯、再起動後再開、モバイル回線対応。転送一覧では進捗、速度、残り時間、一時停止、再開、キャンセルを扱う。電池15%未満では新規転送を一時停止する。端末認証によるアプリロックは付けない。

## 6. Windows

C# + .NET。WindowsサービスとタスクトレイUIを分離する。保存先は変更可能な固定フォルダ。必要容量+20GBの余裕がない場合は新規取得を止める。保存先ドライブ消失時は一時停止し、復帰後に再開する。

## 7. Cloudflare

Workersを認証・制御面、D1を転送状態の正本、R2 Standardを暗号化データの一時置場とする。ファイル本体はWorkerを通過させない。WorkerはMultipart開始、UploadPart署名URL、CompleteMultipart、GET署名URLを発行する。R2は非公開1バケット。

## 8. 認証

端末ごとにP-256署名鍵を生成し、公開鍵だけD1へ保存する。API要求はHTTPS + ECDSA-SHA256署名 + timestamp + nonceで認証し、nonce再利用を拒否する。Windowsは別途RSA-3072暗号鍵を生成し、公開鍵をAndroidへペアリング時に渡す。

## 9. ペアリング

Windowsが先に開始し、Workerからpairing IDと6桁コードを取得する。QRにはWorker URL、pairing ID、コード、Windows device ID、Windows署名公開鍵、Windows RSA公開鍵を含める。Androidで読み取り、同じ6桁コードを表示確認して完了する。初回は小容量テストファイルでE2E確認する。

## 10. 無料枠

課金回避を最優先する。R2 Standard無料枠を設定値として持ち、storage GB-month予測、Class A/B操作数をアプリ内部で集計する。90%予測時点で新規アップロードを止めるが、PC回収と削除は継続する。無料枠値はコード定数に固定せず設定可能にする。

## 11. 履歴

Android 90日、Windows 1年、Cloudflare 30日。Cloudflareへ平文ファイル名・元パスを保存しない。Windowsローカルは元名、保存名、日時、検証結果を保持できる。

## 12. 管理

`workers.dev` の `/admin` をCloudflare Accessで保護する。状態、端末失効/再発行、R2利用量、待機/進行中/直近30日履歴を表示する。秘密鍵は表示しない。削除はD1状態を再確認し安全条件を満たす場合のみ許可する。

## 13. 配布・更新

Androidは当初APK手動配布、将来Play対応。WindowsはMSIでサービス登録し、更新通知後にユーザー操作で更新する。互換性がない場合だけ転送を停止する。

## 14. 正式完成条件

10GB近い実データで、暗号化、Multipart、通信断、中断再開、Android再起動、Windows再起動、PCオフライン、復号、SHA-256一致、正式保存、R2削除、無料枠停止、端末失効まで通す。
