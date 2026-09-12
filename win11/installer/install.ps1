param(
    [Parameter(Mandatory = $true)][string]$ApiBase,
    [Parameter(Mandatory = $true)][string]$SetupToken,
    [string]$Destination = (Join-Path $env:USERPROFILE 'CloudflareBOX'),
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'CloudflareBOX')
)

$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '管理者として PowerShell を実行してください。'
}

$serviceSource = Join-Path $PSScriptRoot 'service'
$traySource = Join-Path $PSScriptRoot 'tray'
if (-not (Test-Path $serviceSource) -or -not (Test-Path $traySource)) {
    throw 'install.ps1 と同じフォルダに service と tray が必要です。'
}

$serviceTarget = Join-Path $InstallRoot 'service'
$trayTarget = Join-Path $InstallRoot 'tray'
New-Item -ItemType Directory -Force $serviceTarget, $trayTarget, $Destination | Out-Null
Copy-Item (Join-Path $serviceSource '*') $serviceTarget -Recurse -Force
Copy-Item (Join-Path $traySource '*') $trayTarget -Recurse -Force

$serviceExe = Join-Path $serviceTarget 'CloudflareBox.Service.exe'
$trayExe = Join-Path $trayTarget 'CloudflareBox.Tray.exe'

$existing = Get-Service -Name 'CloudflareBOX' -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name 'CloudflareBOX' -Force }
    sc.exe delete CloudflareBOX | Out-Null
    Start-Sleep -Milliseconds 500
}

& $serviceExe --init $ApiBase $SetupToken $Destination
if ($LASTEXITCODE -ne 0) { throw "初期設定に失敗しました。終了コード: $LASTEXITCODE" }

New-Service -Name 'CloudflareBOX' -BinaryPathName ('"{0}" --service' -f $serviceExe) -DisplayName 'CloudflareBOX Receiver' -Description 'Receives encrypted CloudflareBOX files from Cloudflare R2.' -StartupType Automatic | Out-Null
sc.exe failure CloudflareBOX reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

$taskCommand = '"{0}"' -f $trayExe
schtasks.exe /Create /TN 'CloudflareBOX-Tray' /SC ONLOGON /TR $taskCommand /RL LIMITED /F | Out-Null
Start-Service -Name 'CloudflareBOX'
Start-Process $trayExe

Write-Host 'CloudflareBOX の Windows 側をインストールしました。Android でペアリングを完了してください。'
