# CloudflareBOX API

All `/api/v1/*` endpoints except pairing bootstrap and health require device request signatures.

Signed headers:

- `X-CB-Device-Id`
- `X-CB-Timestamp` Unix seconds
- `X-CB-Nonce` random UUID
- `X-CB-Signature` base64 DER ECDSA P-256/SHA-256

Canonical payload:

`METHOD\nPATH_AND_QUERY\nSHA256_HEX(BODY)\nTIMESTAMP\nNONCE`

Main routes:

- `POST /api/v1/pairing/start`
- `POST /api/v1/pairing/complete`
- `GET /api/v1/pairing/status`
- `GET /api/v1/status`
- `POST /api/v1/transfers`
- `POST /api/v1/transfers/:id/parts/urls`
- `POST /api/v1/transfers/:id/parts/report`
- `POST /api/v1/transfers/:id/complete-upload`
- `GET /api/v1/transfers/pending`
- `POST /api/v1/transfers/:id/download-url`
- `POST /api/v1/transfers/:id/pc-complete`
- `POST /api/v1/transfers/:id/cancel`
- `POST /api/v1/status/probe`
- `GET /api/v1/status/probe/:id`
- `POST /api/v1/pc/commands/poll`
- `POST /api/v1/pc/commands/:id/ack`
- `GET /admin`
