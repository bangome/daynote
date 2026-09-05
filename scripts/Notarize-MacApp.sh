#!/usr/bin/env bash
# Notarizes a Developer-ID-signed Daynote.app and staples the ticket, then zips it for download.
#
#   scripts/Notarize-MacApp.sh [-a dist/mac/Daynote.app] [-p daynote-notary]
#
# One-time setup (stores the credentials in your login keychain under the profile name):
#   xcrun notarytool store-credentials daynote-notary \
#     --apple-id "<apple-id email>" --team-id "<TEAMID>" --password "<app-specific password>"
# The bundle must have been produced by Build-MacApp.sh with DAYNOTE_SIGN_IDENTITY set to a
# "Developer ID Application: ..." identity; an ad-hoc signature is rejected by the notary service.
set -euo pipefail

APP="dist/mac/Daynote.app"; PROFILE="daynote-notary"
while getopts "a:p:" opt; do
  case $opt in
    a) APP="$OPTARG" ;; p) PROFILE="$OPTARG" ;;
    *) echo "usage: $0 [-a app] [-p keychain-profile]" >&2; exit 2 ;;
  esac
done

if codesign -dv "$APP" 2>&1 | grep -q 'Signature=adhoc'; then
  echo "error: $APP is ad-hoc signed. Rebuild with DAYNOTE_SIGN_IDENTITY set to a Developer ID identity." >&2
  exit 1
fi

ZIP="${APP%.app}-notarize.zip"
echo "==> zip for upload"
ditto -c -k --keepParent "$APP" "$ZIP"

echo "==> submit (waits for Apple's verdict)"
xcrun notarytool submit "$ZIP" --keychain-profile "$PROFILE" --wait

echo "==> staple"
xcrun stapler staple "$APP"
spctl --assess --type execute --verbose=2 "$APP"

FINAL="${APP%.app}.zip"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$FINAL"
echo "==> done: $FINAL"
