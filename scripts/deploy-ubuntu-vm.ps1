# Upload the linux-x64 package to an Ubuntu host/VM and run install.sh.
# Usage:
#   ./scripts/publish-linux.ps1
#   ./scripts/deploy-ubuntu-vm.ps1 -Target user@192.168.56.10
#   ./scripts/deploy-ubuntu-vm.ps1 -Target user@vm -Package artifacts\taskrunner-3.3.0-linux-x64.tar.gz
param(
    [Parameter(Mandatory = $true)]
    [string]$Target,

    [string]$Package
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if ($Package) {
    $packagePath = if ([System.IO.Path]::IsPathRooted($Package)) { $Package } else { Join-Path $root $Package }
} else {
    $packagePath = Get-ChildItem (Join-Path $root "artifacts\taskrunner-*-linux-x64.tar.gz") |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $packagePath -or -not (Test-Path $packagePath)) {
    Write-Error "Package not found. Run ./scripts/publish-linux.ps1 first."
}

$basename = Split-Path -Leaf $packagePath
$remoteTar = "/tmp/$basename"
$remoteDir = "/tmp/taskrunner-install"

Write-Output "Uploading $packagePath -> ${Target}:$remoteTar ..."
$localSha = (Get-FileHash -Algorithm SHA256 $packagePath).Hash.ToLowerInvariant()
scp $packagePath "${Target}:$remoteTar"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 上传完整性校验（与 deploy-ubuntu-vm.sh 对齐）：远端 sha256sum 对账，不一致重试一次
Write-Output "Verifying upload integrity (SHA256) ..."
$remoteSha = (ssh $Target "sha256sum '$remoteTar' | cut -d' ' -f1")
if ($LASTEXITCODE -ne 0) { $remoteSha = $null }
if ($remoteSha -ne $localSha) {
    Write-Output "Hash mismatch, retrying upload once ..."
    scp $packagePath "${Target}:$remoteTar"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $remoteSha = (ssh $Target "sha256sum '$remoteTar' | cut -d' ' -f1")
    if ($LASTEXITCODE -ne 0) { $remoteSha = $null }
}
if ($remoteSha -ne $localSha) {
    Write-Error "Package hash mismatch: remote '$remoteSha' != local '$localSha'. Aborting install."
    exit 1
}
Write-Output "SHA256 verified ($($localSha.Substring(0, 12))...)."

$remoteCmd = "rm -rf '$remoteDir' && mkdir -p '$remoteDir' && tar -xzf '$remoteTar' -C '$remoteDir' && sudo bash '$remoteDir/deploy/install.sh'"
Write-Output "Remote extract + install (sudo required) ..."
ssh -t $Target $remoteCmd
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output ""
Write-Output "If install asked for CHANGE_ME:"
Write-Output "  ssh $Target"
Write-Output "  sudo nano /etc/default/taskrunner      # ConnectionStrings__Default"
Write-Output "  sudo nano /etc/default/taskrunner-api  # Hangfire__AdminApiKey"
Write-Output "  sudo systemctl start taskrunner-api taskrunner-worker"
Write-Output "Do not edit /opt/taskrunner/**/appsettings.json"
