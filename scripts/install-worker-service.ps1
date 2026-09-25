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
    Write-Error "Missing appsettings.json under publish output. Configure ConnectionStrings before install."
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
Write-Output "Before starting, set the production connection string, for example:"
Write-Output "  [Environment]::SetEnvironmentVariable('ConnectionStrings__Default', 'Server=...;Database=TaskRunner;...', 'Machine')"
Write-Output "  [Environment]::SetEnvironmentVariable('Hangfire__Storage', 'SqlServer', 'Machine')"
Write-Output "  [Environment]::SetEnvironmentVariable('Hangfire__ReadOnlyMode', 'false', 'Machine')"
Write-Output "Or edit: $appsettings"
Write-Output ""

sc.exe start TaskRunner.Worker
if ($LASTEXITCODE -ne 0) {
    Write-Error "Service created but failed to start. Check Event Viewer and $out\logs (or solution logs/worker)."
}

Write-Output "Service TaskRunner.Worker installed and started. Startup type=Automatic; restart 5s after failure."
