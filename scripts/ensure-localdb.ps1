$ErrorActionPreference = "Stop"

sqllocaldb start mssqllocaldb | Out-Null

$sql = @"
IF DB_ID(N'TaskRunner') IS NULL
    CREATE DATABASE TaskRunner;
IF DB_ID(N'TaskRunner_Hangfire') IS NULL
    CREATE DATABASE TaskRunner_Hangfire;
"@

sqlcmd -S "(localdb)\mssqllocaldb" -E -Q $sql
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output "LocalDB ready: TaskRunner, TaskRunner_Hangfire."
