# CFBox 配布署名の設定

この手順は、GitHub Releaseから配布するCFBoxの署名設定です。`personal-v0.5`は対象外です。

## 1. AndroidのRelease署名鍵を作成

Windows PowerShellで、リポジトリの外にある安全なフォルダで実行します。

```powershell
$keyDir = Join-Path $env:USERPROFILE 'CFBox-secrets'
New-Item -ItemType Directory -Force $keyDir | Out-Null
keytool -genkeypair -v `
  -keystore (Join-Path $keyDir 'cfbox-release.keystore') `
  -alias cfbox-release `
  -keyalg RSA -keysize 4096 -validity 10000 `
  -storetype PKCS12 `
  -dname 'CN=CFBox, OU=Release, O=CFBox, C=JP'
```

表示されたパスワードは安全なパスワード管理ソフトに保存してください。キーストアとパスワードを失うと、同じアプリとして将来更新できません。

GitHub Actions Secretsには、次の値を登録します。

- `ANDROID_KEYSTORE_BASE64`: キーストアのBase64文字列
- `ANDROID_KEYSTORE_PASSWORD`: キーストアのパスワード
- `ANDROID_KEY_ALIAS`: `cfbox-release`
- `ANDROID_KEY_PASSWORD`: 鍵のパスワード

Base64文字列は次のPowerShellで表示できます。キーストア本体はチャットやリポジトリへ貼り付けません。

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $keyDir 'cfbox-release.keystore')))
```

登録場所は、GitHubリポジトリの
Settings → Secrets and variables → Actions → New repository secret
です。

## 2. Windowsのコード署名証明書

認証局から取得した公開コード署名証明書のPFXを用意します。自己署名証明書はテストには使えますが、一般利用者のSmartScreen警告を減らす目的には適しません。

GitHub Actions Secretsに次を登録します。

- `WINDOWS_SIGNING_PFX_BASE64`: PFXファイルのBase64文字列
- `WINDOWS_SIGNING_PFX_PASSWORD`: PFXのパスワード

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\path\to\cfbox-code-signing.pfx'))
```

## 3. 必要な公開設定

Windows配布パッケージの作成には、既存のRepository variable
`CLOUDFLAREBOX_OAUTH_CLIENT_ID`も必要です。これは秘密鍵ではなく、配布用OAuth Client IDです。

## 4. Releaseの作成

SecretsとRepository variableを登録した後、GitHubの
Actions → CFBox Release → Run workflow
を開き、タグに `v0.1.4` を指定して実行します。

成功すると、次のファイルを含むGitHub Releaseが作成されます。

- `CFBox-Android-v0.1.4.apk`
- `CFBox-Windows-v0.1.4.zip`
- `SHA256SUMS.txt`

ワークフローは署名情報が不足している場合に停止します。未署名のファイルを正式Releaseへ登録することはありません。
