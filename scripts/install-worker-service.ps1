$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Please run this script as Administrator."
}

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\worker"
Write-Output "Publishing Worker to $out ..."
dotnet publish (Join-Path $root "src\TaskRunner.Worker\TaskRunner.Worker.csproj") -c Release -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $out "TaskRunner.Worker.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Executable not found: $exe"
}

$appsettings = Join-Path $out "appsettings.json"
if (-not (Test-Path $appsettings)) {
    Write-Error "Missing appsettings.json under publish output (shared defaults). Set ConnectionStrings__Default via machine environment variables before start."
}

Write-Output "Stopping existing service if present..."
sc.exe stop TaskRunner.Worker 2>$null | Out-Null
sc.exe delete TaskRunner.Worker 2>$null | Out-Null
Start-Sleep -Seconds 2

$binPath = '"{0}"' -f $exe
Write-Output "Creating service TaskRunner.Worker ..."
sc.exe create TaskRunner.Worker binPath= $binPath start= auto DisplayName= "TaskRunner Worker"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

sc.exe description TaskRunner.Worker "TaskRunner Hangfire worker (Server role)"
sc.exe failure TaskRunner.Worker reset= 86400 actions= restart/5000/restart/5000/restart/5000

Write-Output ""
Write-Output "Before starting, set machine-level environment variables (do NOT edit $appsettings — upgrades overwrite it):"
Write-Output "  [Environment]::SetEnvironmentVariable('ConnectionStrings__Default', 'Host=127.0.0.1;Port=5432;Database=taskrunner;Username=taskrunner;Password=...', 'Machine')"
Write-Output "  # optional Worker knobs: Hangfire__WorkerCount, Hangfire__Queues__0=..."
Write-Output "AdminApiKey / ReadOnlyMode belong on the Api host, not this Worker service."
Write-Output "Same rule as Linux: one publish package for all hosts; per-machine secrets live only in env vars."
Write-Output ""

sc.exe start TaskRunner.Worker
if ($LASTEXITCODE -ne 0) {
    Write-Error "Service created but failed to start. Check Event Viewer and machine env (ConnectionStrings__Default). Logs: $out\logs (or solution logs/)."
}

Write-Output "Service TaskRunner.Worker installed and started. Startup type=Automatic; restart 5s after failure."
