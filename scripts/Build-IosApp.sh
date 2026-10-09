#!/usr/bin/env bash
# Builds the iOS app from src/Daynote.Mobile.iOS.
#
#   scripts/Build-IosApp.sh [-c Release] [-o dist/ios] [-t device|simulator]
#
# device     -> a signed .ipa for TestFlight. Needs an Apple Developer account: set
#               DAYNOTE_IOS_SIGN_IDENTITY ("Apple Distribution: ...") and DAYNOTE_IOS_PROVISIONING
#               (the profile's name or UUID).
# simulator  -> an unsigned .app that runs in the Simulator, which needs no account at all and is
#               the fastest way to see a change.
#
# DAYNOTE_IOS_EXTENSIONS=1 also embeds the native Apple targets from native/apple
# (docs/APPLE_EXTENSIONS.md): the widget extension in PlugIns, the watch app (with its
# complications) in Watch, and DaynoteBridge in Frameworks, and gives the app the App Group
# entitlement. xcodebuild builds them first; the .NET build links the bridge; the bundles are then
# signed inside out — extensions, watch app, app — each with its own profile. The extensions' profiles
# are found by name, "Daynote Glance <bundle id>", as scripts/New-AppleGlanceProfiles.py makes them;
# the app's is DAYNOTE_IOS_PROVISIONING, as without extensions.
#
# Off by default, until the App Group group.cc.arachat.daynote exists in the developer portal and
# is assigned to the four App IDs: without that every profile grants an empty group list, and an
# IPA claiming the group is rejected by App Store processing. A device build with extensions checks
# the profiles for the group before it starts and stops if one lacks it.
set -euo pipefail

CONFIG=""; OUT="dist/ios"; TARGET="device"
while getopts "c:o:t:" opt; do
  case $opt in
    c) CONFIG="$OPTARG" ;; o) OUT="$OPTARG" ;; t) TARGET="$OPTARG" ;;
    *) echo "usage: $0 [-c config] [-o outdir] [-t device|simulator]" >&2; exit 2 ;;
  esac
done

# Debug for the Simulator unless asked otherwise. A Release build AOT-compiles Avalonia and Skia,
# which takes tens of minutes and buys nothing on a Simulator that can JIT; Debug turns a look-at-it
# loop from half an hour into a couple of minutes.
if [[ -z "$CONFIG" ]]; then
  if [[ "$TARGET" == "simulator" ]]; then CONFIG="Debug"; else CONFIG="Release"; fi
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/Daynote.Mobile.iOS/Daynote.Mobile.iOS.csproj"

# The shared projects' lock files, put back exactly as they were once this build is done.
#
# Not tidiness. An iOS build has to restore with a RID, and NuGet then records an
# "iossimulator-arm64" (or "ios-arm64") target in the lock file of every project it pulls in. A
# plain net10.0 project cannot declare that RID to match, because iOS runtime packs only resolve
# under a net10.0-ios target — so the next `dotnet restore Daynote.sln --locked-mode`, which the
# macOS and Windows workflows both run, fails with NU1004 on a file this build quietly edited.
# Nothing downstream needs the iOS target recorded, so the build borrows the files and returns them.
SHARED_LOCKS=(
  "$ROOT/src/Daynote.Core/packages.lock.json"
  "$ROOT/src/Daynote.Infrastructure/packages.lock.json"
  "$ROOT/src/Daynote.Presentation/packages.lock.json"
  "$ROOT/src/Daynote.Motion/packages.lock.json"
  "$ROOT/src/Daynote.Mobile/packages.lock.json"
)
LOCK_BACKUP="$(mktemp -d)"
save_locks() {
  local i=0
  for lock in "${SHARED_LOCKS[@]}"; do
    [[ -f "$lock" ]] && cp "$lock" "$LOCK_BACKUP/$i.json"
    i=$((i + 1))
  done
}
restore_locks() {
  local i=0
  for lock in "${SHARED_LOCKS[@]}"; do
    [[ -f "$LOCK_BACKUP/$i.json" ]] && cp "$LOCK_BACKUP/$i.json" "$lock"
    i=$((i + 1))
  done
  rm -rf "$LOCK_BACKUP"
}
trap restore_locks EXIT
save_locks

NATIVE="$ROOT/native/apple"
NATIVE_BUILD="$NATIVE/build"
TEAM="4T8C76SP99"
WITH_EXTENSIONS="${DAYNOTE_IOS_EXTENSIONS:-0}"
GROUP="group.cc.arachat.daynote"
GLANCE_PROPS=()
[[ "$WITH_EXTENSIONS" == "1" ]] && GLANCE_PROPS=(-p:DaynoteGlance=true)

# The Swift targets. For a device, unsigned: signing happens once they are inside the app. For the
# Simulator, signed ad hoc by Xcode, which is what writes their entitlements into the binary where
# the Simulator reads them: macOS refuses to launch an ad hoc signature that claims an App Group.
build_native() {
  local config="$1" ios_sdk="$2" watch_sdk="$3"
  echo "==> native targets ($config, $ios_sdk, $watch_sdk)"
  rm -rf "$NATIVE_BUILD"
  local signing=(CODE_SIGNING_ALLOWED=NO)
  [[ "$ios_sdk" == "iphonesimulator" ]] && signing=(CODE_SIGN_IDENTITY=- CODE_SIGN_STYLE=Manual DEVELOPMENT_TEAM=)
  local common=(-project "$NATIVE/Daynote.xcodeproj" -configuration "$config" "${signing[@]}"
    "SYMROOT=$NATIVE_BUILD" "OBJROOT=$NATIVE_BUILD/obj" -quiet)
  xcodebuild build "${common[@]}" -sdk "$ios_sdk" -target DaynoteBridge -target DaynoteWidgets
  xcodebuild build "${common[@]}" -sdk "$watch_sdk" -target DaynoteWatch
}

# The entitlements a bundle is signed with: its own file, plus what an App Store profile grants
# every bundle (the identifier, the team) and nothing the profile does not.
entitlements_for() {
  local source="$1" bundle_id="$2" out="$3"
  cp "$source" "$out"
  /usr/libexec/PlistBuddy -c "Add :application-identifier string $TEAM.$bundle_id" "$out"
  /usr/libexec/PlistBuddy -c "Add :com.apple.developer.team-identifier string $TEAM" "$out"
  /usr/libexec/PlistBuddy -c "Add :get-task-allow bool false" "$out"
}

# An installed profile by name or UUID.
profile_file() {
  local wanted="$1" file decoded
  for file in "$HOME/Library/MobileDevice/Provisioning Profiles/"*.mobileprovision; do
    decoded="$(security cms -D -i "$file" 2>/dev/null)" || continue
    if [[ "$(plutil -extract Name raw -o - - <<<"$decoded" 2>/dev/null)" == "$wanted" \
       || "$(plutil -extract UUID raw -o - - <<<"$decoded" 2>/dev/null)" == "$wanted" ]]; then
      echo "$file"
      return 0
    fi
  done
  echo "error: no installed provisioning profile '$wanted'" >&2
  return 1
}

# The profile a bundle signs with: the app's is DAYNOTE_IOS_PROVISIONING, the extensions' their own.
profile_named() {
  if [[ "$1" == "cc.arachat.daynote" ]]; then
    profile_file "$DAYNOTE_IOS_PROVISIONING"
  else
    profile_file "Daynote Glance $1" || { echo "  run scripts/New-AppleGlanceProfiles.py" >&2; return 1; }
  fi
}

# Stops before a half-hour build that would end in an IPA App Store processing rejects.
check_group_in_profiles() {
  local bundle file
  for bundle in cc.arachat.daynote cc.arachat.daynote.widgets cc.arachat.daynote.watchkitapp cc.arachat.daynote.watchkitapp.widgets; do
    file="$(profile_named "$bundle")"
    if ! security cms -D -i "$file" 2>/dev/null \
         | plutil -extract Entitlements.com\.apple\.security\.application-groups xml1 -o - - 2>/dev/null \
         | grep -q "<string>$GROUP</string>"; then
      echo "error: the profile for $bundle does not grant $GROUP." >&2
      echo "  Create the App Group in the developer portal, assign it to the four App IDs, and run" >&2
      echo "  scripts/New-AppleGlanceProfiles.py (and regenerate DAYNOTE_IOS_PROVISIONING) — or build" >&2
      echo "  with DAYNOTE_IOS_EXTENSIONS=0. docs/APPLE_EXTENSIONS.md §2." >&2
      return 1
    fi
  done
}

# Copies the widget extension and the watch app into APP and signs them, then APP itself, inside
# out. IDENTITY "-" signs ad hoc for the Simulator; anything else is a device identity and each
# bundle gets its profile embedded.
embed_native() {
  local app="$1" platform="$2" identity="$3"
  local ios_dir watch_dir
  if [[ "$platform" == "device" ]]; then
    ios_dir="$NATIVE_BUILD/Release-iphoneos"; watch_dir="$NATIVE_BUILD/Release-watchos"
  else
    ios_dir="$NATIVE_BUILD/Debug-iphonesimulator"; watch_dir="$NATIVE_BUILD/Debug-watchsimulator"
  fi

  echo "==> embedding widgets and watch app"
  mkdir -p "$app/PlugIns" "$app/Watch"
  rm -rf "$app/PlugIns/DaynoteWidgets.appex" "$app/Watch/DaynoteWatch.app"
  ditto "$ios_dir/DaynoteWidgets.appex" "$app/PlugIns/DaynoteWidgets.appex"
  ditto "$watch_dir/DaynoteWatch.app" "$app/Watch/DaynoteWatch.app"

  local work; work="$(mktemp -d)"
  # On a device, the app's own entitlements as the .NET build signed it, so the re-sign keeps
  # exactly those. A Simulator build carries none in its signature, so it gets the project's file.
  if [[ "$platform" == "device" ]]; then
    codesign -d --entitlements "$work/app.plist" --xml "$app" >/dev/null 2>&1
  else
    cp "$ROOT/src/Daynote.Mobile.iOS/Entitlements.plist" "$work/app.plist"
  fi

  sign_bundle() {
    local bundle="$1" bundle_id="$2" entitlements="$3"
    # Xcode already signed the Simulator's copies, entitlements and all.
    [[ "$identity" == "-" ]] && return 0
    if [[ "$identity" != "-" ]]; then
      local profile; profile="$(profile_named "$bundle_id")"
      cp "$profile" "$bundle/embedded.mobileprovision"
      entitlements_for "$entitlements" "$bundle_id" "$work/$bundle_id.plist"
      entitlements="$work/$bundle_id.plist"
    fi
    codesign --force --sign "$identity" --entitlements "$entitlements" --timestamp=none --generate-entitlement-der "$bundle"
  }

  local watch="$app/Watch/DaynoteWatch.app"
  sign_bundle "$watch/PlugIns/DaynoteWatchWidgets.appex" cc.arachat.daynote.watchkitapp.widgets "$NATIVE/WatchWidgets/DaynoteWatchWidgets.entitlements"
  sign_bundle "$watch" cc.arachat.daynote.watchkitapp "$NATIVE/Watch/DaynoteWatch.entitlements"
  sign_bundle "$app/PlugIns/DaynoteWidgets.appex" cc.arachat.daynote.widgets "$NATIVE/Widgets/DaynoteWidgets.entitlements"
  if [[ "$identity" != "-" ]]; then
    cp "$(profile_named cc.arachat.daynote)" "$app/embedded.mobileprovision"
  fi
  if [[ "$identity" == "-" ]]; then
    # The .NET build put the app's entitlements in its binary; the seal only has to cover the new contents.
    codesign --force --sign - --timestamp=none "$app"
  else
    codesign --force --sign "$identity" --entitlements "$work/app.plist" --timestamp=none --generate-entitlement-der "$app"
  fi
  codesign --verify --deep --strict "$app"
  rm -rf "$work"
}

if [[ "$TARGET" == "simulator" ]]; then
  # The Simulator runs the host's architecture; arm64 on Apple silicon.
  RID="iossimulator-$(uname -m | sed 's/x86_64/x64/')"
  echo "==> build ($RID, $CONFIG)"
  # The app project's own output goes too, not just the destination. An incremental build into an
  # existing -o directory leaves a bundle whose _CodeSignature no longer matches its contents, and
  # the Simulator kills it on launch with "Code Signature Invalid" - which looks exactly like an app
  # crash and is not one. A clean build here costs about ninety seconds.
  rm -rf "$ROOT/$OUT" "$ROOT/src/Daynote.Mobile.iOS/bin" "$ROOT/src/Daynote.Mobile.iOS/obj"
  [[ "$WITH_EXTENSIONS" == "1" ]] && build_native Debug iphonesimulator watchsimulator
  dotnet build "$PROJECT" -c "$CONFIG" -r "$RID" -o "$ROOT/$OUT" -nologo -v q ${GLANCE_PROPS[@]+"${GLANCE_PROPS[@]}"}
  [[ "$WITH_EXTENSIONS" == "1" ]] && embed_native "$ROOT/$OUT/Daynote.Mobile.iOS.app" simulator -
  echo "==> done: $ROOT/$OUT"
  echo "    xcrun simctl install booted \"$ROOT/$OUT/Daynote.Mobile.iOS.app\""
  exit 0
fi

echo "==> publish (ios-arm64, $CONFIG)"
# The linker caches its resolved publish graph under obj. Keeping that cache can resurrect an
# assembly that a newly added packaging target removed, producing an IPA that does not match the
# current project file. A distribution build must always start from a clean app-head graph.
rm -rf "$ROOT/$OUT" "$ROOT/src/Daynote.Mobile.iOS/bin" "$ROOT/src/Daynote.Mobile.iOS/obj"
if [[ "$WITH_EXTENSIONS" == "1" ]]; then
  : "${DAYNOTE_IOS_PROVISIONING:?set DAYNOTE_IOS_PROVISIONING}"
  check_group_in_profiles
  build_native Release iphoneos watchos
fi
dotnet publish "$PROJECT" -c "$CONFIG" -r ios-arm64 -o "$ROOT/$OUT" -nologo -v q \
  -p:ArchiveOnBuild=true \
  -p:CodesignKey="${DAYNOTE_IOS_SIGN_IDENTITY:?set DAYNOTE_IOS_SIGN_IDENTITY}" \
  -p:CodesignProvision="${DAYNOTE_IOS_PROVISIONING:?set DAYNOTE_IOS_PROVISIONING}" ${GLANCE_PROPS[@]+"${GLANCE_PROPS[@]}"}

IPA="$(find "$ROOT/$OUT" -maxdepth 2 -name '*.ipa' -print -quit)"
if [[ -z "$IPA" ]]; then
  echo "error: publish completed without producing an IPA" >&2
  exit 1
fi
if unzip -Z1 "$IPA" | grep -q 'Avalonia\.DesignerSupport'; then
  echo "error: IPA contains Avalonia.DesignerSupport, which crashes trimmed iOS device builds" >&2
  exit 1
fi

if [[ "$WITH_EXTENSIONS" == "1" ]]; then
  # The IPA is the .NET build's, signed; open it, put the extensions in, sign it again inside out.
  STAGE="$(mktemp -d)"
  unzip -q "$IPA" -d "$STAGE"
  embed_native "$(find "$STAGE/Payload" -maxdepth 1 -name '*.app' -print -quit)" device "$DAYNOTE_IOS_SIGN_IDENTITY"
  rm -f "$IPA"
  (cd "$STAGE" && zip -qr -y "$IPA" Payload $(ls -d SwiftSupport 2>/dev/null))
  rm -rf "$STAGE"
fi

echo "==> done: $ROOT/$OUT"
echo "$IPA"
