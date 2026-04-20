#!/usr/bin/env bash
# build-mtc.sh — clean publish MTC for osx-x64, osx-arm64, and win-x64
# Run this after any code change to refresh the standalone binary set in Source/bin/MTC.
set -euo pipefail

cd "$(dirname "$0")"

RIDS=(
  osx-arm64
  osx-x64
  win-x64
)

echo "==> Cleaning..."
rm -rf bin obj MTC/bin MTC/obj

for RID in "${RIDS[@]}"; do
  echo "==> Publishing ${RID}..."
  AVALONIA_TELEMETRY_OPTOUT=1 dotnet publish MTC/MTC.csproj \
      -c Release \
      -r "${RID}" \
      2>&1 | grep -v "^$"

  if [[ "${RID}" == win-* ]]; then
    BIN_NAME="MTC.exe"
  else
    BIN_NAME="MTC"
  fi

  BIN_DIR="MTC/bin/Release/net10.0/${RID}/publish"
  BIN="${BIN_DIR}/${BIN_NAME}"
  DEST_DIR="bin/MTC/${RID}"
  DEST="${DEST_DIR}/${BIN_NAME}"

  rm -rf "${DEST_DIR}"
  mkdir -p "${DEST_DIR}"
  cp "${BIN}" "${DEST}"

  if [[ "${RID}" == osx-* ]]; then
    xattr -d com.apple.quarantine "${DEST}" 2>/dev/null || true
    xattr -d com.apple.quarantine "${BIN}" 2>/dev/null || true
    echo "==> Signing ${RID}..."
    codesign --force --deep --sign - "${BIN}"
    codesign --force --deep --sign - "${DEST}"
  fi

  if [[ "${RID}" == win-* ]]; then
    if [[ -f "${BIN_DIR}/MTC.pdb" ]]; then
      cp "${BIN_DIR}/MTC.pdb" "${DEST_DIR}/MTC.pdb"
    fi
    if [[ -f "bin/Release/net10.0/TWXProxy.pdb" ]]; then
      cp "bin/Release/net10.0/TWXProxy.pdb" "${DEST_DIR}/TWXProxy.pdb"
    fi
  fi

  echo "==> Done ${RID}: $(ls -lh "${DEST}" | awk '{print $5, $6, $7, $8, $9}')"
done
