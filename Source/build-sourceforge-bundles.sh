#!/usr/bin/env bash
# build-sourceforge-bundles.sh — build standalone TWX30 bundles
# for SourceForge distribution.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
BIN_ROOT="${REPO_ROOT}/bin"

cd "${SCRIPT_DIR}"

if [[ "${1:-}" == "--help" ]]; then
  cat <<'EOF'
Usage: ./build-sourceforge-bundles.sh

Builds standalone binaries for:
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
  - TWX30/bin/mtc-<rid>.zip
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

for rid in "${RIDS[@]}"; do
  echo "==> Packaging ${rid}..."

  if [[ "${rid}" == win-* ]]; then
    mtc_bin="MTC.exe"
    twxp_bin="twxp.exe"
    twxc_bin="twxc.exe"
    twxd_bin="twxd.exe"
  else
    mtc_bin="MTC"
    twxp_bin="twxp"
    twxc_bin="twxc"
    twxd_bin="twxd"
  fi

  stage_dir="$(mktemp -d "/tmp/mtc-sourceforge-${rid}-XXXXXX")"
  zip_tmp="${BIN_ROOT}/mtc-${rid}.zip.tmp.$$"
  zip_dest="${BIN_ROOT}/mtc-${rid}.zip"

  cp "${BIN_ROOT}/${rid}/${mtc_bin}" "${stage_dir}/${mtc_bin}"
  cp "${BIN_ROOT}/${rid}/${twxp_bin}" "${stage_dir}/${twxp_bin}"
  cp "${BIN_ROOT}/${rid}/${twxc_bin}" "${stage_dir}/${twxc_bin}"
  cp "${BIN_ROOT}/${rid}/${twxd_bin}" "${stage_dir}/${twxd_bin}"

  rm -f "${zip_tmp}" "${zip_dest}"
  (
    cd "${stage_dir}"
    zip -qry "${zip_tmp}" "${mtc_bin}" "${twxp_bin}" "${twxc_bin}" "${twxd_bin}"
  )
  mv -f "${zip_tmp}" "${zip_dest}"
  rm -rf "${stage_dir}"

  echo "==> Done package ${rid}: $(ls -lh "${zip_dest}" | awk '{print $5, $6, $7, $8, $9}')"
done
