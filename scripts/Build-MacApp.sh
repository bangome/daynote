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
#
# The desktop widgets (native/mac) need a team: a widget extension must be sandboxed, it shares its
# data with the app through an App Group, and an ad-hoc signature carries no team to own one. So
# they are built and embedded only when DAYNOTE_SIGN_IDENTITY is set (and Xcode is installed);
# otherwise the app is built without them and says so. DAYNOTE_TEAM_ID (default 4T8C76SP99) names
# the group; DAYNOTE_APP_PROFILE / DAYNOTE_WIDGET_PROFILE embed provisioning profiles when the
# distribution channel wants them. DAYNOTE_WIDGETS=0 leaves them out. docs/APPLE_EXTENSIONS.md §10.
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
IDENTITY="${DAYNOTE_SIGN_IDENTITY:--}"
TEAM_ID="${DAYNOTE_TEAM_ID:-4T8C76SP99}"
APP_GROUP="$TEAM_ID.group.$BUNDLE_ID"
case "$RID" in osx-x64) ARCH="x86_64" ;; *) ARCH="arm64" ;; esac

WIDGETS=0
if [ "${DAYNOTE_WIDGETS:-1}" = "0" ]; then
  :
elif [ "$IDENTITY" = "-" ]; then
  echo "    (no widgets: they need a team signature — set DAYNOTE_SIGN_IDENTITY)"
elif ! xcrun --find xcodebuild >/dev/null 2>&1; then
  echo "    (no widgets: Xcode is not installed)"
else
  WIDGETS=1
fi

echo "==> publish ($RID, $CONFIG)"
rm -rf "$PUBLISH"
dotnet publish "$PROJECT" -c "$CONFIG" -r "$RID" --self-contained \
  -p:PublishSingleFile=false -p:DebugType=none -o "$PUBLISH" -nologo -v q

echo "==> bundle"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Daynote.Desktop" "$APP/Contents/MacOS/Daynote.Mcp" 2>/dev/null || true

if [ "$WIDGETS" = "1" ]; then
  echo "==> widgets ($ARCH, group $APP_GROUP)"
  NATIVE="$ROOT/artifacts/mac-native/$RID"
  rm -rf "$NATIVE"
  for target in DaynoteWidgets DaynoteWidgetBridge; do
    xcodebuild -project "$ROOT/native/mac/DaynoteMac.xcodeproj" -target "$target" -configuration Release \
      ARCHS="$ARCH" ONLY_ACTIVE_ARCH=NO DEVELOPMENT_TEAM="$TEAM_ID" DAYNOTE_APP_GROUP="$APP_GROUP" \
      MARKETING_VERSION="$VERSION" CURRENT_PROJECT_VERSION="$VERSION" \
      SYMROOT="$NATIVE/build" OBJROOT="$NATIVE/obj" -quiet build
  done
  mkdir -p "$APP/Contents/PlugIns"
  cp -R "$NATIVE/build/Release/DaynoteWidgets.appex" "$APP/Contents/PlugIns/"
  # Beside the .NET runtime's own native libraries, where DllImport looks first.
  cp "$NATIVE/build/Release/libDaynoteWidgetBridge.dylib" "$APP/Contents/MacOS/"
fi

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
if [ "$WIDGETS" = "1" ]; then
  # The group the bridge looks for (Platform/MacWidgetBridge.cs), and the daynote:// links a
  # widget opens a day with.
  /usr/libexec/PlistBuddy -c "Add :DaynoteAppGroup string $APP_GROUP" \
    -c "Add :CFBundleURLTypes array" -c "Add :CFBundleURLTypes:0 dict" \
    -c "Add :CFBundleURLTypes:0:CFBundleURLName string $BUNDLE_ID.widgets" \
    -c "Add :CFBundleURLTypes:0:CFBundleURLSchemes array" \
    -c "Add :CFBundleURLTypes:0:CFBundleURLSchemes:0 string daynote" "$APP/Contents/Info.plist"
fi
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
if [ "$IDENTITY" = "-" ]; then
  echo "    (ad-hoc: set DAYNOTE_SIGN_IDENTITY for a Developer ID signature)"
  codesign --force --deep --sign - --entitlements "$ENTITLEMENTS" "$APP"
elif [ "$WIDGETS" = "0" ]; then
  codesign --force --deep --options runtime --timestamp --sign "$IDENTITY" --entitlements "$ENTITLEMENTS" "$APP"
else
  # Inside-out and without --deep: --deep would re-sign the extension with the app's entitlements
  # and strip its sandbox. Everything in MacOS first, then the extension, then the app.
  # The identity may be given by name or by its SHA-1; find-certificate only takes a name.
  CERT_NAME="$(security find-identity -v -p codesigning | grep -F "$IDENTITY" | head -1 | sed -n 's/.*"\(.*\)".*/\1/p' || true)"
  CERT_TEAM="$(security find-certificate -c "${CERT_NAME:-$IDENTITY}" -p 2>/dev/null | openssl x509 -noout -subject 2>/dev/null \
    | sed -n 's/.*OU *= *\([A-Z0-9]\{10\}\).*/\1/p' || true)"
  if [ -z "$CERT_TEAM" ]; then
    echo "error: cannot read the team of $IDENTITY; the App Group $APP_GROUP must belong to it" >&2
    exit 1
  elif [ "$CERT_TEAM" != "$TEAM_ID" ]; then
    echo "error: $IDENTITY belongs to team $CERT_TEAM, but the App Group is $APP_GROUP (DAYNOTE_TEAM_ID)" >&2
    exit 1
  fi
  /usr/libexec/PlistBuddy -c "Add :com.apple.security.application-groups array" \
    -c "Add :com.apple.security.application-groups:0 string $APP_GROUP" "$ENTITLEMENTS"
  WIDGET_ENTITLEMENTS="$(mktemp).plist"
  cat > "$WIDGET_ENTITLEMENTS" <<WENT
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.app-sandbox</key><true/>
  <key>com.apple.security.application-groups</key><array><string>$APP_GROUP</string></array>
</dict></plist>
WENT
  APPEX="$APP/Contents/PlugIns/DaynoteWidgets.appex"
  [ -n "${DAYNOTE_APP_PROFILE:-}" ] && cp "$DAYNOTE_APP_PROFILE" "$APP/Contents/embedded.provisionprofile"
  [ -n "${DAYNOTE_WIDGET_PROFILE:-}" ] && cp "$DAYNOTE_WIDGET_PROFILE" "$APPEX/Contents/embedded.provisionprofile"

  find "$APP/Contents/MacOS" -type f ! -name Daynote.Desktop -print0 | while IFS= read -r -d '' file; do
    case "$(file -b "$file")" in
      # Daynote.Mcp is a .NET host too, and needs the JIT entitlements as much as the app does.
      *Mach-O*executable*) codesign --force --options runtime --timestamp --sign "$IDENTITY" --entitlements "$ENTITLEMENTS" "$file" ;;
      # Libraries, and the managed assemblies the .NET layout keeps in MacOS too: codesign treats
      # everything there as code, which is what --deep did for them on the other path.
      *) codesign --force --options runtime --timestamp --sign "$IDENTITY" "$file" ;;
    esac
  done
  codesign --force --options runtime --timestamp --sign "$IDENTITY" --entitlements "$WIDGET_ENTITLEMENTS" "$APPEX"
  codesign --force --options runtime --timestamp --sign "$IDENTITY" --entitlements "$ENTITLEMENTS" "$APP"
fi
codesign --verify --deep --strict "$APP"

echo "==> done: $APP ($(du -sh "$APP" | cut -f1))"
