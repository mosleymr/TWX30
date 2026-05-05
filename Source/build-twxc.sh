#!/usr/bin/env bash
# build-twxc.sh — clean publish twxc for osx-arm64, osx-x64, and win-x64
# Release binaries are written to Source/bin/TWXC/<rid> and mirrored to
# Source/TWXC/publish/<rid>.
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

HOST_ARCH="$(uname -m)"
HOST_RID=""
case "${HOST_ARCH}" in
  arm64)
    HOST_RID="osx-arm64"
    ;;
  x86_64)
    HOST_RID="osx-x64"
    ;;
esac

INSTALL_DIR="/usr/local/bin"
INSTALL_PATH="${INSTALL_DIR}/twxc"

run_install_cmd() {
  if [[ -w "${INSTALL_DIR}" ]] || [[ ! -e "${INSTALL_PATH}" && -w "${INSTALL_DIR}" ]]; then
    "$@"
    return
  fi

  if sudo -n true 2>/dev/null; then
    sudo -n "$@"
    return
  fi

  echo "==> Warning: published standalone succeeded, but install to ${INSTALL_PATH} requires sudo." >&2
  return 1
}

echo "==> Cleaning..."
rm -rf obj TWXC/bin TWXC/obj
rm -rf TWXC/publish bin/TWXC

for RID in "${RIDS[@]}"; do
  echo "==> Publishing ${RID}..."
  DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet publish TWXC/TWXC.csproj \
      -c Release \
      -r "${RID}" \
      2>&1 | grep -v "^$"

  if [[ "${RID}" == win-* ]]; then
    BIN_NAME="twxc.exe"
  else
    BIN_NAME="twxc"
  fi

  BIN_DIR="TWXC/bin/Release/net10.0/${RID}/publish"
  BIN="${BIN_DIR}/${BIN_NAME}"
  DEST_DIR="TWXC/publish/${RID}"
  DEST="${DEST_DIR}/${BIN_NAME}"
  RELEASE_DIR="bin/TWXC/${RID}"
  RELEASE_DEST="${RELEASE_DIR}/${BIN_NAME}"

  if [[ "${RID}" == osx-* ]]; then
    xattr -d com.apple.quarantine "${BIN}" 2>/dev/null || true
    echo "==> Signing ${RID}..."
    codesign --force --deep --sign - "${BIN}"
  fi

  install_release_artifact "${BIN}" "${DEST}" "${RID}"
  install_release_artifact "${BIN}" "${RELEASE_DEST}" "${RID}"

  if [[ "${RID}" == win-* ]]; then
    if [[ -f "${BIN_DIR}/twxc.pdb" ]]; then
      mkdir -p "${DEST_DIR}" "${RELEASE_DIR}"
      cp "${BIN_DIR}/twxc.pdb" "${DEST_DIR}/twxc.pdb"
      cp "${BIN_DIR}/twxc.pdb" "${RELEASE_DIR}/twxc.pdb"
    fi
    if [[ -f "bin/Release/net10.0/TWXProxy.pdb" ]]; then
      mkdir -p "${DEST_DIR}" "${RELEASE_DIR}"
      cp "bin/Release/net10.0/TWXProxy.pdb" "${DEST_DIR}/TWXProxy.pdb"
      cp "bin/Release/net10.0/TWXProxy.pdb" "${RELEASE_DIR}/TWXProxy.pdb"
    fi
  fi

  echo "==> Done ${RID}: $(ls -lh "${RELEASE_DEST}" | awk '{print $5, $6, $7, $8, $9}')"
done

if [[ -n "${HOST_RID}" ]]; then
  HOST_DEST="bin/TWXC/${HOST_RID}/twxc"
  if [[ -f "${HOST_DEST}" ]]; then
    echo "==> Installing ${HOST_RID} standalone to ${INSTALL_PATH}..."
    run_install_cmd mkdir -p "${INSTALL_DIR}"
    run_install_cmd cp "${HOST_DEST}" "${INSTALL_PATH}"
    run_install_cmd chmod 755 "${INSTALL_PATH}"
    run_install_cmd xattr -d com.apple.quarantine "${INSTALL_PATH}" 2>/dev/null || true
    if [[ "${HOST_RID}" == osx-* ]]; then
      run_install_cmd codesign --force --deep --sign - "${INSTALL_PATH}"
    fi
    ls -lh "${INSTALL_PATH}" | awk '{print "==> Installed: " $5, $6, $7, $8, $9}'
  fi
fi
