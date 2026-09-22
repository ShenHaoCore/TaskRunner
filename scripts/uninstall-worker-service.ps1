$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "请以管理员身份运行此脚本。"
}

sc.exe stop TaskRunner.Worker | Out-Null
sc.exe delete TaskRunner.Worker
Write-Output "服务 TaskRunner.Worker 已删除。"
