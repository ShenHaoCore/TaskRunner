# Apply EF Core migrations to PostgreSQL (ConnectionStrings from env / User Secrets).
# Usage:
#   ./scripts/ef-database-update.ps1
#   $env:ConnectionStrings__Default = "Host=127.0.0.1;..."; ./scripts/ef-database-update.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$core = Join-Path $root "src\TaskRunner.Core\TaskRunner.Core.csproj"
$api = Join-Path $root "src\TaskRunner.Api\TaskRunner.Api.csproj"

Write-Output "dotnet ef database update (Core <- Api startup) ..."
dotnet ef database update --project $core --startup-project $api
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "Done. Empty DB can also rely on Api startup MigrateAsync."
