# Deploy the linux-x64 package into local WSL Ubuntu and run install.sh.
# Requires: WSL distro with systemd enabled (see docs/本地开发与虚拟机.md).
# Usage:
#   ./scripts/publish-linux.ps1
#   ./scripts/deploy-wsl.ps1
#   ./scripts/deploy-wsl.ps1 -Distro Ubuntu-24.04 -Package artifacts\taskrunner-3.3.1-linux-x64.tar.gz
param(
    [string]$Distro = "Ubuntu",
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

$wslCheck = wsl -d $Distro -- echo ok 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Cannot enter WSL distro '$Distro'. List with: wsl -l -v"
}

$wslPkg = (wsl -d $Distro -- wslpath -a ($packagePath -replace '\\', '/') 2>$null)
if (-not $wslPkg) {
    # Fallback: /mnt/<drive>/...
    $full = (Resolve-Path $packagePath).Path
    $drive = $full.Substring(0, 1).ToLowerInvariant()
    $rest = $full.Substring(2) -replace '\\', '/'
    $wslPkg = "/mnt/$drive$rest"
}
$wslPkg = ($wslPkg | Out-String).Trim()

Write-Output "Distro: $Distro"
Write-Output "Package (Windows): $packagePath"
Write-Output "Package (WSL): $wslPkg"
Write-Output "Extract + install (sudo may prompt in this window) ..."

$remoteDir = "/tmp/taskrunner-install"
$remoteTar = "/tmp/taskrunner-pkg.tar.gz"
# 9P(/mnt/c) 读取偶发陈旧内容：先复制进 WSL 本地文件系统并做 SHA256 校验，不一致重试一次，仍不一致则中止
$expectedHash = (Get-FileHash -Algorithm SHA256 -Path $packagePath).Hash.ToLowerInvariant()

$cmd = @"
set -e
rm -rf '$remoteDir' && mkdir -p '$remoteDir'
cp '$wslPkg' '$remoteTar'
actual=`$(sha256sum '$remoteTar' | cut -d' ' -f1)
if [ "`$actual" != "$expectedHash" ]; then
  sleep 1
  cp '$wslPkg' '$remoteTar'
  actual=`$(sha256sum '$remoteTar' | cut -d' ' -f1)
fi
if [ "`$actual" != "$expectedHash" ]; then
  echo "Package hash mismatch after copy: `$actual != $expectedHash" >&2
  exit 1
fi
tar -xzf '$remoteTar' -C '$remoteDir'
rm -f '$remoteTar'
sudo bash '$remoteDir/deploy/install.sh'
"@
wsl -d $Distro -- bash -lc $cmd
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output ""
Write-Output "If install asked for CHANGE_ME, inside WSL run:"
Write-Output "  wsl -d $Distro"
Write-Output "  sudo nano /etc/default/taskrunner"
Write-Output "  sudo systemctl start taskrunner-api taskrunner-worker"
Write-Output "PG + services are in WSL; Windows secrets should use Host=127.0.0.1 (localhost relay)."
