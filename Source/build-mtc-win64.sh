#!/usr/bin/env bash
# build-mtc-win64.sh — clean publish MTC for win-x64
# Run this after any code change to get a fresh standalone Windows build.
set -euo pipefail

cd "$(dirname "$0")"

echo "==> Cleaning..."
rm -rf bin obj MTC/bin MTC/obj

echo "==> Publishing..."
AVALONIA_TELEMETRY_OPTOUT=1 dotnet publish MTC/MTC.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PublishTrimmed=false \
    2>&1 | grep -v "^$"

BIN="MTC/bin/Release/net10.0/win-x64/publish/MTC.exe"

# Also copy to the shortcut path the user runs from
DEST_DIR="MTC/publish/win-x64"
DEST="$DEST_DIR/MTC.exe"
rm -rf "$DEST_DIR"
mkdir -p "$DEST_DIR"
cp "$BIN" "$DEST"

if [ -f "MTC/bin/Release/net10.0/win-x64/publish/MTC.pdb" ]; then
    cp "MTC/bin/Release/net10.0/win-x64/publish/MTC.pdb" "$DEST_DIR/MTC.pdb"
fi

if [ -f "bin/Release/net10.0/TWXProxy.pdb" ]; then
    cp "bin/Release/net10.0/TWXProxy.pdb" "$DEST_DIR/TWXProxy.pdb"
fi

echo ""
echo "==> Done: $(ls -lh "$DEST" | awk '{print $5, $6, $7, $8, $9}')"
echo "==> Copied to: $DEST"
