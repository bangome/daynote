#!/usr/bin/env bash
# Builds the Android app from src/Daynote.Mobile.Android.
#
#   scripts/Build-AndroidApp.sh [-c Release] [-o dist/android] [-f aab|apk] [-r android-arm64]
#
# Produces a signed .aab for Play (the format Play requires for new uploads) or an .apk for
# sideloading onto a device. Signing uses the keystore named by the four DAYNOTE_ANDROID_*
# variables; without them the build is debug-signed, which installs on a device but cannot be
# uploaded.
set -euo pipefail

CONFIG="Release"; OUT="dist/android"; FORMAT="aab"; RID="android-arm64"
while getopts "c:o:f:r:" opt; do
  case $opt in
    c) CONFIG="$OPTARG" ;; o) OUT="$OPTARG" ;; f) FORMAT="$OPTARG" ;; r) RID="$OPTARG" ;;
    *) echo "usage: $0 [-c config] [-o outdir] [-f aab|apk] [-r rid]" >&2; exit 2 ;;
  esac
done

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/Daynote.Mobile.Android/Daynote.Mobile.Android.csproj"

# The Android SDK needs a JDK and does not find one by itself on a Mac with no system Java. Android
# Studio ships one, which is the JDK this build is tested against; JAVA_HOME wins if it is set.
JDK="${JAVA_HOME:-/Applications/Android Studio.app/Contents/jbr/Contents/Home}"
if [[ ! -x "$JDK/bin/java" ]]; then
  echo "No JDK at '$JDK'. Install Android Studio or set JAVA_HOME." >&2
  exit 1
fi

SIGN_ARGS=()
if [[ -n "${DAYNOTE_ANDROID_KEYSTORE:-}" ]]; then
  SIGN_ARGS=(
    -p:AndroidKeyStore=true
    -p:AndroidSigningKeyStore="$DAYNOTE_ANDROID_KEYSTORE"
    -p:AndroidSigningKeyAlias="${DAYNOTE_ANDROID_KEY_ALIAS:?set DAYNOTE_ANDROID_KEY_ALIAS}"
    -p:AndroidSigningKeyPass="${DAYNOTE_ANDROID_KEY_PASS:?set DAYNOTE_ANDROID_KEY_PASS}"
    -p:AndroidSigningStorePass="${DAYNOTE_ANDROID_STORE_PASS:?set DAYNOTE_ANDROID_STORE_PASS}"
  )
else
  echo "==> no DAYNOTE_ANDROID_KEYSTORE: debug-signed build (installs on a device, cannot be uploaded)"
fi

# The shared libraries are built for the ABI first, on their own.
#
# Not an optimisation. Publishing the head in one go makes the SDK build each of these twice in the
# same invocation, once plain and once for the ABI, through one obj directory; the second pass then
# reports its compile as done and writes no assembly, and the publish fails on a missing
# Daynote.Core.dll (MSB3030). Building them here settles the ABI-specific output before the head
# asks for it. Only the net10.0 flavour: Infrastructure also has a net10.0-windows one, which has
# no Android target at all.
echo "==> shared libraries ($RID, $CONFIG)"
for project in Daynote.Core Daynote.Infrastructure Daynote.Presentation Daynote.Mobile; do
  dotnet build "$ROOT/src/$project/$project.csproj" -c "$CONFIG" -f net10.0 \
    -p:RuntimeIdentifier="$RID" -p:SelfContained=true -nologo -v q
done

echo "==> publish ($RID, $CONFIG, $FORMAT)"
rm -rf "${ROOT:?}/$OUT"
dotnet publish "$PROJECT" -c "$CONFIG" -o "$ROOT/$OUT" -nologo -v q \
  -p:RuntimeIdentifier="$RID" \
  -p:AndroidPackageFormat="$FORMAT" \
  -p:JavaSdkDirectory="$JDK" \
  ${SIGN_ARGS+"${SIGN_ARGS[@]}"}

echo "==> done: $ROOT/$OUT"
find "$ROOT/$OUT" -maxdepth 1 \( -name '*.aab' -o -name '*.apk' \) -exec basename {} \;
