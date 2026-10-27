#!/usr/bin/env bash
# 为本机开发写入 User Secrets（Api/Worker 共用 UserSecretsId）。
# 常见布局：PostgreSQL 在 Ubuntu 虚拟机内，Host 填虚拟机 IP。
# 用法：
#   ./scripts/setup-dev-secrets.sh 'Host=192.168.56.10;Port=5432;Database=taskrunner;Username=taskrunner;Password=...'
#   ./scripts/setup-dev-secrets.sh --vm-host 192.168.56.10 --password '...'
# 无参数时回退到宿主机 Docker 示例（127.0.0.1:15432）。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_PROJECT="${ROOT}/src/TaskRunner.Api/TaskRunner.Api.csproj"
CONNECTION_STRING=""
VM_HOST=""
PASSWORD="TaskRunner!2026"
PORT=5432
DATABASE=taskrunner
USERNAME=taskrunner

while [[ $# -gt 0 ]]; do
  case "$1" in
    --vm-host) VM_HOST="$2"; shift 2 ;;
    --password) PASSWORD="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    -h|--help)
      echo "Usage: $0 'Host=...;...' | $0 --vm-host <ip> [--password <pwd>] [--port 5432]"
      exit 0
      ;;
    *)
      CONNECTION_STRING="$1"
      shift
      ;;
  esac
done

if [[ -z ${CONNECTION_STRING} ]]; then
  if [[ -n ${VM_HOST} ]]; then
    CONNECTION_STRING="Host=${VM_HOST};Port=${PORT};Database=${DATABASE};Username=${USERNAME};Password=${PASSWORD};"
  else
    CONNECTION_STRING="Host=127.0.0.1;Port=15432;Database=taskrunner;Username=taskrunner;Password=TaskRunner!2026;"
    echo "未指定连接串 / --vm-host：使用宿主机 Docker 默认（127.0.0.1:15432）。"
    echo "PG 在 Ubuntu VM 内时请用：--vm-host <虚拟机IP> --password <密码>"
  fi
fi

if [[ ! -f ${API_PROJECT} ]]; then
  echo "找不到 Api 项目：${API_PROJECT}" >&2
  exit 1
fi

echo "==> 写入 ConnectionStrings:Default（与 Worker 共享 UserSecretsId）"
dotnet user-secrets set "ConnectionStrings:Default" "${CONNECTION_STRING}" --project "${API_PROJECT}"

cat <<'EOF'
完成。下一步（先开着装有 PG 的虚拟机）：
  dotnet run --project src/TaskRunner.Api
Secrets 只存在本机，勿提交到仓库。
EOF
