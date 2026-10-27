#!/usr/bin/env bash
# 对 PostgreSQL 执行 EF 迁移（连接串来自环境变量 / User Secrets）。
# 用法：
#   ./scripts/ef-database-update.sh
#   ConnectionStrings__Default='Host=127.0.0.1;...' ./scripts/ef-database-update.sh
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
echo "==> dotnet ef database update (Core <- Api startup)"
dotnet ef database update \
  --project "${ROOT}/src/TaskRunner.Core/TaskRunner.Core.csproj" \
  --startup-project "${ROOT}/src/TaskRunner.Api/TaskRunner.Api.csproj"
echo "完成。空库也可依赖 Api 启动时的 MigrateAsync。"
