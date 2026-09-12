# Architecture

```text
Android
  | signed control API
  v
Cloudflare Worker ---- D1 (state/device/public keys/history)
  |       |
  |       +---- R2 control/cleanup
  |
  +---- presigned UploadPart / GET URLs
             |
             v
        private R2 bucket
             |
             v
Windows Service ---- local IPC ---- Tray UI
```

Control API never proxies file bodies. Android encrypts locally. Windows decrypts only after download.

Transfer state machine:

`PREPARING -> UPLOADING -> R2_READY -> PC_DOWNLOADING -> VERIFYING -> DELETE_PENDING -> COMPLETE`

Terminal/side states: `PAUSED`, `FAILED`, `CANCELED`.
