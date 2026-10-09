#!/usr/bin/env python3
"""Creates the App Store provisioning profiles the iOS app and its extensions sign with.

    scripts/New-AppleGlanceProfiles.py [--key ~/.config/schooling/asc_api_key.json]

One profile per bundle in the IPA (docs/APPLE_EXTENSIONS.md §8): the app, its widget extension,
the watch app and the watch's widget extension. Each is named "Daynote Glance <bundle id>" and
installed into ~/Library/MobileDevice/Provisioning Profiles, where scripts/Build-IosApp.sh finds
it by that name.

Run it again after anything changes on an App ID — above all after the App Group
group.cc.arachat.daynote has been assigned to the four App IDs in the developer portal, which the
App Store Connect API cannot do: a profile carries the groups its App ID had when the profile was
made, and an old profile keeps the old list. Profiles this script made before are deleted first;
no other profile is touched.

The key file is JSON with key_id, issuer_id and key (the .p8 contents). Needs PyJWT.
"""
import argparse
import base64
import json
import os
import time
import urllib.error
import urllib.request

import jwt

BUNDLES = [
    "cc.arachat.daynote",
    "cc.arachat.daynote.widgets",
    "cc.arachat.daynote.watchkitapp",
    "cc.arachat.daynote.watchkitapp.widgets",
]
PREFIX = "Daynote Glance "
PROFILES = os.path.expanduser("~/Library/MobileDevice/Provisioning Profiles")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--key", default="~/.config/schooling/asc_api_key.json")
    parser.add_argument("--certificate-name", default="Apple Distribution")
    args = parser.parse_args()
    key = json.load(open(os.path.expanduser(args.key)))

    def call(method, path, body=None):
        now = int(time.time())
        token = jwt.encode(
            {"iss": key["issuer_id"], "iat": now, "exp": now + 900, "aud": "appstoreconnect-v1"},
            key["key"], algorithm="ES256", headers={"kid": key["key_id"], "typ": "JWT"})
        request = urllib.request.Request(
            "https://api.appstoreconnect.apple.com" + path,
            data=json.dumps(body).encode() if body is not None else None,
            method=method,
            headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(request) as response:
                raw = response.read()
                return json.loads(raw) if raw else {}
        except urllib.error.HTTPError as error:
            raise SystemExit(f"{method} {path} -> {error.code}: {error.read().decode()[:1000]}")

    certificates = [
        c for c in call("GET", "/v1/certificates?filter[certificateType]=DISTRIBUTION,IOS_DISTRIBUTION&limit=50")["data"]
        if c["attributes"]["name"].startswith(args.certificate_name)]
    if not certificates:
        raise SystemExit("No distribution certificate found.")

    os.makedirs(PROFILES, exist_ok=True)
    for identifier in BUNDLES:
        bundles = [b for b in call("GET", f"/v1/bundleIds?filter[identifier]={identifier}")["data"]
                   if b["attributes"]["identifier"] == identifier]
        if not bundles:
            raise SystemExit(f"App ID {identifier} does not exist.")
        bundle_id = bundles[0]["id"]

        name = PREFIX + identifier
        for old in call("GET", f"/v1/profiles?filter[name]={urllib.request.quote(name)}&limit=20")["data"]:
            call("DELETE", f"/v1/profiles/{old['id']}")
            # The installed copy too, or a lookup by name could find the dead one first.
            stale = os.path.join(PROFILES, old["attributes"]["uuid"] + ".mobileprovision")
            if os.path.exists(stale):
                os.remove(stale)

        profile = call("POST", "/v1/profiles", {"data": {
            "type": "profiles",
            "attributes": {"name": name, "profileType": "IOS_APP_STORE"},
            "relationships": {
                "bundleId": {"data": {"type": "bundleIds", "id": bundle_id}},
                "certificates": {"data": [{"type": "certificates", "id": c["id"]} for c in certificates]},
            }}})["data"]
        uuid = profile["attributes"]["uuid"]
        with open(os.path.join(PROFILES, uuid + ".mobileprovision"), "wb") as handle:
            handle.write(base64.b64decode(profile["attributes"]["profileContent"]))
        print(f"{identifier}: profile {profile['id']} '{name}' ({uuid})")


if __name__ == "__main__":
    main()
