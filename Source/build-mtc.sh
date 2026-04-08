#!/usr/bin/env bash
# build-mtc.sh — clean publish MTC for osx-arm64
# Run this after any code change to get a fresh binary.
set -euo pipefail

cd "$(dirname "$0")"

echo "==> Cleaning..."
rm -rf bin obj MTC/bin MTC/obj

echo "==> Publishing..."
AVALONIA_TELEMETRY_OPTOUT=1 dotnet publish MTC/MTC.csproj \
    -c Release \
    -r osx-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PublishTrimmed=false \
    2>&1 | grep -v "^$"

BIN="MTC/bin/Release/net10.0/osx-arm64/publish/MTC"

# Also copy to the shortcut path the user runs from
DEST_DIR="MTC/publish/osx-arm64"
DEST="$DEST_DIR/MTC"
rm -rf "$DEST_DIR"
mkdir -p "$DEST_DIR"
cp "$BIN" "$DEST"
xattr -d com.apple.quarantine "$DEST" 2>/dev/null || true
xattr -d com.apple.quarantine "$BIN"  2>/dev/null || true

echo "==> Signing..."
codesign --force --deep --sign - "$BIN"
codesign --force --deep --sign - "$DEST"

echo ""
echo "==> Done: $(ls -lh "$BIN" | awk '{print $5, $6, $7, $8, $9}')"
echo "==> Copied to: $DEST"
