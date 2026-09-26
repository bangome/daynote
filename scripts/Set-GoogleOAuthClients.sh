#!/usr/bin/env bash
# Writes the two mobile Google OAuth client ids into every place that has to agree on them.
#
#   scripts/Set-GoogleOAuthClients.sh --ios <id> --android <id>
#
# Either flag may be given alone. Pass "" to clear one, which puts that platform back to
# local-only: with no client id the app registers no identity provider and the account section
# is simply absent, rather than showing a sign-in button that cannot work.
#
# The clients themselves have to be created by hand in the Google console. Google offers no API
# for iOS and Android OAuth clients - the only programmatic path is the IAP-specific one, which
# issues web clients - so this script starts where the console leaves off.
#
# Four files have to hold the same values and drift silently if they do not:
#   src/Daynote.Mobile.iOS/Platform/IosPlatformServices.cs      the id the app authorizes with
#   src/Daynote.Mobile.iOS/Info.plist                           the scheme iOS registers at install
#   src/Daynote.Mobile.Android/Platform/AndroidPlatformServices.cs
#   cloud/worker/wrangler.toml                                  the id the Worker exchanges with
#
# Neither id is a secret: an installed app cannot keep one, and Google does not treat them as
# secret. The client secret belongs to the desktop client alone and lives only in the Worker.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IOS_ID=""; ANDROID_ID=""; SET_IOS=0; SET_ANDROID=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --ios)     IOS_ID="${2-}";     SET_IOS=1;     shift 2 ;;
    --android) ANDROID_ID="${2-}"; SET_ANDROID=1; shift 2 ;;
    -h|--help) sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [[ $SET_IOS -eq 0 && $SET_ANDROID -eq 0 ]]; then
  echo "nothing to do: pass --ios and/or --android" >&2
  exit 2
fi

# A client id, or empty to clear. Checked here rather than at the first HTTP round trip, because a
# typo otherwise surfaces as Google's "invalid_client" on a phone with no console attached.
validate() {
  local kind="$1" id="$2"
  [[ -z "$id" ]] && return 0
  if [[ ! "$id" =~ ^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$ ]]; then
    echo "not a Google client id ($kind): $id" >&2
    echo "expected the form 123456789-abcdef.apps.googleusercontent.com" >&2
    exit 1
  fi
}

# The redirect scheme an iOS client accepts: the id with its dot-separated parts reversed. The
# scheme equal to the bundle id used to work too and Google has deprecated it.
reverse_client_id() {
  awk -F. '{ for (i = NF; i > 0; i--) printf "%s%s", $i, (i > 1 ? "." : "\n") }' <<< "$1"
}

# Replaces the initializer of a `public const string NAME = "...";` line, whatever it holds now.
set_constant() {
  local file="$1" name="$2" value="$3"
  grep -q "const string $name" "$file" || { echo "no $name in $file" >&2; exit 1; }
  python3 - "$file" "$name" "$value" <<'PY'
import io, re, sys
path, name, value = sys.argv[1], sys.argv[2], sys.argv[3]
text = io.open(path, encoding='utf-8').read()
pattern = re.compile(r'(const string %s\s*=\s*)"[^"]*"' % re.escape(name))
text, count = pattern.subn(lambda m: '%s"%s"' % (m.group(1), value), text, count=1)
assert count == 1, 'no match for %s in %s' % (name, path)
io.open(path, 'w', encoding='utf-8').write(text)
PY
  echo "  $(basename "$file"): $name = \"${value:-}\""
}

set_toml_var() {
  local file="$1" name="$2" value="$3"
  python3 - "$file" "$name" "$value" <<'PY'
import io, re, sys
path, name, value = sys.argv[1], sys.argv[2], sys.argv[3]
text = io.open(path, encoding='utf-8').read()
pattern = re.compile(r'(?m)^(%s\s*=\s*)"[^"]*"$' % re.escape(name))
text, count = pattern.subn(lambda m: '%s"%s"' % (m.group(1), value), text, count=1)
assert count == 1, 'no match for %s in %s' % (name, path)
io.open(path, 'w', encoding='utf-8').write(text)
PY
  echo "  wrangler.toml: $name = \"${value:-}\""
}

# The single <string> inside CFBundleURLSchemes, replaced in place. Deliberately NOT PlistBuddy:
# it rewrites the whole document and strips every XML comment, and this plist is half comments
# explaining why each key is there. plutil then checks the result is still a valid plist, which
# is what PlistBuddy was wanted for - a malformed Info.plist fails the build with no useful
# message.
set_url_scheme() {
  local file="$1" scheme="$2"
  python3 - "$file" "$scheme" <<'PLIST'
import io, re, sys
path, scheme = sys.argv[1], sys.argv[2]
text = io.open(path, encoding='utf-8').read()
pattern = re.compile(r'(<key>CFBundleURLSchemes</key>\s*<array>\s*<string>)[^<]*(</string>)')
text, count = pattern.subn(lambda m: m.group(1) + scheme + m.group(2), text, count=1)
assert count == 1, 'no CFBundleURLSchemes string in %s' % path
io.open(path, 'w', encoding='utf-8').write(text)
PLIST
  plutil -lint "$file" > /dev/null
  echo "  Info.plist: CFBundleURLSchemes = $scheme"
}

if [[ $SET_IOS -eq 1 ]]; then
  validate iOS "$IOS_ID"
  echo "iOS:"
  set_constant "$ROOT/src/Daynote.Mobile.iOS/Platform/IosPlatformServices.cs" GoogleIosClientId "$IOS_ID"
  # Cleared: park the scheme on the bundle id so the plist stays valid and registers nothing useful.
  scheme="cc.arachat.daynote"
  [[ -n "$IOS_ID" ]] && scheme="$(reverse_client_id "$IOS_ID")"
  set_url_scheme "$ROOT/src/Daynote.Mobile.iOS/Info.plist" "$scheme"
  set_toml_var "$ROOT/cloud/worker/wrangler.toml" GOOGLE_IOS_CLIENT_ID "$IOS_ID"
fi

if [[ $SET_ANDROID -eq 1 ]]; then
  validate Android "$ANDROID_ID"
  echo "Android:"
  set_constant "$ROOT/src/Daynote.Mobile.Android/Platform/AndroidPlatformServices.cs" GoogleAndroidClientId "$ANDROID_ID"
  # No scheme to write: an Android client is identified by package name and certificate
  # fingerprint, and redirects to the package-name scheme already in AndroidManifest.xml.
  set_toml_var "$ROOT/cloud/worker/wrangler.toml" GOOGLE_ANDROID_CLIENT_ID "$ANDROID_ID"
fi

cat <<'NEXT'

Then, to make it live:
  cd cloud/worker && npx wrangler deploy     # the Worker reads the ids from wrangler.toml
  scripts/Build-AndroidApp.sh                # rebuild whichever head changed
  scripts/Build-IosApp.sh -t simulator
NEXT
