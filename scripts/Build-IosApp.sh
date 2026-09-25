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

if [[ "$TARGET" == "simulator" ]]; then
  # The Simulator runs the host's architecture; arm64 on Apple silicon.
  RID="iossimulator-$(uname -m | sed 's/x86_64/x64/')"
  echo "==> build ($RID, $CONFIG)"
  # The app project's own output goes too, not just the destination. An incremental build into an
  # existing -o directory leaves a bundle whose _CodeSignature no longer matches its contents, and
  # the Simulator kills it on launch with "Code Signature Invalid" - which looks exactly like an app
  # crash and is not one. A clean build here costs about ninety seconds.
  rm -rf "$ROOT/$OUT" "$ROOT/src/Daynote.Mobile.iOS/bin" "$ROOT/src/Daynote.Mobile.iOS/obj"
  dotnet build "$PROJECT" -c "$CONFIG" -r "$RID" -o "$ROOT/$OUT" -nologo -v q
  echo "==> done: $ROOT/$OUT"
  echo "    xcrun simctl install booted \"$ROOT/$OUT/Daynote.Mobile.iOS.app\""
  exit 0
fi

echo "==> publish (ios-arm64, $CONFIG)"
rm -rf "$ROOT/$OUT"
dotnet publish "$PROJECT" -c "$CONFIG" -r ios-arm64 -o "$ROOT/$OUT" -nologo -v q \
  -p:ArchiveOnBuild=true \
  -p:CodesignKey="${DAYNOTE_IOS_SIGN_IDENTITY:?set DAYNOTE_IOS_SIGN_IDENTITY}" \
  -p:CodesignProvision="${DAYNOTE_IOS_PROVISIONING:?set DAYNOTE_IOS_PROVISIONING}"

echo "==> done: $ROOT/$OUT"
find "$ROOT/$OUT" -name '*.ipa' -maxdepth 2 || true
