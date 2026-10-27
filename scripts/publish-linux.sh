#!/usr/bin/env bash
# 交叉编译 linux-x64 发布包（Mac / Linux 宿主机可用；Windows 请用 publish-linux.ps1）。
# 用法：
#   ./scripts/publish-linux.sh
#   ./scripts/publish-linux.sh --framework-dependent
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${ROOT}/artifacts/linux"
FRAMEWORK_DEPENDENT=0

for arg in "$@"; do
  case "${arg}" in
    --framework-dependent|-f) FRAMEWORK_DEPENDENT=1 ;;
    -h|--help)
      echo "Usage: $0 [--framework-dependent]"
      exit 0
      ;;
    *)
      echo "Unknown argument: ${arg}" >&2
      exit 1
      ;;
  esac
done

if [[ ${FRAMEWORK_DEPENDENT} -eq 1 ]]; then
  SELF_CONTAINED=false
  SINGLE_FILE=false
else
  SELF_CONTAINED=true
  SINGLE_FILE=true
fi

rm -rf "${OUT}"
mkdir -p "${OUT}"

VERSION=dev
if tag="$(git -C "${ROOT}" describe --tags --abbrev=0 2>/dev/null)"; then
  VERSION="${tag#v}"
fi

for project in TaskRunner.Api TaskRunner.Worker; do
  name="${project#TaskRunner.}"
  name="$(printf '%s' "${name}" | tr '[:upper:]' '[:lower:]')"
  project_out="${OUT}/${name}"
  echo "==> Publishing ${project} (linux-x64, self-contained=${SELF_CONTAINED}) -> ${project_out}"
  dotnet publish "${ROOT}/src/${project}/${project}.csproj" \
    -c Release -r linux-x64 \
    --self-contained "${SELF_CONTAINED}" \
    -p:PublishSingleFile="${SINGLE_FILE}" \
    -p:InformationalVersion="${VERSION}" \
    -o "${project_out}"
done

cp -a "${ROOT}/deploy/linux" "${OUT}/deploy"

PACKAGE="${ROOT}/artifacts/taskrunner-${VERSION}-linux-x64.tar.gz"
rm -f "${PACKAGE}"
tar -czf "${PACKAGE}" -C "${OUT}" .
SIZE="$(du -h "${PACKAGE}" | awk '{print $1}')"

cat <<EOF

Package ready: ${PACKAGE} (${SIZE})
Deploy to Ubuntu VM:
  ./scripts/deploy-ubuntu-vm.sh user@vm-ip
  # or manually:
  scp ${PACKAGE} user@server:/tmp/
  ssh user@server 'mkdir -p /tmp/taskrunner && tar -xzf /tmp/taskrunner-${VERSION}-linux-x64.tar.gz -C /tmp/taskrunner && sudo bash /tmp/taskrunner/deploy/install.sh'
EOF
