#!/usr/bin/env bash
# build-release-bundles.sh — build TWX30 standalone tools for all release targets
# and package one platform zip per target in TWX30/bin.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
BIN_ROOT="${REPO_ROOT}/bin"

cd "${SCRIPT_DIR}"

if [[ "${1:-}" == "--help" ]]; then
  cat <<'EOF'
Usage: ./build-release-bundles.sh

Builds:
  - MTC
  - TWXP
  - TWXC
  - TWXD

Targets:
  - osx-arm64
  - osx-x64
  - win-x64
  - linux-x64

Outputs:
  - TWX30/bin/<rid>/MTC
  - TWX30/bin/<rid>/twxp
  - TWX30/bin/<rid>/twxc
  - TWX30/bin/<rid>/twxd
  - TWX30/bin/TWX30-<rid>.zip
EOF
  exit 0
elif [[ $# -gt 0 ]]; then
  echo "Unknown option: $1" >&2
  exit 1
fi

if [[ -n "${RID_LIST:-}" ]]; then
  IFS=' ' read -r -a RIDS <<< "${RID_LIST}"
else
  RIDS=(
    osx-arm64
    osx-x64
    win-x64
    linux-x64
  )
fi

./build-mtc.sh
./build-twxp.sh
TWXC_INSTALL_AFTER_BUILD=0 ./build-twxc.sh
./build-twxd.sh

for RID in "${RIDS[@]}"; do
  echo "==> Packaging ${RID}..."

  if [[ "${RID}" == win-* ]]; then
    MTC_BIN="MTC.exe"
    TWXP_BIN="twxp.exe"
    TWXC_BIN="twxc.exe"
    TWXD_BIN="twxd.exe"
  else
    MTC_BIN="MTC"
    TWXP_BIN="twxp"
    TWXC_BIN="twxc"
    TWXD_BIN="twxd"
  fi

  STAGE_DIR="$(mktemp -d "/tmp/twx30-release-${RID}-XXXXXX")"
  ZIP_TMP="${BIN_ROOT}/TWX30-${RID}.zip.tmp.$$"
  ZIP_DEST="${BIN_ROOT}/TWX30-${RID}.zip"

  cp "${BIN_ROOT}/${RID}/${MTC_BIN}" "${STAGE_DIR}/${MTC_BIN}"
  cp "${BIN_ROOT}/${RID}/${TWXP_BIN}" "${STAGE_DIR}/${TWXP_BIN}"
  cp "${BIN_ROOT}/${RID}/${TWXC_BIN}" "${STAGE_DIR}/${TWXC_BIN}"
  cp "${BIN_ROOT}/${RID}/${TWXD_BIN}" "${STAGE_DIR}/${TWXD_BIN}"

  rm -f "${ZIP_TMP}" "${ZIP_DEST}"
  (
    cd "${STAGE_DIR}"
    zip -qry "${ZIP_TMP}" "${MTC_BIN}" "${TWXP_BIN}" "${TWXC_BIN}" "${TWXD_BIN}"
  )
  mv -f "${ZIP_TMP}" "${ZIP_DEST}"
  rm -rf "${STAGE_DIR}"

  echo "==> Done package ${RID}: $(ls -lh "${ZIP_DEST}" | awk '{print $5, $6, $7, $8, $9}')"
done
