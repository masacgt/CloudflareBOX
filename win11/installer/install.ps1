param(
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'CloudflareBOX'),
    [string]$InstallUserSid = '',
    [string]$InstallUserName = '',
    [string]$InstallerRoot = ''
)

$ErrorActionPreference = 'Stop'

# ScriptBlock::Create does not reliably populate the automatic script path variables.
# Prefer the explicit path passed by the CMD entrypoint, then support direct .ps1 use.
$installerRoot = $InstallerRoot
if ([string]::IsNullOrWhiteSpace($installerRoot)) {
    $installerRoot = $PSScriptRoot
}
if ([string]::IsNullOrWhiteSpace($installerRoot)) {
    $installerRoot = $env:CLOUDFLAREBOX_INSTALLER_DIR
}
if ([string]::IsNullOrWhiteSpace($installerRoot)) {
    throw 'インストーラーの配置場所を確認できません。Install-CloudflareBOX.cmd から実行してください。'
}

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ([string]::IsNullOrWhiteSpace($InstallUserSid)) {
    $InstallUserSid = $currentIdentity.User.Value
}
if ([string]::IsNullOrWhiteSpace($InstallUserName)) {
    $InstallUserName = $currentIdentity.Name
}

$principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $scriptPath = $env:CLOUDFLAREBOX_INSTALL_SCRIPT
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        $scriptPath = $PSCommandPath
    }
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        throw 'install.ps1 の場所を確認できません。Install-CloudflareBOX.cmd から実行してください。'
    }
    $scriptPathLiteral = "'" + $scriptPath.Replace("'", "''") + "'"
    $installRootLiteral = "'" + $InstallRoot.Replace("'", "''") + "'"
    $installUserSidLiteral = "'" + $InstallUserSid.Replace("'", "''") + "'"
    $installUserNameLiteral = "'" + $InstallUserName.Replace("'", "''") + "'"
    $installerRootLiteral = "'" + $installerRoot.Replace("'", "''") + "'"
    $elevationCommand = '$script = [ScriptBlock]::Create((Get-Content -Raw -Encoding UTF8 -LiteralPath {0})); & $script -InstallRoot {1} -InstallUserSid {2} -InstallUserName {3} -InstallerRoot {4}' -f $scriptPathLiteral, $installRootLiteral, $installUserSidLiteral, $installUserNameLiteral, $installerRootLiteral
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($elevationCommand))
    $arguments = @(
        '-NoProfile'
        '-ExecutionPolicy'
        'Bypass'
        '-EncodedCommand'
        $encodedCommand
    )
    $elevated = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    exit $elevated.ExitCode
}

try {
    $installSid = [Security.Principal.SecurityIdentifier]::new($InstallUserSid)
}
catch {
    throw "インストール元ユーザーの SID を確認できません: $InstallUserSid"
}

$serviceSource = Join-Path $installerRoot 'service'
$traySource = Join-Path $installerRoot 'tray'
$workerSource = Join-Path $installerRoot 'worker'
$oauthClientFile = Join-Path $installerRoot 'oauth-client-id.txt'
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

# DPAPI(LocalMachine) のファイルを同じ PC の別ユーザーから読めないよう、
# ProgramData の親フォルダから継承された ACL を外して対象 principal だけを許可する。
$systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$administratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
$propagation = [Security.AccessControl.PropagationFlags]::None
$fullControl = [Security.AccessControl.FileSystemRights]::FullControl
$modify = [Security.AccessControl.FileSystemRights]::Modify
$allow = [Security.AccessControl.AccessControlType]::Allow
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($systemSid, $fullControl, $inheritance, $propagation, $allow))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($administratorsSid, $fullControl, $inheritance, $propagation, $allow))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($installSid, $modify, $inheritance, $propagation, $allow))
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
schtasks.exe /Create /TN 'CloudflareBOX-Tray' /SC ONLOGON /TR $taskCommand /RU $InstallUserName /IT /RL LIMITED /F | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "CloudflareBOX タスクトレイの登録に失敗しました: $InstallUserName"
}

Start-Service -Name 'CloudflareBOX'
schtasks.exe /Run /TN 'CloudflareBOX-Tray' | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Warning 'CloudflareBOX のトレイ起動は次回のユーザーログオン時に行われます。管理者権限のプロセスからは起動しません。'
}

Write-Host 'CloudflareBOX をインストールしました。タスクトレイの「Cloudflareと連携」を押し、Cloudflareへログインしてください。R2・D1・Workerは連携後に自動構築されます。'
