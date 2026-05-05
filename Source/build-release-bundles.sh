#!/usr/bin/env bash
# build-release-bundles.sh — build MTC, TWXC, and TWXD for all release targets
# and package one platform zip per target in Source/bin.
set -euo pipefail

cd "$(dirname "$0")"

if [[ "${1:-}" == "--help" ]]; then
  cat <<'EOF'
Usage: ./build-release-bundles.sh

Builds:
  - MTC
  - TWXC
  - TWXD

Targets:
  - osx-arm64
  - osx-x64
  - win-x64

Outputs:
  - Source/bin/MTC/<rid>/*
  - Source/bin/TWXC/<rid>/*
  - Source/bin/TWXD/<rid>/*
  - Source/bin/TWX30-<rid>.zip
EOF
  exit 0
elif [[ $# -gt 0 ]]; then
  echo "Unknown option: $1" >&2
  exit 1
fi

RIDS=(
  osx-arm64
  osx-x64
  win-x64
)

./build-mtc.sh
./build-twxc.sh
./build-twxd.sh

for RID in "${RIDS[@]}"; do
  echo "==> Packaging ${RID}..."

  if [[ "${RID}" == win-* ]]; then
    MTC_BIN="MTC.exe"
    TWXC_BIN="twxc.exe"
    TWXD_BIN="twxd.exe"
  else
    MTC_BIN="MTC"
    TWXC_BIN="twxc"
    TWXD_BIN="twxd"
  fi

  STAGE_DIR="$(mktemp -d "/tmp/twx30-release-${RID}-XXXXXX")"
  ZIP_TMP="bin/TWX30-${RID}.zip.tmp.$$"
  ZIP_DEST="bin/TWX30-${RID}.zip"

  cp "bin/MTC/${RID}/${MTC_BIN}" "${STAGE_DIR}/${MTC_BIN}"
  cp "bin/TWXC/${RID}/${TWXC_BIN}" "${STAGE_DIR}/${TWXC_BIN}"
  cp "bin/TWXD/${RID}/${TWXD_BIN}" "${STAGE_DIR}/${TWXD_BIN}"

  rm -f "${ZIP_TMP}" "${ZIP_DEST}"
  (
    cd "${STAGE_DIR}"
    zip -qry "${OLDPWD}/${ZIP_TMP}" "${MTC_BIN}" "${TWXC_BIN}" "${TWXD_BIN}"
  )
  mv -f "${ZIP_TMP}" "${ZIP_DEST}"
  rm -rf "${STAGE_DIR}"

  echo "==> Done package ${RID}: $(ls -lh "${ZIP_DEST}" | awk '{print $5, $6, $7, $8, $9}')"
done
