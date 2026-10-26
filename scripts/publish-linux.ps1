param(
    # Publish framework-dependent instead of self-contained single-file; the target machine then needs the .NET 9 runtime.
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

# Keep this script ASCII-only: PowerShell 5.1 decodes BOM-less UTF-8 scripts using the system ANSI codepage,
# which corrupts non-ASCII comments and can swallow the following line into a comment.
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\linux"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

$version = "dev"
$tag = git -C $root describe --tags --abbrev=0 2>$null
if ($LASTEXITCODE -eq 0 -and $tag) { $version = ([string]$tag).TrimStart("v") }

$selfContained = if ($FrameworkDependent) { "false" } else { "true" }
$singleFile = if ($FrameworkDependent) { "false" } else { "true" }

foreach ($project in @("TaskRunner.Api", "TaskRunner.Worker")) {
    $projectOut = Join-Path $out ($project -replace "^TaskRunner\.", "").ToLower()
    Write-Output "Publishing $project (linux-x64, self-contained=$selfContained) to $projectOut ..."
    dotnet publish (Join-Path $root "src\$project\$project.csproj") -c Release -r linux-x64 --self-contained $selfContained -p:PublishSingleFile=$singleFile -o $projectOut
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Copy-Item (Join-Path $root "deploy\linux") (Join-Path $out "deploy") -Recurse -Force

# Pack with the Windows built-in bsdtar: GNU tar on PATH treats "G:\..." as a remote host name.
# Start-Process + stderr to file: PS 5.1 promotes stderr lines to terminating errors under
# $ErrorActionPreference = "Stop"; bsdtar warns on files changing while reading (AV scans fresh
# publish output), so retry once before giving up.
$tar = Join-Path $env:SystemRoot "System32\tar.exe"
if (-not (Test-Path $tar)) { $tar = "tar" }
$packageAbs = Join-Path $root "artifacts\taskrunner-$version-linux-x64.tar.gz"
$srcAbs = Join-Path $root "artifacts\linux"
$tarErrLog = Join-Path $root "artifacts\tar-stderr.log"
$attempt = 0
do {
    $attempt++
    if (Test-Path $packageAbs) { Remove-Item -Force $packageAbs }
    $tarProc = Start-Process -FilePath $tar -ArgumentList @("-czf", $packageAbs, "-C", $srcAbs, ".") -NoNewWindow -PassThru -Wait -RedirectStandardError $tarErrLog
    $tarCode = $tarProc.ExitCode
    if ($tarCode -ne 0) {
        Write-Output "tar attempt $attempt failed:"
        Get-Content $tarErrLog | Write-Output
    }
} while ($tarCode -ne 0 -and $attempt -lt 2)
if ($tarCode -ne 0) { exit $tarCode }
$package = $packageAbs

$size = "{0:N1} MB" -f ((Get-Item $package).Length / 1MB)
Write-Output ""
Write-Output "Package ready: $package ($size)"
Write-Output "Deploy to Ubuntu:"
Write-Output "  scp $package user@server:/tmp/"
Write-Output "  ssh user@server `"mkdir -p /tmp/taskrunner && tar -xzf /tmp/taskrunner-$version-linux-x64.tar.gz -C /tmp/taskrunner && sudo bash /tmp/taskrunner/deploy/install.sh`""
