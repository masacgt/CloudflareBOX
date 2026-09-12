# Cloudflare OAuth Public Client setup

この文書は CloudflareBOX の配布者が一度だけ行う設定です。利用者は Client ID、R2 Access Key、Secret、D1 ID、Worker 名を入力しません。

## 目的

CloudflareBOX Windows アプリから、利用者自身の Cloudflare アカウントへ Authorization Code + PKCE S256 で接続し、利用者が許可した1アカウントに CloudflareBOX 専用の R2・D1・Worker を自動構築できるようにします。

デスクトップ配布物に Client Secret は含めません。CloudflareBOX は public client として `token_endpoint_auth_method=none` を使います。

## OAuth Client の登録値

Cloudflare の現在の OAuth Client API で作成時に必要な値は次の6項目です。

- Client name: `CloudflareBOX`
- Response types: `code`
- Grant types: `authorization_code`, `refresh_token`
- Token endpoint authentication method: `none`
- Redirect URI: `http://127.0.0.1:53682/oauth/callback/`
- Scopes:
  - `account-settings.read`
  - `workers-scripts.write`
  - `workers-r2.write`
  - `d1.write`

画面上の表示名はAPIのIDと異なります。Cloudflareダッシュボードでは、`account-settings.read` は「Account Settings Read」、`workers-scripts.write` は「Workers」の最後にある小文字の「edit」、`workers-r2.write` は「Workers R2 Storage」の「Edit」、`d1.write` は「D1」の「Edit」に対応します。Workersの「Bind」や「Workers Editor」は今回の構成では選択しません。

API で作成する場合の内容は次の形です。

```json
{
  "client_name": "CloudflareBOX",
  "grant_types": ["authorization_code", "refresh_token"],
  "redirect_uris": ["http://127.0.0.1:53682/oauth/callback/"],
  "response_types": ["code"],
  "scopes": [
    "account-settings.read",
    "workers-scripts.write",
    "workers-r2.write",
    "d1.write"
  ],
  "token_endpoint_auth_method": "none"
}
```

PKCE S256 は Windows アプリ側が Authorization Code フロー開始時に使用します。Client Secret は使用しません。

`refresh_token` grant を有効にすると、Cloudflare 側が protocol scope の `offline_access` を client 設定へ自動的に反映します。CloudflareBOX は refresh token が返された場合に Windows DPAPI で保存し、access token の有効期限が近づくと自動更新します。

## OAuth Client 管理 API の権限と復旧

この節の API token は CloudflareBOX の配布者が OAuth Client を作成・更新するときだけ使います。通常利用者の Windows/Android には配布せず、Client Secret と同様にアプリや GitHub の公開ファイルへ入れません。

Cloudflare API で OAuth Client を管理する場合は、少なくとも次の API token permission が必要です。

- 一覧・詳細確認: `OAuth Client Read`
- 作成・更新・公開昇格・削除: `OAuth Client Write`

`GET /accounts/{account_id}/oauth_clients` で `10000: Authentication error` になる場合は、対象アカウントが違う、token が無効、または `OAuth Client Read` / `OAuth Client Write` が不足している状態を疑います。まず対象 account を確認し、OAuth Client 管理権限を付けた API token で一覧取得を再試行します。

Client の作成前に一覧取得を成功させておくと、誤った account へ重複作成する事故を避けやすくなります。既存の CloudflareBOX Client が見つかった場合は、その Client ID と設定を確認して更新・公開昇格を行い、同名 Client を増やしません。

## Scope の用途

`account-settings.read` は、OAuth で利用者が許可した Cloudflare アカウントの一覧取得と、複数アカウント利用者の構築先選択に使います。

`workers-scripts.write` は Worker script、workers.dev、Worker subdomain、cron の作成・更新・削除に使います。

`workers-r2.write` は CloudflareBOX installation 専用 R2 bucket の作成・確認・削除に使います。

`d1.write` は installation 専用 D1 database の作成、schema 適用、transfer 安全確認、削除に使います。

## Public 公開に必要な配布者情報

他ユーザーへ公開する場合は、配布者が実際に管理する HTTPS URL を使って次の情報を設定します。

- Client URL (`client_uri`)
- Logo URL (`logo_uri`)
- Privacy Policy URL (`policy_uri`)
- Terms of Service URL (`tos_uri`)

Cloudflare の OAuth Client API では、client URI のドメイン所有確認状態として `client_uri_verification` が返り、必要な場合は DNS TXT レコードの値が提示されます。所有していないドメインは設定しません。

OAuth Client の作成要求では `visibility` を指定しません。Client name、logo URI、所有確認済みの client URI host、少なくとも1つの非identity scope を満たした後、既存 Client に `PATCH /accounts/{account_id}/oauth_clients/{oauth_client_id}` で `{"visibility":"public"}` を送ると Public へ昇格できます。公開後は `visibility=public` と `promoted_at` を確認します。Private への降格はサポートされません。

公開前に redirect URI、scope、表示名、ロゴ、利用規約、プライバシーポリシー、publisher domain の所有確認を最終確認します。

## GitHub Actions への Client ID 設定

Public Client 登録後に発行された公開 Client ID を、GitHub repository variable として次の名前で設定します。

`CLOUDFLAREBOX_OAUTH_CLIENT_ID`

これは秘密情報ではありません。Client Secret は repository variable / secret、ソースコード、Windows ZIP のいずれにも保存しません。

現在の CI はこの variable を Windows 配布物の必須条件にしています。未設定または空の場合、`windows-package` は `Validate distribution OAuth client` で失敗し、配布 ZIP を生成しません。設定済みの場合は `oauth-client-id.txt` を ZIP に同梱し、そのファイルの存在確認後に成果物を作成します。

Windows インストーラーも `oauth-client-id.txt` が存在し、空でないことを確認してからインストールを開始します。したがって、利用者へ Client ID の手入力を求めません。

## 公開前の確認

Public Client を用いた実アカウント E2E では、最低でも次を確認します。

1. 新規利用者がブラウザだけで OAuth を完了できること。
2. Cloudflare アカウントが1件なら自動選択されること。
3. 複数アカウントなら Windows に選択画面が表示され、選択した1アカウントだけが変更されること。
4. R2・D1・Worker・schema・bindings・cron・workers.dev が自動構築されること。
5. access token の更新後も常駐受信を継続できること。
6. repair が同じ installation 資源を再利用すること。
7. unlink が未完了 transfer 中は拒否され、完了後は installation 専用資源だけを削除すること。
8. 2台目の Android を追加し、片方だけ revoke しても他方が継続利用できること。
9. 復旧 bundle と recovery code を使った新PC復旧後、Cloudflare を再連携して受信を再開できること。

CI の build 成功だけでは、この Public OAuth E2E の代替にはなりません。
