#!/usr/bin/env bash
# build-mtc.sh — clean publish MTC for osx-x64, osx-arm64, and win-x64
# Release binaries are written to Source/bin/MTC/<rid> and mirrored to
# Source/MTC/publish/<rid>.
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

if [[ "${1:-}" == "--help" ]]; then
  cat <<'EOF'
Usage: ./build-mtc.sh

  Publishes standalone MTC release binaries into:
    - Source/bin/MTC/<rid>
    - Source/MTC/publish/<rid>
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

echo "==> Cleaning..."
rm -rf obj MTC/bin MTC/obj

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
  LEGACY_DEST_DIR="MTC/publish/${RID}"
  LEGACY_DEST="${LEGACY_DEST_DIR}/${BIN_NAME}"

  if [[ "${RID}" == osx-* ]]; then
    xattr -d com.apple.quarantine "${BIN}" 2>/dev/null || true
    echo "==> Signing ${RID}..."
    codesign --force --deep --sign - "${BIN}"
  fi

  install_release_artifact "${BIN}" "${DEST}" "${RID}"
  install_release_artifact "${BIN}" "${LEGACY_DEST}" "${RID}"

  if [[ -f "${BIN_DIR}/MTC.pdb" ]]; then
    mkdir -p "${DEST_DIR}" "${LEGACY_DEST_DIR}"
    cp "${BIN_DIR}/MTC.pdb" "${DEST_DIR}/MTC.pdb"
    cp "${BIN_DIR}/MTC.pdb" "${LEGACY_DEST_DIR}/MTC.pdb"
  fi
  if [[ -f "bin/Release/net10.0/TWXProxy.pdb" ]]; then
    mkdir -p "${DEST_DIR}" "${LEGACY_DEST_DIR}"
    cp "bin/Release/net10.0/TWXProxy.pdb" "${DEST_DIR}/TWXProxy.pdb"
    cp "bin/Release/net10.0/TWXProxy.pdb" "${LEGACY_DEST_DIR}/TWXProxy.pdb"
  fi

  echo "==> Done ${RID}: $(ls -lh "${DEST}" | awk '{print $5, $6, $7, $8, $9}')"
done
