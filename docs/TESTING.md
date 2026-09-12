# Verification plan

1. Unit: chunk framing, AES-GCM, metadata envelope, SHA-256, filename sanitization, budget prediction.
2. Worker: pairing, ECDSA auth, nonce replay rejection, role authorization, transfer state transitions, budget stop.
3. Android: picker/share intake, local queue persistence, WorkManager resume, mobile/Wi-Fi policy, cancellation.
4. Windows: download Range resume, RSA unwrap, streamed decrypt, hash verify, atomic final rename, duplicate handling.
5. E2E small: generated file -> Android encryption/upload -> Windows recovery -> byte-for-byte compare -> R2 delete.
6. Failure: kill Android/Windows mid-transfer, disconnect network, remove destination drive, expire URL, duplicate nonce.
7. Large: 9.5-10.0GB test file, interrupt at multiple positions, verify no full restart.
8. Billing guard: force synthetic usage to 89/90/95% and verify new uploads stop while GET/delete continue.
9. Revocation: revoke Android/PC and verify new signed requests fail.
