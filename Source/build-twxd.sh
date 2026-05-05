#!/usr/bin/env bash
# build-twxd.sh — clean publish twxd for osx-arm64, osx-x64, and win-x64
# Release binaries are written to Source/bin/TWXD/<rid> and mirrored to
# Source/TWXD/publish/<rid>.
set -euo pipefail

cd "$(dirname "$0")"

install_release_artifact() {
  local src="$1"
  local dest="$2"
  local rid="$3"
  local dest_dir
  local tmp

  dest_dir="$(dirname "$dest")"
  tmp="${dest}.tmp.$$"

  mkdir -p "$dest_dir"
  rm -f "$tmp"
  cp "$src" "$tmp"
  chmod 755 "$tmp" 2>/dev/null || true

  if [[ "$rid" == osx-* ]]; then
    xattr -d com.apple.quarantine "$tmp" 2>/dev/null || true
    codesign --force --deep --sign - "$tmp"
  fi

  mv -f "$tmp" "$dest"
}

RIDS=(
  osx-arm64
  osx-x64
  win-x64
)

echo "==> Cleaning..."
rm -rf obj TWXD/bin TWXD/obj
rm -rf TWXD/publish bin/TWXD

for RID in "${RIDS[@]}"; do
  echo "==> Publishing ${RID}..."
  DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet publish TWXD/TWXD.csproj \
      -c Release \
      -r "${RID}" \
      2>&1 | grep -v "^$"

  if [[ "${RID}" == win-* ]]; then
    BIN_NAME="twxd.exe"
  else
    BIN_NAME="twxd"
  fi

  BIN_DIR="TWXD/bin/Release/net10.0/${RID}/publish"
  BIN="${BIN_DIR}/${BIN_NAME}"
  DEST_DIR="TWXD/publish/${RID}"
  DEST="${DEST_DIR}/${BIN_NAME}"
  RELEASE_DIR="bin/TWXD/${RID}"
  RELEASE_DEST="${RELEASE_DIR}/${BIN_NAME}"

  if [[ "${RID}" == osx-* ]]; then
    xattr -d com.apple.quarantine "${BIN}" 2>/dev/null || true
    echo "==> Signing ${RID}..."
    codesign --force --deep --sign - "${BIN}"
  fi

  install_release_artifact "${BIN}" "${DEST}" "${RID}"
  install_release_artifact "${BIN}" "${RELEASE_DEST}" "${RID}"

  if [[ "${RID}" == win-* ]]; then
    if [[ -f "${BIN_DIR}/twxd.pdb" ]]; then
      mkdir -p "${DEST_DIR}" "${RELEASE_DIR}"
      cp "${BIN_DIR}/twxd.pdb" "${DEST_DIR}/twxd.pdb"
      cp "${BIN_DIR}/twxd.pdb" "${RELEASE_DIR}/twxd.pdb"
    fi
    if [[ -f "bin/Release/net10.0/TWXProxy.pdb" ]]; then
      mkdir -p "${DEST_DIR}" "${RELEASE_DIR}"
      cp "bin/Release/net10.0/TWXProxy.pdb" "${DEST_DIR}/TWXProxy.pdb"
      cp "bin/Release/net10.0/TWXProxy.pdb" "${RELEASE_DIR}/TWXProxy.pdb"
    fi
  fi

  echo "==> Done ${RID}: $(ls -lh "${RELEASE_DEST}" | awk '{print $5, $6, $7, $8, $9}')"
done
