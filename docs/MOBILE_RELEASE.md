# Shipping the phone apps: TestFlight and Play internal testing

What the code already does for review, and the console steps that only the account holder can do,
in the order they have to happen. [MOBILE_PORT.md](MOBILE_PORT.md) explains how the apps are built;
this file is the route from a built app to a tester's phone.

## What the code covers (2026-09-28)

| Review requirement | Where |
| --- | --- |
| Account deletion inside the app (App Store 5.1.1(v), Play account deletion policy) | Settings → account card → Delete account, two steps. `DELETE /v1/account` wipes everything server-side; notes on the device stay. Also on desktop. |
| A web page for deletion requests (Play) | `https://daynote.arachat.cc/delete-account` (ko + en), linked from the privacy page. |
| Sign in with Apple beside Google on iOS (App Store 4.8) | Black button above Google on iOS only. `POST /v1/auth/apple`; Apple tokens are revoked when the account is deleted. |
| Privacy manifest (upload is refused without one) | `src/Daynote.Mobile.iOS/PrivacyInfo.xcprivacy`, copied to the bundle root. |
| No purchase outside the stores | No checkout on either phone; an existing subscription is shown read-only. |

An Apple account is a separate account from a Google one, even with the same address; identity is
the provider's subject, never the email. Desktop has no Apple sign-in, so someone who picks Apple on
the iPhone syncs between Apple devices only. Say so in the listing if it matters.

## Where things stand (2026-09-29)

| | |
| --- | --- |
| Worker | Deployed with migration 0009; Sign in with Apple configured (team `4T8C76SP99`, key `W2B29H98NT`) |
| App Store Connect | App "Daynote - 날짜별 노트", Apple ID `6817146422`, bundle `cc.arachat.daynote`; build 1.5.0 (2) processed and in TestFlight group "Daynote Internal" (all builds, account holder invited). Build 1 failed processing (ITMS-90683, fixed by trimming the SDK) |
| Signing (iOS) | "Apple Distribution: Jinhwa Jung (4T8C76SP99)" in the login keychain; profile "cc.arachat.daynote AppStore" in `~/.config/daynote/signing` |
| Google Play | Developer "Bread Jinhwa Jeong", internal testing 1.5.0 (2); opt-in `https://play.google.com/apps/internaltest/4701689771009472592` |
| Signing (Android) | Upload key `~/.config/daynote/android-upload.jks`, passwords in `android-upload.properties` next to it (source it before `Build-AndroidApp.sh`); upload SHA-1 `40:E1:D7:…:0D:AC`, Play app-signing SHA-1 `99:84:BA:…:25:1C` |

Uploads use the App Store Connect API key in `~/.config/schooling/asc_api_key.json` (same team):
`xcrun altool --upload-app -f <ipa> -t ios --apiKey <id> --apiIssuer <issuer> --p8-file-path <p8>`, then
`xcrun altool --build-status --delivery-id <uuid> …` until VALID — a processing failure arrives only there
(and by email), never as an upload error. Bump `ApplicationVersion` / `CFBundleVersion` (iOS) or
`ApplicationVersion` (Android versionCode) for every upload.

**Still to do before anyone outside the team:** the store listing, App Privacy / Data safety forms,
age rating and screenshots in both consoles (text in mobile-store-listing.md, images in docs/brand);
TestFlight external testing needs Beta App Review; a personal Play developer account must run a
closed test with at least 12 testers for 14 days before production.

## Before anything: deploy the Worker

The phones call endpoints that are not deployed yet. **Back up D1 first** — migration 0009 rebuilds
`users` and the five tables that reference it (see the comment at its top for why).

```
cd cloud/worker
npx wrangler d1 export daynote --remote --output=backup-before-0009.sql
npx wrangler d1 time-travel info daynote          # note the restore point too
npx wrangler d1 migrations apply daynote --remote # migrate BEFORE deploying: the new Worker needs 0009
npx wrangler deploy
```

**Set `PADDLE_API_KEY` in production** (`npx wrangler secret put PADDLE_API_KEY`). Without it the
Worker cannot cancel a running subscription, so deleting such an account is refused with
`subscription_active` rather than leaving someone billed for an account that is gone.

**0005 may already have lost data.** It rebuilt `users` the same way 0009 now avoids, and on D1
that `DROP TABLE users` cascades into every child table. If production held accounts with notes
when 0005 ran (around 2026-09-02), their server copies may have been emptied then. The notes on
each device would be untouched and re-upload on the next sign-in only if they were re-enrolled; a
count of `notes` rows against `users` rows on the remote database answers whether it happened.

Until the Apple values below are set, `/v1/auth/apple` answers 400 with a readable message and the
iPhone shows the generic "sync service reported a problem" line; Google sign-in and everything else
work unchanged.

## Apple

1. **Identifiers → `cc.arachat.daynote` → enable Sign in with Apple.** The app's
   `Entitlements.plist` already asks for it; a profile without it fails signing.
2. **Keys → new key with Sign in with Apple**, bound to that App ID. Download the `.p8` (once only).
   Put the Key ID and your Team ID into `APPLE_KEY_ID` / `APPLE_TEAM_ID` in `cloud/worker/wrangler.toml`,
   then `npx wrangler secret put APPLE_PRIVATE_KEY` with the file's contents, and deploy again.
3. **Certificates → Apple Distribution**, and **Profiles → App Store** for `cc.arachat.daynote`
   (after step 1, so the capability is in it).
4. Build and upload:
   ```
   DAYNOTE_IOS_SIGN_IDENTITY="Apple Distribution: …" DAYNOTE_IOS_PROVISIONING="<profile name>" \
     scripts/Build-IosApp.sh -t device
   xcrun altool --upload-app -f dist/ios/*.ipa -t ios --apiKey … --apiIssuer …   # or Transporter
   ```
   Release AOT takes tens of minutes.
5. **App Store Connect**: new app with bundle id `cc.arachat.daynote`; privacy policy URL
   `https://daynote.arachat.cc/privacy`; App Privacy — *Email address* and *Other user content*,
   both linked to the user, used for app functionality, not for tracking, collected only with an
   account. Export compliance: the app ships `ITSAppUsesNonExemptEncryption = false` (standard
   algorithms only: TLS, AES-GCM); confirm that answer is right for you before the first submission.
6. Add internal testers in TestFlight. A reviewer needs a way in: note in the review notes that
   sign-in is optional and the app is fully usable signed out.

## Google Play

1. **Upload key.** Create it once and keep it (and its password) somewhere safe; losing it means
   asking Google to reset it:
   ```
   keytool -genkeypair -v -keystore daynote-upload.jks -alias upload -keyalg RSA -keysize 4096 -validity 10000
   ```
2. Build the bundle:
   ```
   DAYNOTE_ANDROID_KEYSTORE=daynote-upload.jks DAYNOTE_ANDROID_KEY_ALIAS=upload \
   DAYNOTE_ANDROID_KEY_PASS=… DAYNOTE_ANDROID_STORE_PASS=… scripts/Build-AndroidApp.sh
   ```
3. **Play Console**: create the app, opt into Play App Signing, upload the `.aab` to *Internal testing*.
4. **No OAuth change is needed for the Play build.** Verified 2026-09-29: an APK signed with the
   upload key, whose SHA-1 is registered nowhere in Google Cloud, completed Google sign-in on the
   emulator. This flow (Custom Tab, PKCE, custom-scheme redirect, code redeemed by the Worker) never
   presents the app's certificate to Google, so Play's re-signing changes nothing. The fingerprint
   on the Android OAuth client matters only to Google Play services sign-in, which Daynote does not use.
5. App content: privacy policy URL as above; **account deletion URL**
   `https://daynote.arachat.cc/delete-account`; Data safety — email address and "other in-app
   content" (notes), collected only with an account, encrypted in transit, user can request deletion,
   not shared, not used for ads; ads: none; target audience: adults; content rating questionnaire.

## Store copy and images

- **Listing text, privacy labels, Data safety**: [mobile-store-listing.md](mobile-store-listing.md).
- **Screenshots**: `docs/brand/app-store/{ko,en}/` (1320×2868, iPhone 6.9") and
  `docs/brand/google-play/{ko,en}/` (1080×2160), five each. Re-render with
  `DAYNOTE_STORE_SHOTS=1 ./tests/Daynote.Mobile.Tests/bin/Debug/net10.0/Daynote.Mobile.Tests --filter "FullyQualifiedName~StoreScreenshot"`.
- **Phone wording**: the phone's account card never mentions buying — no trial countdown, no
  "subscribe", and the status reads "Files not synced" instead of "Files: subscription needed"
  (`AccountViewModel.IsPhone`).
