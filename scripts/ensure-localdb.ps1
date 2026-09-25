$ErrorActionPreference = "Stop"

sqllocaldb start mssqllocaldb | Out-Null

$sql = @"
IF DB_ID(N'TaskRunner') IS NULL
    CREATE DATABASE TaskRunner;
"@

sqlcmd -S "(localdb)\mssqllocaldb" -E -Q $sql
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output "LocalDB ready: TaskRunner (Hangfire 表位于 HangFire schema，与业务表同库)。"
