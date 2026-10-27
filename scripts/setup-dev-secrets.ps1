# Sets local User Secrets for development (Api/Worker share UserSecretsId).
# Typical layout: PostgreSQL in WSL Ubuntu; Windows uses localhost relay → Host=127.0.0.1.
# Usage:
#   ./scripts/setup-dev-secrets.ps1 -VmHost 127.0.0.1 -Password "..."
#   ./scripts/setup-dev-secrets.ps1 -ConnectionString "Host=127.0.0.1;Port=5432;Database=taskrunner;Username=taskrunner;Password=..."
# Optional Docker-on-host default (port 15432) if -ConnectionString / -VmHost omitted.
param(
    [string]$ConnectionString,
    [string]$VmHost,
    [string]$Password = "TaskRunner!2026;",
    [int]$Port = 5432,
    [string]$Database = "taskrunner",
    [string]$Username = "taskrunner"
)

if (-not $ConnectionString) {
    if ($VmHost) {
        $pwd = $Password.TrimEnd(';')
        $ConnectionString = "Host=$VmHost;Port=$Port;Database=$Database;Username=$Username;Password=$pwd;"
    } else {
        $ConnectionString = "Host=127.0.0.1;Port=15432;Database=taskrunner;Username=taskrunner;Password=TaskRunner!2026;"
        Write-Output "No -VmHost/-ConnectionString: using Docker-on-host default (127.0.0.1:15432)."
        Write-Output "For PG inside WSL, use: -VmHost 127.0.0.1 -Password <pg-password>"
    }
}

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $root "src\TaskRunner.Api\TaskRunner.Api.csproj"

if (-not (Test-Path $apiProject)) {
    Write-Error "Api project not found: $apiProject"
}

Write-Output "Setting ConnectionStrings:Default for TaskRunner.Api (shared with Worker) ..."
dotnet user-secrets set "ConnectionStrings:Default" $ConnectionString --project $apiProject
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output "Done. Next:"
Write-Output "  dotnet run --project src/TaskRunner.Api"
Write-Output "Secrets stay on this machine only; do not commit them."
