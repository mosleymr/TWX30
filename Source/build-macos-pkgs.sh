#!/usr/bin/env bash
# build-macos-pkgs.sh — build macOS installer packages for TWX30.
set -euo pipefail
export COPYFILE_DISABLE=1
export COPY_EXTENDED_ATTRIBUTES_DISABLE=1

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
BIN_ROOT="${REPO_ROOT}/bin"
SCRIPTS_SOURCE="${REPO_ROOT}/scripts"
MTC_ICON_SOURCE="${SCRIPT_DIR}/MTC/mtc2.png"
TWXP_ICON_SOURCE="${SCRIPT_DIR}/TWXP/TWXProxy_Icon.ico"

if [[ "${1:-}" == "--help" ]]; then
  cat <<'EOF'
Usage: ./build-macos-pkgs.sh

Builds macOS .pkg installers for:
  - osx-arm64
  - osx-x64

Each package is a macOS product installer with selectable choices:
  - Mayhem Tradewars Client
  - Tradewars Proxy
  - Compiler Package (twxc and twxd)
  - Bundled TWX scripts

During installation, tools and scripts use the TWX program directory stored in
~/Library/twxproxy/programdir.txt when present. Otherwise it defaults to
~/twxproxy and stores that path for both apps. Scripts are copied into:
  - <programdir>/scripts

Set RID_LIST to narrow the target list, e.g.:
  RID_LIST=osx-arm64 ./build-macos-pkgs.sh
EOF
  exit 0
elif [[ $# -gt 0 ]]; then
  echo "Unknown option: $1" >&2
  exit 1
fi

if [[ -n "${RID_LIST:-}" ]]; then
  IFS=' ' read -r -a RIDS <<< "${RID_LIST}"
else
  RIDS=(osx-arm64 osx-x64)
fi

VERSION="${VERSION:-$(date +%Y.%m.%d.%H%M)}"

require_binary() {
  local path="$1"
  if [[ ! -x "$path" ]]; then
    echo "Missing executable: $path" >&2
    echo "Build the standalone binaries first, for example:" >&2
    echo "  RID_LIST=\"${RIDS[*]}\" ./build-sourceforge-bundles.sh" >&2
    exit 1
  fi
}

require_dir() {
  local path="$1"
  if [[ ! -d "$path" ]]; then
    echo "Missing directory: $path" >&2
    exit 1
  fi
}

write_info_plist() {
  local plist_path="$1"
  local bundle_id="$2"
  local bundle_name="$3"
  local executable="$4"
  local icon_file="$5"

  cat >"$plist_path" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>${executable}</string>
  <key>CFBundleIdentifier</key>
  <string>${bundle_id}</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleIconFile</key>
  <string>${icon_file}</string>
  <key>CFBundleName</key>
  <string>${bundle_name}</string>
  <key>CFBundleDisplayName</key>
  <string>${bundle_name}</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${VERSION}</string>
  <key>CFBundleVersion</key>
  <string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF
}

create_icns() {
  local source_image="$1"
  local output_icns="$2"

  if [[ ! -f "$source_image" ]]; then
    echo "Missing icon source: $source_image" >&2
    exit 1
  fi

  if ! command -v sips >/dev/null 2>&1 || ! command -v iconutil >/dev/null 2>&1; then
    echo "sips and iconutil are required to build macOS app icons." >&2
    exit 1
  fi

  local work_dir
  work_dir="$(mktemp -d "/tmp/twx30-icon-XXXXXX")"
  local base_png="${work_dir}/base.png"
  local iconset="${work_dir}/AppIcon.iconset"
  mkdir -p "$iconset"

  sips -s format png "$source_image" --out "$base_png" >/dev/null

  sips -z 16 16 "$base_png" --out "${iconset}/icon_16x16.png" >/dev/null
  sips -z 32 32 "$base_png" --out "${iconset}/icon_16x16@2x.png" >/dev/null
  sips -z 32 32 "$base_png" --out "${iconset}/icon_32x32.png" >/dev/null
  sips -z 64 64 "$base_png" --out "${iconset}/icon_32x32@2x.png" >/dev/null
  sips -z 128 128 "$base_png" --out "${iconset}/icon_128x128.png" >/dev/null
  sips -z 256 256 "$base_png" --out "${iconset}/icon_128x128@2x.png" >/dev/null
  sips -z 256 256 "$base_png" --out "${iconset}/icon_256x256.png" >/dev/null
  sips -z 512 512 "$base_png" --out "${iconset}/icon_256x256@2x.png" >/dev/null
  sips -z 512 512 "$base_png" --out "${iconset}/icon_512x512.png" >/dev/null
  sips -z 1024 1024 "$base_png" --out "${iconset}/icon_512x512@2x.png" >/dev/null

  iconutil -c icns "$iconset" -o "$output_icns"
  rm -rf "$work_dir"
}

create_app_bundle() {
  local app_path="$1"
  local bundle_id="$2"
  local bundle_name="$3"
  local executable="$4"
  local source_binary="$5"
  local icon_source="$6"
  local icon_file="$7"

  rm -rf "$app_path"
  mkdir -p "${app_path}/Contents/MacOS" "${app_path}/Contents/Resources"
  cp "$source_binary" "${app_path}/Contents/MacOS/${executable}"
  chmod 755 "${app_path}/Contents/MacOS/${executable}"
  create_icns "$icon_source" "${app_path}/Contents/Resources/${icon_file}"
  write_info_plist "${app_path}/Contents/Info.plist" "$bundle_id" "$bundle_name" "$executable" "$icon_file"

  if command -v codesign >/dev/null 2>&1; then
    codesign --force --deep --sign - "$app_path" >/dev/null
  fi
}

copy_tree_clean() {
  local src="$1"
  local dest="$2"

  mkdir -p "$dest"
  (
    cd "$src"
    tar --exclude='.DS_Store' --exclude='._*' --exclude='.git' -cf - .
  ) | (
    cd "$dest"
    tar -xf -
  )
}

write_programdir_postinstall() {
  local script_path="$1"
  local rid="$2"
  local mode="$3"

  cat >"$script_path" <<EOF
#!/bin/bash
set -u

RID="${rid}"
MODE="${mode}"
TOOLS_PAYLOAD_DIR="/Library/Application Support/TWX30/InstallerTools/\${RID}"
SCRIPTS_PAYLOAD_DIR="/Library/Application Support/TWX30/InstallerScripts/\${RID}/scripts"
LOG_FILE="/Library/Logs/TWX30Installer.log"

log() {
  printf '%s %s\n' "\$(date '+%Y-%m-%d %H:%M:%S')" "\$*" >>"\$LOG_FILE"
}

console_user="\$(stat -f %Su /dev/console 2>/dev/null || true)"
program_dir="\${TWX30_PROGRAMDIR:-}"
home_dir=""

if [[ -n "\$console_user" && "\$console_user" != "root" && "\$console_user" != "loginwindow" ]]; then
  home_dir="\$(dscl . -read "/Users/\$console_user" NFSHomeDirectory 2>/dev/null | awk '{print \$2}' || true)"
fi

if [[ -z "\$program_dir" && -n "\$home_dir" ]]; then
  locator_dir="\${home_dir}/Library/twxproxy"
  locator_file="\${locator_dir}/programdir.txt"
  if [[ -f "\$locator_file" ]]; then
    program_dir="\$(head -n 1 "\$locator_file" | sed 's/[[:space:]]*\$//')"
  fi

  if [[ -z "\$program_dir" ]]; then
    program_dir="\${home_dir}/twxproxy"
  fi
fi

program_dir="\${program_dir%/}"

if [[ -z "\$program_dir" ]]; then
  log "No TWX program directory could be resolved; \${MODE} payload was not installed."
  exit 1
fi

bin_dir="\${program_dir}/bin"
scripts_dir="\${program_dir}/scripts"
games_dir="\${program_dir}/games"
logs_dir="\${program_dir}/logs"
modules_dir="\${program_dir}/modules"
mkdir -p "\$bin_dir" "\$scripts_dir" "\$games_dir" "\$logs_dir" "\$modules_dir"

if [[ "\$MODE" == "tools" ]]; then
  if [[ ! -x "\${TOOLS_PAYLOAD_DIR}/twxc" || ! -x "\${TOOLS_PAYLOAD_DIR}/twxd" ]]; then
    log "Installer tools payload missing for \${RID}: \${TOOLS_PAYLOAD_DIR}"
    exit 1
  fi
  install -m 755 "\${TOOLS_PAYLOAD_DIR}/twxc" "\${bin_dir}/twxc"
  install -m 755 "\${TOOLS_PAYLOAD_DIR}/twxd" "\${bin_dir}/twxd"
  rm -rf "\$TOOLS_PAYLOAD_DIR"
fi

if [[ "\$MODE" == "scripts" ]]; then
  if [[ ! -d "\$SCRIPTS_PAYLOAD_DIR" ]]; then
    log "Installer scripts payload missing for \${RID}: \${SCRIPTS_PAYLOAD_DIR}"
    exit 1
  fi
  ditto "\$SCRIPTS_PAYLOAD_DIR" "\$scripts_dir"
  rm -rf "/Library/Application Support/TWX30/InstallerScripts/\${RID}"
fi

if [[ -n "\$home_dir" ]]; then
  locator_dir="\${home_dir}/Library/twxproxy"
  locator_file="\${locator_dir}/programdir.txt"
  mkdir -p "\$locator_dir"
  if [[ ! -f "\$locator_file" ]]; then
    printf '%s\n' "\$program_dir" >"\$locator_file"
  fi
fi

config_file="\${program_dir}/config.twx"
if [[ ! -f "\$config_file" ]]; then
  cat >"\$config_file" <<CONFIG
<?xml version="1.0" encoding="utf-8"?>
<TwxProxyConfig>
  <SharedPaths>
    <ProgramDirectory>\${program_dir}</ProgramDirectory>
    <ScriptsDirectory>\${scripts_dir}</ScriptsDirectory>
  </SharedPaths>
</TwxProxyConfig>
CONFIG
fi

if [[ -n "\$console_user" && "\$console_user" != "root" && -n "\$home_dir" ]]; then
  for owned_path in "\$program_dir" "\$bin_dir" "\$scripts_dir" "\$games_dir" "\$logs_dir" "\$modules_dir" "\${home_dir}/Library/twxproxy"; do
    if [[ "\$owned_path" == "\$home_dir"* ]]; then
      chown -R "\$console_user":staff "\$owned_path" 2>/dev/null || true
    fi
  done
fi

rmdir "/Library/Application Support/TWX30/InstallerTools" 2>/dev/null || true
rmdir "/Library/Application Support/TWX30/InstallerScripts" 2>/dev/null || true
log "Installed \${MODE} payload for \${RID} into \${program_dir}"
exit 0
EOF

  chmod 755 "$script_path"
}

build_component_pkg() {
  local root_dir="$1"
  local scripts_dir="$2"
  local identifier="$3"
  local output_pkg="$4"

  local clean_root_dir
  local clean_scripts_dir
  clean_root_dir="$(mktemp -d "/tmp/twx30-component-root-XXXXXX")"
  clean_scripts_dir="$(mktemp -d "/tmp/twx30-component-scripts-XXXXXX")"

  find "$root_dir" "$scripts_dir" -name '._*' -delete
  xattr -cr "$root_dir" "$scripts_dir" 2>/dev/null || true
  ditto --norsrc --noextattr "$root_dir" "$clean_root_dir"
  ditto --norsrc --noextattr "$scripts_dir" "$clean_scripts_dir"
  find "$clean_root_dir" "$clean_scripts_dir" -name '._*' -delete

  pkgbuild \
    --root "$clean_root_dir" \
    --scripts "$clean_scripts_dir" \
    --filter '/\._[^/]*$' \
    --filter '^\._[^/]*$' \
    --filter '/\.DS_Store$' \
    --filter '^\.DS_Store$' \
    --identifier "$identifier" \
    --version "$VERSION" \
    --install-location "/" \
    --ownership recommended \
    "$output_pkg" >/dev/null

  rm -rf "$clean_root_dir" "$clean_scripts_dir"
}

write_distribution() {
  local distribution_path="$1"
  local rid="$2"

  cat >"$distribution_path" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<installer-gui-script minSpecVersion="1">
  <title>TWX30</title>
  <options customize="always" require-scripts="true"/>
  <domains enable_anywhere="true" enable_currentUserHome="false" enable_localSystem="true"/>
  <choices-outline>
    <line choice="mtc"/>
    <line choice="twxp"/>
    <line choice="tools"/>
    <line choice="scripts"/>
  </choices-outline>
  <choice id="mtc" title="Mayhem Tradewars Client" description="Install the MTC desktop client." selected="true" visible="true" enabled="true">
    <pkg-ref id="com.mayhem.twx30.mtc.${rid}"/>
  </choice>
  <choice id="twxp" title="Tradewars Proxy" description="Install the standalone TWX Proxy application." selected="true" visible="true" enabled="true">
    <pkg-ref id="com.mayhem.twx30.twxp.${rid}"/>
  </choice>
  <choice id="tools" title="Compiler Package" description="Install twxc and twxd into the configured TWX program directory." selected="true" visible="true" enabled="true">
    <pkg-ref id="com.mayhem.twx30.tools.${rid}"/>
  </choice>
  <choice id="scripts" title="Bundled TWX Scripts" description="Populate the configured TWX scripts directory with the bundled scripts." selected="true" visible="true" enabled="true">
    <pkg-ref id="com.mayhem.twx30.scripts.${rid}"/>
  </choice>
  <pkg-ref id="com.mayhem.twx30.mtc.${rid}" version="${VERSION}" auth="Root">mtc.pkg</pkg-ref>
  <pkg-ref id="com.mayhem.twx30.twxp.${rid}" version="${VERSION}" auth="Root">twxp.pkg</pkg-ref>
  <pkg-ref id="com.mayhem.twx30.tools.${rid}" version="${VERSION}" auth="Root">tools.pkg</pkg-ref>
  <pkg-ref id="com.mayhem.twx30.scripts.${rid}" version="${VERSION}" auth="Root">scripts.pkg</pkg-ref>
</installer-gui-script>
EOF
}

for rid in "${RIDS[@]}"; do
  case "$rid" in
    osx-arm64|osx-x64) ;;
    *)
      echo "Unsupported macOS RID for pkg: $rid" >&2
      exit 1
      ;;
  esac

  echo "==> Building macOS pkg for ${rid}..."

  require_binary "${BIN_ROOT}/${rid}/MTC"
  require_binary "${BIN_ROOT}/${rid}/twxp"
  require_binary "${BIN_ROOT}/${rid}/twxc"
  require_binary "${BIN_ROOT}/${rid}/twxd"
  require_dir "$SCRIPTS_SOURCE"

  work_dir="$(mktemp -d "/tmp/twx30-macos-pkg-${rid}-XXXXXX")"
  components_dir="${work_dir}/components"
  mkdir -p "$components_dir"

  mtc_root="${work_dir}/mtc-root"
  mtc_scripts="${work_dir}/mtc-scripts"
  mkdir -p "$mtc_root" "$mtc_scripts"
  create_app_bundle \
    "${mtc_root}/Applications/Mayhem Tradewars Client.app" \
    "com.mayhem.twx30.mtc.${rid}" \
    "Mayhem Tradewars Client" \
    "MTC" \
    "${BIN_ROOT}/${rid}/MTC" \
    "$MTC_ICON_SOURCE" \
    "MTC.icns"
  build_component_pkg "$mtc_root" "$mtc_scripts" "com.mayhem.twx30.mtc.${rid}" "${components_dir}/mtc.pkg"

  twxp_root="${work_dir}/twxp-root"
  twxp_scripts="${work_dir}/twxp-scripts"
  mkdir -p "$twxp_root" "$twxp_scripts"
  create_app_bundle \
    "${twxp_root}/Applications/Tradewars Proxy.app" \
    "com.mayhem.twx30.twxp.${rid}" \
    "Tradewars Proxy" \
    "twxp" \
    "${BIN_ROOT}/${rid}/twxp" \
    "$TWXP_ICON_SOURCE" \
    "TWXP.icns"
  build_component_pkg "$twxp_root" "$twxp_scripts" "com.mayhem.twx30.twxp.${rid}" "${components_dir}/twxp.pkg"

  tools_root="${work_dir}/tools-root"
  tools_scripts="${work_dir}/tools-scripts"
  tools_payload_dir="${tools_root}/Library/Application Support/TWX30/InstallerTools/${rid}"
  mkdir -p "$tools_payload_dir" "$tools_scripts"
  cp "${BIN_ROOT}/${rid}/twxc" "${tools_payload_dir}/twxc"
  cp "${BIN_ROOT}/${rid}/twxd" "${tools_payload_dir}/twxd"
  chmod 755 "${tools_payload_dir}/twxc" "${tools_payload_dir}/twxd"
  write_programdir_postinstall "${tools_scripts}/postinstall" "$rid" "tools"
  build_component_pkg "$tools_root" "$tools_scripts" "com.mayhem.twx30.tools.${rid}" "${components_dir}/tools.pkg"

  scripts_root="${work_dir}/scripts-root"
  scripts_scripts="${work_dir}/scripts-scripts"
  scripts_payload_dir="${scripts_root}/Library/Application Support/TWX30/InstallerScripts/${rid}/scripts"
  mkdir -p "$scripts_payload_dir" "$scripts_scripts"
  copy_tree_clean "$SCRIPTS_SOURCE" "$scripts_payload_dir"
  write_programdir_postinstall "${scripts_scripts}/postinstall" "$rid" "scripts"
  build_component_pkg "$scripts_root" "$scripts_scripts" "com.mayhem.twx30.scripts.${rid}" "${components_dir}/scripts.pkg"

  distribution="${work_dir}/Distribution.xml"
  write_distribution "$distribution" "$rid"

  pkg_dest="${BIN_ROOT}/twx30-${rid}.pkg"
  rm -f "$pkg_dest"
  productbuild \
    --distribution "$distribution" \
    --package-path "$components_dir" \
    "$pkg_dest" >/dev/null

  rm -rf "$work_dir"
  echo "==> Done package ${rid}: $(ls -lh "$pkg_dest" | awk '{print $5, $6, $7, $8, $9}')"
done
