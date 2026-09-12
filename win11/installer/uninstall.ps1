param(
    [switch]$RemoveData,
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'CloudflareBOX')
)

$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '管理者として PowerShell を実行してください。'
}

$service = Get-Service -Name 'CloudflareBOX' -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name 'CloudflareBOX' -Force }
    sc.exe delete CloudflareBOX | Out-Null
}
schtasks.exe /Delete /TN 'CloudflareBOX-Tray' /F 2>$null | Out-Null
Get-Process 'CloudflareBox.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path $InstallRoot) { Remove-Item $InstallRoot -Recurse -Force }
if ($RemoveData) {
    $data = Join-Path $env:ProgramData 'CloudflareBOX'
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
}

Write-Host 'CloudflareBOX の Windows 側をアンインストールしました。'
