# Cloudflare OAuth Public Client setup

この文書は CloudflareBOX の配布者が一度だけ行う設定です。利用者は Client ID、R2 Access Key、Secret、D1 ID、Worker 名を入力しません。

## 目的

CloudflareBOX Windows アプリから、利用者自身の Cloudflare アカウントへ Authorization Code + PKCE で安全に接続し、利用者が許可した1アカウントに CloudflareBOX 専用の R2・D1・Worker を自動構築できるようにします。

デスクトップ配布物に Client Secret は含めません。

## Public Client の登録値

Cloudflare OAuth Client は次の値で登録します。

- Client name: `CloudflareBOX`
- Response types: `code`
- Grant types: `authorization_code`, `refresh_token`
- Token endpoint authentication method: `none`
- Redirect URI: `http://127.0.0.1:53682/oauth/callback/`
- PKCE: `S256`
- Scopes:
  - `account.read`
  - `workers-scripts.write`
  - `workers-r2.write`
  - `d1.write`

`refresh_token` grant を有効にすると、Cloudflare 側が protocol scope の `offline_access` を client 設定へ自動的に反映します。CloudflareBOX は refresh token が返された場合に Windows DPAPI で保存し、access token の有効期限が近づくと自動更新します。

## Scope の用途

`account.read` は、OAuth で利用者が許可した Cloudflare アカウントの一覧取得と、複数アカウント利用者の構築先選択に使います。

`workers-scripts.write` は Worker script、workers.dev、Worker subdomain、cron の作成・更新・削除に使います。

`workers-r2.write` は CloudflareBOX installation 専用 R2 bucket の作成・確認・削除に使います。

`d1.write` は installation 専用 D1 database の作成、schema 適用、transfer 安全確認、削除に使います。

## Public 公開に必要な配布者情報

Cloudflare の Public OAuth Client として他ユーザーから利用可能にするには、Client name に加えて client URL、logo URL、必要な scope を設定し、publisher domain の所有確認を完了させます。

Client URL と logo URL は、配布者が実際に管理する HTTPS URL を使用します。所有していないドメインを設定しません。

Public への昇格は配布者側の公開操作です。公開前に redirect URI、scope、表示名、ロゴ、利用規約・プライバシーポリシー等の公開情報を最終確認します。

## GitHub Actions への Client ID 設定

Public Client 登録後に発行された公開 Client ID を、GitHub repository variable として次の名前で設定します。

`CLOUDFLAREBOX_OAUTH_CLIENT_ID`

これは秘密情報ではありません。Client Secret は repository variable / secret、ソースコード、Windows ZIP のいずれにも保存しません。

CI はこの variable が存在する場合だけ `oauth-client-id.txt` を Windows 配布 ZIP に含めます。Tray はその Client ID を使って「Cloudflareと連携」を開始します。

## 公開前の確認

Public Client を用いた実アカウント E2E では、最低でも次を確認します。

1. 新規利用者がブラウザだけで OAuth を完了できること。
2. Cloudflare アカウントが1件なら自動選択されること。
3. 複数アカウントなら Windows に選択画面が表示され、選択した1アカウントだけが変更されること。
4. R2・D1・Worker・schema・bindings・cron・workers.dev が自動構築されること。
5. access token の更新後も常駐受信を継続できること。
6. repair が同じ installation 資源を再利用すること。
7. unlink が未完了 transfer 中は拒否され、完了後は installation 専用資源だけを削除すること。

CI の build 成功だけでは、この Public OAuth E2E の代替にはなりません。
