#!/usr/bin/env bash
# Builds Daynote.app for macOS from src/Daynote.Desktop.
#
#   scripts/Build-MacApp.sh [-r osx-arm64|osx-x64] [-c Release] [-o dist/mac] [-v 1.5.0]
#
# Produces a self-contained bundle (the .NET runtime is inside, nothing to install) with an .icns
# rendered from the brand favicon, and signs it. Set DAYNOTE_SIGN_IDENTITY to a "Developer ID
# Application: ..." identity for a distributable signature; without it the bundle is ad-hoc signed,
# which runs on this Mac but shows Gatekeeper's warning elsewhere. Notarization is a separate step
# (xcrun notarytool) that needs Apple credentials and is deliberately not automated here.
set -euo pipefail

RID="osx-arm64"; CONFIG="Release"; OUT="dist/mac"; VERSION="1.5.0"
while getopts "r:c:o:v:" opt; do
  case $opt in
    r) RID="$OPTARG" ;; c) CONFIG="$OPTARG" ;; o) OUT="$OPTARG" ;; v) VERSION="$OPTARG" ;;
    *) echo "usage: $0 [-r rid] [-c config] [-o outdir] [-v version]" >&2; exit 2 ;;
  esac
done

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/Daynote.Desktop/Daynote.Desktop.csproj"
PUBLISH="$ROOT/artifacts/mac-publish/$RID"
APP="$ROOT/$OUT/Daynote.app"
ICON_SRC="$ROOT/src/Daynote.App/Assets/Brand/daynote-favicon-v1.png"
BUNDLE_ID="cc.arachat.daynote"

echo "==> publish ($RID, $CONFIG)"
rm -rf "$PUBLISH"
dotnet publish "$PROJECT" -c "$CONFIG" -r "$RID" --self-contained \
  -p:PublishSingleFile=false -p:DebugType=none -o "$PUBLISH" -nologo -v q

echo "==> bundle"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Daynote.Desktop" "$APP/Contents/MacOS/Daynote.Mcp" 2>/dev/null || true

echo "==> icon"
ICONSET="$(mktemp -d)/Daynote.iconset"; mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z $size $size "$ICON_SRC" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size*2)) $((size*2)) "$ICON_SRC" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Daynote.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Daynote</string>
  <key>CFBundleDisplayName</key><string>Daynote</string>
  <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
  <key>CFBundleExecutable</key><string>Daynote.Desktop</string>
  <key>CFBundleIconFile</key><string>Daynote</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Daynote</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.productivity</string>
</dict>
</plist>
PLIST
printf 'APPL????' > "$APP/Contents/PkgInfo"

echo "==> sign"
ENTITLEMENTS="$(mktemp).plist"
cat > "$ENTITLEMENTS" <<'ENT'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <!-- .NET's JIT and the runtime need these under the hardened runtime. -->
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
  <key>com.apple.security.cs.allow-dyld-environment-variables</key><true/>
</dict></plist>
ENT
IDENTITY="${DAYNOTE_SIGN_IDENTITY:--}"
if [ "$IDENTITY" = "-" ]; then
  echo "    (ad-hoc: set DAYNOTE_SIGN_IDENTITY for a Developer ID signature)"
  codesign --force --deep --sign - --entitlements "$ENTITLEMENTS" "$APP"
else
  codesign --force --deep --options runtime --timestamp --sign "$IDENTITY" --entitlements "$ENTITLEMENTS" "$APP"
fi
codesign --verify --deep --strict "$APP"

echo "==> done: $APP ($(du -sh "$APP" | cut -f1))"
