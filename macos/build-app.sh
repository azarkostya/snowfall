#!/bin/bash
# Builds a universal (arm64 + x86_64) Snowfall.app, ad-hoc signs it and zips it.
# Output: macos/build/Snowfall.app and macos/build/Snowfall-macOS.zip
set -euo pipefail
cd "$(dirname "$0")"

swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/Snowfall"

APP=build/Snowfall.app
rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Snowfall"
cp Resources/Info.plist "$APP/Contents/Info.plist"

ICONSET=build/AppIcon.iconset
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s Resources/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z $d $d Resources/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

# Ad-hoc signature: required to run on Apple Silicon. Not notarized.
codesign --force --deep --sign - "$APP"

(cd build && ditto -c -k --keepParent Snowfall.app Snowfall-macOS.zip)
echo "Built $(pwd)/$APP"
