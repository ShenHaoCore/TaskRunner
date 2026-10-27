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
DEFAULTS_FILE=/etc/default/taskrunner

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
if [[ ! -f ${DEFAULTS_FILE} ]]; then
  echo "==> 生成配置文件 ${DEFAULTS_FILE}（含占位符，启动前必须修改）"
  cat > "${DEFAULTS_FILE}" <<'EOF'
# TaskRunner 生产配置（systemd EnvironmentFile）。
# 两处 CHANGE_ME 必填，改完执行：sudo systemctl restart taskrunner-api taskrunner-worker
ConnectionStrings__Default='Host=localhost;Port=5432;Database=taskrunner;Username=taskrunner;Password=CHANGE_ME'
Hangfire__AdminApiKey='CHANGE_ME'

# 可选覆盖（取消注释生效；此处同名变量优先于 unit 内默认值）：
# ASPNETCORE_URLS='http://0.0.0.0:5088'
# Hangfire__ReadOnlyMode='false'
# Hangfire__WorkerCount='5'
# Hangfire__CookieSecure='true'   # TLS 反代部署时置 true
EOF
  chmod 600 "${DEFAULTS_FILE}"
  NEED_CONFIG=1
elif grep -q "CHANGE_ME" "${DEFAULTS_FILE}"; then
  NEED_CONFIG=1
fi

echo "==> 设置开机自启（不立即启动）"
systemctl enable taskrunner-api taskrunner-worker >/dev/null

if [[ ${NEED_CONFIG} -eq 1 ]]; then
  cat <<EOF

安装完成，还差最后一步——生产配置：
  1) sudo nano ${DEFAULTS_FILE}    # 填好数据库连接串与管理员密钥（两处 CHANGE_ME）
  2) sudo systemctl start taskrunner-api taskrunner-worker
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
