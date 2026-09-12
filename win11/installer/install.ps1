param(
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'CloudflareBOX')
)

$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -InstallRoot `"$InstallRoot`""
    $elevated = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    exit $elevated.ExitCode
}

$serviceSource = Join-Path $PSScriptRoot 'service'
$traySource = Join-Path $PSScriptRoot 'tray'
$workerSource = Join-Path $PSScriptRoot 'worker'
$oauthClientFile = Join-Path $PSScriptRoot 'oauth-client-id.txt'
if (-not (Test-Path $serviceSource) -or -not (Test-Path $traySource) -or -not (Test-Path (Join-Path $workerSource 'cloudflarebox-worker.mjs'))) {
    throw 'install.ps1 と同じフォルダに service、tray、worker/cloudflarebox-worker.mjs が必要です。'
}
if (-not (Test-Path $oauthClientFile)) {
    throw '配布物が不完全です。oauth-client-id.txt がありません。CloudflareBOX の正式な Windows 配布 ZIP を使用してください。'
}
$oauthClientId = (Get-Content -Path $oauthClientFile -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($oauthClientId)) {
    throw '配布物が不完全です。oauth-client-id.txt が空です。CloudflareBOX の正式な Windows 配布 ZIP を使用してください。'
}

$serviceTarget = Join-Path $InstallRoot 'service'
$trayTarget = Join-Path $InstallRoot 'tray'
$workerTarget = Join-Path $serviceTarget 'worker'
New-Item -ItemType Directory -Force $serviceTarget, $trayTarget, $workerTarget | Out-Null
Copy-Item (Join-Path $serviceSource '*') $serviceTarget -Recurse -Force
Copy-Item (Join-Path $traySource '*') $trayTarget -Recurse -Force
Copy-Item (Join-Path $workerSource 'cloudflarebox-worker.mjs') $workerTarget -Force
Copy-Item $oauthClientFile (Join-Path $serviceTarget 'oauth-client-id.txt') -Force
Copy-Item $oauthClientFile (Join-Path $trayTarget 'oauth-client-id.txt') -Force

$serviceExe = Join-Path $serviceTarget 'CloudflareBox.Service.exe'
$trayExe = Join-Path $trayTarget 'CloudflareBox.Tray.exe'
if (-not (Test-Path $serviceExe) -or -not (Test-Path $trayExe)) {
    throw 'CloudflareBOX の実行ファイルが配布物に含まれていません。'
}

$dataRoot = Join-Path $env:ProgramData 'CloudflareBOX'
New-Item -ItemType Directory -Force $dataRoot | Out-Null
$currentUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
$rule = [Security.AccessControl.FileSystemAccessRule]::new(
    $currentUserSid,
    [Security.AccessControl.FileSystemRights]::Modify,
    $inheritance,
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow
)
$acl = Get-Acl $dataRoot
$acl.SetAccessRule($rule)
Set-Acl -Path $dataRoot -AclObject $acl

$existing = Get-Service -Name 'CloudflareBOX' -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name 'CloudflareBOX' -Force }
    sc.exe delete CloudflareBOX | Out-Null
    Start-Sleep -Milliseconds 500
}

New-Service -Name 'CloudflareBOX' -BinaryPathName ('"{0}" --service' -f $serviceExe) -DisplayName 'CloudflareBOX Receiver' -Description 'Receives encrypted CloudflareBOX files through the user owned Cloudflare Worker and R2.' -StartupType Automatic | Out-Null
sc.exe failure CloudflareBOX reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

$taskCommand = '"{0}"' -f $trayExe
schtasks.exe /Create /TN 'CloudflareBOX-Tray' /SC ONLOGON /TR $taskCommand /RL LIMITED /F | Out-Null

Start-Service -Name 'CloudflareBOX'
Start-Process $trayExe
Write-Host 'CloudflareBOX をインストールしました。タスクトレイの「Cloudflareと連携」を押し、Cloudflareへログインしてください。R2・D1・Workerは連携後に自動構築されます。'
