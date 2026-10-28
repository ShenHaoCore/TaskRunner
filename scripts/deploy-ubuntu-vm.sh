#!/usr/bin/env bash
# 把已打好的 linux-x64 包拷到 Ubuntu（实体机或本机虚拟机）并执行 install.sh。
# 用法：
#   ./scripts/publish-linux.sh   # 或 Windows: ./scripts/publish-linux.ps1
#   ./scripts/deploy-ubuntu-vm.sh user@192.168.x.x
#   PACKAGE=artifacts/taskrunner-3.3.0-linux-x64.tar.gz ./scripts/deploy-ubuntu-vm.sh user@vm
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET="${1:-}"

if [[ -z ${TARGET} ]]; then
  cat <<'EOF' >&2
用法: ./scripts/deploy-ubuntu-vm.sh user@host

示例（本机 Ubuntu 虚拟机）:
  ./scripts/deploy-ubuntu-vm.sh taskrunner@192.168.56.10

可选环境变量:
  PACKAGE   指定 tar.gz 路径（默认取 artifacts/ 下最新的 taskrunner-*-linux-x64.tar.gz）
EOF
  exit 1
fi

if [[ -n ${PACKAGE:-} ]]; then
  PACKAGE_PATH="${PACKAGE}"
  if [[ ${PACKAGE_PATH} != /* ]]; then
    PACKAGE_PATH="${ROOT}/${PACKAGE_PATH}"
  fi
else
  PACKAGE_PATH="$(ls -t "${ROOT}"/artifacts/taskrunner-*-linux-x64.tar.gz 2>/dev/null | head -n1 || true)"
fi

if [[ -z ${PACKAGE_PATH} || ! -f ${PACKAGE_PATH} ]]; then
  echo "找不到发布包。请先运行 ./scripts/publish-linux.sh 或 ./scripts/publish-linux.ps1" >&2
  exit 1
fi

BASENAME="$(basename "${PACKAGE_PATH}")"
REMOTE_TAR="/tmp/${BASENAME}"
REMOTE_DIR="/tmp/taskrunner-install"

echo "==> 上传 ${PACKAGE_PATH} -> ${TARGET}:${REMOTE_TAR}"
LOCAL_SHA="$(sha256sum "${PACKAGE_PATH}" | cut -d' ' -f1)"
scp "${PACKAGE_PATH}" "${TARGET}:${REMOTE_TAR}"

echo "==> 校验上传完整性（SHA256）"
REMOTE_SHA="$(ssh "${TARGET}" "sha256sum '${REMOTE_TAR}' | cut -d' ' -f1" || true)"
if [[ "${REMOTE_SHA}" != "${LOCAL_SHA}" ]]; then
  echo "==> 校验不一致，重试上传一次"
  scp "${PACKAGE_PATH}" "${TARGET}:${REMOTE_TAR}"
  REMOTE_SHA="$(ssh "${TARGET}" "sha256sum '${REMOTE_TAR}' | cut -d' ' -f1" || true)"
fi
if [[ "${REMOTE_SHA}" != "${LOCAL_SHA}" ]]; then
  echo "包哈希不一致：远端 ${REMOTE_SHA:-<empty>} != 本地 ${LOCAL_SHA}，中止安装" >&2
  exit 1
fi
echo "==> SHA256 校验通过（${LOCAL_SHA:0:12}…）"

echo "==> 远端解压并安装（需要 sudo）"
ssh -t "${TARGET}" "rm -rf '${REMOTE_DIR}' && mkdir -p '${REMOTE_DIR}' && tar -xzf '${REMOTE_TAR}' -C '${REMOTE_DIR}' && sudo bash '${REMOTE_DIR}/deploy/install.sh'"

cat <<EOF

远端安装脚本已跑完。若提示 CHANGE_ME：
  ssh ${TARGET}
  sudo nano /etc/default/taskrunner      # ConnectionStrings__Default
  sudo nano /etc/default/taskrunner-api  # Hangfire__AdminApiKey
  # 只改本机 env，不要改 /opt/taskrunner/**/appsettings.json
  sudo systemctl start taskrunner-api taskrunner-worker
EOF
