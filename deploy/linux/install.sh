#!/usr/bin/env bash
# TaskRunner Ubuntu 服务器端安装脚本。
# 用法：解压发布包（含 api/ worker/ deploy/）后以 root 运行：
#   sudo bash ./deploy/install.sh
# 可用环境变量覆盖：
#   TASKRUNNER_INSTALL_ROOT   安装目录（默认 /opt/taskrunner）
#   TASKRUNNER_SERVICE_TYPE   systemd Type（默认 notify；配旧版产物时用 simple）
set -euo pipefail

if [[ ${EUID} -ne 0 ]]; then
  echo "请以 root 运行：sudo $0" >&2
  exit 1
fi

INSTALL_ROOT="${TASKRUNNER_INSTALL_ROOT:-/opt/taskrunner}"
SERVICE_TYPE="${TASKRUNNER_SERVICE_TYPE:-notify}"
SERVICE_USER=taskrunner
SHARED_ENV=/etc/default/taskrunner
API_ENV=/etc/default/taskrunner-api
WORKER_ENV=/etc/default/taskrunner-worker

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PKG_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

for app in api worker; do
  if [[ ! -d ${PKG_ROOT}/${app} ]]; then
    echo "缺少 ${PKG_ROOT}/${app} 目录，请在发布包根目录运行本脚本。" >&2
    exit 1
  fi
done

# 产物形态：自包含单文件（TaskRunner.Api）或框架依赖（TaskRunner.Api.dll，需目标机装有 .NET 9）
api_exec="${INSTALL_ROOT}/api/TaskRunner.Api"
worker_exec="${INSTALL_ROOT}/worker/TaskRunner.Worker"
if [[ ! -f ${PKG_ROOT}/api/TaskRunner.Api ]]; then
  command -v dotnet >/dev/null || { echo "包为框架依赖形态但目标机没有 dotnet：apt install -y dotnet-sdk-9.0" >&2; exit 1; }
  api_exec="/usr/bin/dotnet ${INSTALL_ROOT}/api/TaskRunner.Api.dll"
  worker_exec="/usr/bin/dotnet ${INSTALL_ROOT}/worker/TaskRunner.Worker.dll"
fi

echo "==> 创建系统用户 ${SERVICE_USER}"
id -u "${SERVICE_USER}" >/dev/null 2>&1 \
  || useradd --system --home-dir "${INSTALL_ROOT}" --no-create-home --shell /usr/sbin/nologin "${SERVICE_USER}"

echo "==> 停止运行中的服务（如有）"
systemctl stop taskrunner-api taskrunner-worker 2>/dev/null || true

echo "==> 安装程序文件到 ${INSTALL_ROOT}"
mkdir -p "${INSTALL_ROOT}"
rm -rf "${INSTALL_ROOT}/api" "${INSTALL_ROOT}/worker"
cp -a "${PKG_ROOT}/api" "${PKG_ROOT}/worker" "${INSTALL_ROOT}/"
chown -R "${SERVICE_USER}:" "${INSTALL_ROOT}"
chmod -R u=rwX,go=rX "${INSTALL_ROOT}"
# Windows 打包的 tar 不保留可执行位，单文件宿主需要手动补上
for exe in "${INSTALL_ROOT}/api/TaskRunner.Api" "${INSTALL_ROOT}/worker/TaskRunner.Worker"; do
  [[ -f ${exe} ]] && chmod 755 "${exe}"
done

echo "==> 安装 systemd 单元（Type=${SERVICE_TYPE}）"
sed -e "s|^Type=.*|Type=${SERVICE_TYPE}|" -e "s|^ExecStart=.*|ExecStart=${api_exec}|" \
  "${SCRIPT_DIR}/taskrunner-api.service" > /etc/systemd/system/taskrunner-api.service
sed -e "s|^Type=.*|Type=${SERVICE_TYPE}|" -e "s|^ExecStart=.*|ExecStart=${worker_exec}|" \
  "${SCRIPT_DIR}/taskrunner-worker.service" > /etc/systemd/system/taskrunner-worker.service
chmod 644 /etc/systemd/system/taskrunner-api.service /etc/systemd/system/taskrunner-worker.service
systemctl daemon-reload

NEED_CONFIG=0

# 旧版单文件含 Api/Worker 键时，拆到角色文件并只保留共享连接串。
migrate_monolith_env() {
  [[ -f ${SHARED_ENV} ]] || return 0
  [[ -f ${API_ENV} && -f ${WORKER_ENV} ]] && return 0
  grep -qE '^(Hangfire__AdminApiKey|Hangfire__WorkerCount|Hangfire__Queues|Hangfire__ReadOnlyMode|Hangfire__CookieSecure|ASPNETCORE_URLS)=' "${SHARED_ENV}" || return 0

  echo "==> 迁移旧版 ${SHARED_ENV} 到共享 + Api/Worker 角色文件"
  local tmp
  tmp="$(mktemp)"
  grep -E '^(#|$|ConnectionStrings__)' "${SHARED_ENV}" > "${tmp}" || true
  if [[ ! -f ${API_ENV} ]]; then
    {
      echo "# TaskRunner Api（鉴权 / Dashboard）。只改本文件与 ${SHARED_ENV}，勿改包内 appsettings。"
      grep -E '^(Hangfire__AdminApiKey|Hangfire__ReadOnlyMode|Hangfire__CookieSecure|ASPNETCORE_URLS)=' "${SHARED_ENV}" || echo "Hangfire__AdminApiKey='CHANGE_ME'"
    } > "${API_ENV}"
    chmod 600 "${API_ENV}"
  fi
  if [[ ! -f ${WORKER_ENV} ]]; then
    {
      echo "# TaskRunner Worker（队列 / 并发）。只改本文件与 ${SHARED_ENV}，勿改包内 appsettings。"
      grep -E '^(Hangfire__WorkerCount|Hangfire__Queues)' "${SHARED_ENV}" || true
      echo "# Hangfire__WorkerCount='5'"
      echo "# Hangfire__Queues__0='default'"
    } > "${WORKER_ENV}"
    chmod 600 "${WORKER_ENV}"
  fi
  {
    echo "# TaskRunner 共享（Api + Worker）。连接串两边必须一致。"
    echo "# 角色专属键见 ${API_ENV} / ${WORKER_ENV}。勿改 /opt/taskrunner/**/appsettings.json。"
    grep -E '^ConnectionStrings__' "${tmp}" || echo "ConnectionStrings__Default='Host=127.0.0.1;Port=5432;Database=taskrunner;Username=taskrunner;Password=CHANGE_ME'"
  } > "${SHARED_ENV}"
  chmod 600 "${SHARED_ENV}"
  rm -f "${tmp}"
}

migrate_monolith_env

if [[ ! -f ${SHARED_ENV} ]]; then
  echo "==> 生成共享配置 ${SHARED_ENV}"
  cat > "${SHARED_ENV}" <<'EOF'
# TaskRunner 共享（Api + Worker）。连接串两边必须一致。
# 角色专属键见 /etc/default/taskrunner-api 与 taskrunner-worker。勿改包内 appsettings。
ConnectionStrings__Default='Host=127.0.0.1;Port=5432;Database=taskrunner;Username=taskrunner;Password=CHANGE_ME'
EOF
  chmod 600 "${SHARED_ENV}"
fi

if [[ ! -f ${API_ENV} ]]; then
  echo "==> 生成 Api 配置 ${API_ENV}"
  cat > "${API_ENV}" <<'EOF'
# TaskRunner Api（鉴权 / Dashboard）。改完：sudo systemctl restart taskrunner-api
Hangfire__AdminApiKey='CHANGE_ME'
# ASPNETCORE_URLS='http://0.0.0.0:5088'
# Hangfire__ReadOnlyMode='false'
# Hangfire__CookieSecure='true'   # TLS 反代时置 true
EOF
  chmod 600 "${API_ENV}"
fi

if [[ ! -f ${WORKER_ENV} ]]; then
  echo "==> 生成 Worker 配置 ${WORKER_ENV}"
  cat > "${WORKER_ENV}" <<'EOF'
# TaskRunner Worker（队列 / 并发）。改完：sudo systemctl restart taskrunner-worker
# Hangfire__WorkerCount='5'
# Hangfire__Queues__0='default'
EOF
  chmod 600 "${WORKER_ENV}"
fi

for f in "${SHARED_ENV}" "${API_ENV}" "${WORKER_ENV}"; do
  if grep -q "CHANGE_ME" "${f}"; then
    NEED_CONFIG=1
  fi
done

echo "==> 设置开机自启（不立即启动）"
systemctl enable taskrunner-api taskrunner-worker >/dev/null

if [[ ${NEED_CONFIG} -eq 1 ]]; then
  cat <<EOF

安装完成，还差最后一步——本机生产配置（含 CHANGE_ME，服务不会启动）：
  1) sudo nano ${SHARED_ENV}     # ConnectionStrings__Default
     sudo nano ${API_ENV}        # Hangfire__AdminApiKey
     （可选）sudo nano ${WORKER_ENV}  # Queues / WorkerCount
     不要改 ${INSTALL_ROOT}/**/appsettings.json（升级会覆盖；多机应共用同一份发布包）
  2) sudo systemctl start taskrunner-api taskrunner-worker
     （若仍含 CHANGE_ME，systemd ExecStartPre 会拒绝启动）
EOF
else
  echo "==> 配置已存在且有效，启动服务"
  systemctl restart taskrunner-api taskrunner-worker
  systemctl --no-pager --lines=5 status taskrunner-api taskrunner-worker || true
fi

cat <<'EOF'

常用命令：
  systemctl status taskrunner-api taskrunner-worker
  journalctl -u taskrunner-api -f              # Api 控制台日志（Worker 同理）
  tail -f /opt/taskrunner/api/logs/*.log       # Api 文件日志（Worker 在 worker/logs）
EOF
