# Daynote on iOS and Android

The phone version of Daynote, built on Avalonia like the macOS app. Written 2026-09-25.

## Why Avalonia and not MAUI or native

`Daynote.Presentation` is ~7,800 lines of view models with no UI-framework reference at all, and
`Daynote.Core` plus `Daynote.Infrastructure` add another ~12,000 that are pure .NET. Avalonia reuses
every line of that *and* the desktop's palette and icon set, so a fix to the calendar, to search, to
autosave or to the sync engine lands on four platforms at once. MAUI would have reused the lower
layers but forced a second UI codebase; native SwiftUI + Compose would have meant reimplementing the
note parser, the sync protocol and the crypto twice.

The cost is that Avalonia draws its own controls, so platform feel is something this app has to
build rather than inherit. `Themes/Daynote.Mobile.Styles.axaml` is where that happens.

## Layout

```
src/Daynote.Mobile/            shared UI and composition, net10.0 — no Java, no UIKit
src/Daynote.Mobile.Android/    the Android head
src/Daynote.Mobile.iOS/        the iOS head
```

The shared project targets plain `net10.0` deliberately: it cannot reference a platform binding even
by accident, which keeps it buildable and testable on any machine. Everything platform-shaped
arrives through one record, `MobilePlatformServices`, that the head fills in at startup — the data
root, the secret protector, the identity provider, the browser, the top level.

## What the phone does not have

Five desktop features are absent, each because the platform has no such thing rather than because
the work is unfinished:

| Missing | Why |
| --- | --- |
| Open at login | Phone apps do not launch at boot. |
| Global hotkey | There is no system-wide key handler for an app. |
| MCP registration | There is no Claude Desktop on a phone to register with. |
| Updater | The store updates the app. |
| Backup archive | There is no user-visible data folder to export to or restore from. |

Attachments are still read and synced; the Files *panel* is gone, because attachments are read on
the note rather than browsed as a set.

## The screens

Four tabs, not three columns. The desktop shows the calendar, the editor and the right rail at once;
390 points of width cannot.

- **날짜 (Day)** — the month grid over the selected day's notes, with a floating new-note button.
  This is the desktop's left column promoted to a whole screen.
- **검색 (Search)** — its own screen, not a drop-down. A drop-down over a phone keyboard leaves about
  two rows visible.
- **목록 (Lists)** — to-dos, favourites and tags behind a segmented control: the right rail, read one
  panel at a time.
- **설정 (Settings)** — the account, the theme. See the table above for what is not there.

The editor is a layer over the day, not a tab. Leaving it flushes; so does backgrounding the app,
which on a phone is the last moment either OS guarantees the process runs.

## Sign-in: what still has to be set up

**Done: both client ids are set** (2026-09-27), the Worker exchanges phone codes, and sign-in has
been exercised on the Android emulator and the iOS Simulator against the deployed service. The steps
below are kept for re-creating a client. Release and Play-signed builds sign in with the same
client; the fingerprint turned out not to matter for this flow (see [MOBILE_RELEASE.md](MOBILE_RELEASE.md)).

There is no way to automate the first step: Google offers **no API for iOS and Android OAuth
clients**, and the only programmatic path it does offer issues web clients for IAP. So the console
step is by hand, and everything after it is one script.

1. **Two new OAuth clients in the Google console**, in the same project as the existing desktop
   client. Google binds a mobile client to the app's identity and rejects a call that mixes them up,
   so the desktop client id in `DaynoteAppOptions` cannot be reused.

   | | value |
   | --- | --- |
   | iOS bundle id | `cc.arachat.daynote` |
   | Android package name | `cc.arachat.daynote` |
   | Android SHA-1, debug | `BF:2D:90:66:F7:13:5C:8A:93:68:E2:58:E4:08:FC:89:AA:60:74:52` |

   **That fingerprint is not Android Studio's.** The .NET Android SDK keeps its own debug
   keystore, and an unsigned build gets that one - `~/.android/debug.keystore` is never consulted.
   Registering the Studio key instead produces a client that looks correct and refuses every
   sign-in, because Google checks the certificate the APK was actually signed with. Read the right
   one from the artifact rather than from a keystore path:

   ```
   JAVA_HOME="/Applications/Android Studio.app/Contents/jbr/Contents/Home" \
     $ANDROID_HOME/build-tools/*/apksigner verify --print-certs \
     dist/android/cc.arachat.daynote-Signed.apk
   ```

   `keytool -printcert -jarfile` does not work here: it only understands the v1 JAR signature and
   an APK signed with scheme v2/v3 comes back as "not a signed jar file".

   The console insists on a fingerprint when the client is created, but the browser flow never
   shows Google the app's certificate: a build signed with an unregistered key signs in just the
   same (tested 2026-09-29), so release and Play-signed builds need nothing extra.

   Neither client type has a client secret, so, as on the desktop, nothing secret ships in the app.

2. **Run the script** with whichever ids you got:

   ```
   scripts/Set-GoogleOAuthClients.sh --ios <id> --android <id>
   ```

   It writes all four places that have to agree: the constant in each head, the `CFBundleURLSchemes`
   entry in `Info.plist`, and `GOOGLE_IOS_CLIENT_ID` / `GOOGLE_ANDROID_CLIENT_ID` in
   `cloud/worker/wrangler.toml`. Passing `""` clears one and puts that platform back to local-only.

   The iOS redirect scheme is **not** the bundle id. An iOS client redirects to the *reversed client
   id* — `com.googleusercontent.apps.NNN-xyz` — and Google has deprecated the bundle-id form.
   `IosPlatformServices.CallbackScheme` derives it from the id so the two cannot drift, and the
   script writes the matching `Info.plist` entry. Android is different again and needs no scheme
   registered: a client there is identified by package name and certificate fingerprint, and
   redirects to the package-name scheme `AndroidManifest.xml` already routes through
   `AuthCallbackActivity`.

3. **Deploy and rebuild**: `npx wrangler deploy` in `cloud/worker`, then the head that changed. The
   Worker reads the ids from `wrangler.toml`, so it needs the deploy before a phone can sign in.

The Worker side is done. It takes a `client` field of `desktop`, `ios` or `android` on
`/v1/auth/google`, exchanges the code against that client, sends the secret only for the desktop one,
and checks the redirect against the shape that client can own — a loopback port for the desktop, a
private URI scheme for a phone. A platform with no id configured is refused with a message the app
can show rather than a 500. Covered by seven tests in `cloud/worker/test/auth.test.ts`.

**What was verified without a real client.** A build with a made-up id opens Chrome Custom Tabs on
`accounts.google.com` and gets back `401 invalid_client` — which is exactly what that id deserves,
and means the authorize URL, PKCE, the custom scheme and the tab are all correct. Everything up to
Google works; only the client itself is missing.

The flow itself is the sanctioned one on both platforms: PKCE plus a private-URI redirect (RFC 8252
§7.1), run in Custom Tabs on Android and `ASWebAuthenticationSession` on iOS. Not a web view —
Google blocks sign-in from embedded web views outright, because the host app can read what is typed
into one.

## Where the notes live

| | Path | Notes |
| --- | --- | --- |
| Android | `/data/data/cc.arachat.daynote/files/Daynote` | Private, backed up, wiped on uninstall. |
| iOS | `<container>/Library/Daynote` | Not Documents (the user could delete the database from the Files app) and not Caches (iOS evicts it). |

The sync session is sealed by the platform keystore, exactly as it is by DPAPI on Windows and the
Keychain on macOS. Android uses an AES-256 key generated inside AndroidKeyStore; iOS reuses
`MacKeychainSecretProtector` unchanged, because iOS and macOS ship the same Security.framework. If a
build ever has no sealed store, the app behaves as permanently signed out rather than writing a
refresh token in the clear.

## Subscriptions

The account card shows an existing subscription but offers **no checkout**. Apple and Google both
require their own in-app purchase for a digital subscription and reject an app that links out to a
web checkout, so the Paddle flow the desktop uses cannot appear here. Reading what a subscriber
already has is not selling, which is why the state is still shown.

Adding in-app purchase later means StoreKit 2 and Play Billing, plus a Worker endpoint that verifies
each platform's receipt against the same entitlement the Paddle webhook writes.

## Building

```bash
# Once, and it needs an administrator password because the SDK lives under /usr/local:
sudo dotnet workload install android ios

scripts/Build-IosApp.sh -t simulator     # no Apple account needed
scripts/Build-IosApp.sh                  # signed .ipa; set DAYNOTE_IOS_SIGN_IDENTITY, DAYNOTE_IOS_PROVISIONING
scripts/Build-AndroidApp.sh -f apk       # sideload
scripts/Build-AndroidApp.sh              # .aab for Play; set the four DAYNOTE_ANDROID_* variables
```

The shared project builds without either workload, which is the fastest way to check a UI change
compiles, and its tests render every screen headless:

```bash
dotnet build src/Daynote.Mobile/Daynote.Mobile.csproj
dotnet build tests/Daynote.Mobile.Tests/Daynote.Mobile.Tests.csproj
./tests/Daynote.Mobile.Tests/bin/Debug/net10.0/Daynote.Mobile.Tests
```

The PNGs land in `artifacts/mobile-screens`, one per screen per theme.

### Three things the Android build needs that nothing else in this repo does

Each cost a debugging session; all three are handled inside `scripts/Build-AndroidApp.sh`, so they
only matter to someone building the head by hand.

**A JDK.** The Android SDK will not look for one. The script uses `$JAVA_HOME`, else the JDK that
ships inside Android Studio.

**`dotnet publish`, never `dotnet build`.** Building the head produces an unusable mix of
RID-specific and RID-agnostic passes; publish is the supported path.

**The shared libraries built for the ABI first.** Publishing the head in one go makes the SDK build
each of `Daynote.Core`, `.Infrastructure`, `.Presentation` and `.Mobile` twice in the same
invocation, once plain and once for `android-arm64`, through one `obj` directory. The second pass
reports its compile as done and writes no assembly, and the publish then fails on a missing
`Daynote.Core.dll` (`MSB3030`). Building them for the ABI beforehand settles that output. It is also
why those four projects declare `android-arm64;android-x64` in `RuntimeIdentifiers`, and why the
head publishes **one ABI per invocation**.

## Two decisions the device made for us

**No edge-to-edge.** Avalonia reports `InsetsManager.SafeAreaPadding` on Android in physical pixels
while a `Thickness` is logical, so on a 3x display the 156-pixel status-bar inset became 156 logical
units and pushed the month header a fifth of the way down the screen. `DisplayEdgeToEdgePreference`
is off and the platform insets the window instead, which removes the unit question entirely. A notes
app gains nothing from bleeding colour behind the clock.

**Partial trimming, AOT on.** Core and Infrastructure serialize the sync payloads, the auth DTOs and
the backup manifest with reflection-based `System.Text.Json`. A fully trimmed build compiles and then
fails the first time a note syncs, so `TrimMode` is `partial`: only assemblies that opt in with
`IsTrimmable` are trimmed, and this app's are not. AOT stays on — it is trimming, not AOT, that
breaks reflection here. The real fix is a `JsonSerializerContext` over those payloads, which is
shared code the desktop also uses and so is its own change.

### Why the heads are not in Daynote.sln, and carry no lock file

Both follow from the same fact: a head's dependency graph is not the same on two machines.

`Daynote.sln` holds `Daynote.Mobile` and its tests but not `Daynote.Mobile.Android` or
`Daynote.Mobile.iOS`. The macOS and Windows workflows restore and build the whole solution and do
not install the mobile workloads, so a head in the solution failed them outright with NETSDK1147.
The mobile workflow restores the heads by path instead.

Neither head has a `packages.lock.json`. The workload supplies `Microsoft.NET.ILLink.Tasks` at
whatever version it happens to carry — 10.0.8 on one machine, 10.0.11 on a runner — and the RID
differs too: `iossimulator-arm64` on an Apple silicon Mac, `iossimulator-x64` elsewhere. A committed
lock file pinned both and then failed locked-mode restore for everyone who did not match. Every
project the heads reference is still locked; only the heads are not.

For the same reason `Build-IosApp.sh` puts the four shared lock files back after it runs: an iOS
build has to restore with a RID, and NuGet writes that RID into each of them, which the next
`dotnet restore Daynote.sln --locked-mode` rejects.

### And one the iOS Simulator needs

**A clean build.** An incremental build into an existing `-o` directory leaves a bundle whose
`_CodeSignature` no longer matches its contents, and the Simulator kills it at launch with
`Code Signature Invalid`. That reads as an app crash and is not one; the whole build is about ninety
seconds, so `scripts/Build-IosApp.sh` clears the app project's output every time. `simctl uninstall`
before `install` for the same reason.

## What has actually been run

- **Android**: built, installed on a Pixel 9 Pro emulator (API 36), launched, note created, typed
  into, autosaved to SQLite, back gesture closed the editor, and the note came back in the day list
  with its preview and the calendar's activity dot. 2026-09-26.
- **iOS**: built and run on an iPhone 17 Simulator, in Korean. Note created and typed into, the
  editor closed through the tab bar, all four tabs reached, the segmented Lists page rendered. A
  Release build AOT-compiles Avalonia and Skia and takes tens of minutes, so the Simulator target
  defaults to Debug.
- **Both themes** switch from the settings page, verified with a real tap on the emulator. A
  synthetic click does not move an Avalonia `ToggleSwitch` on the iOS Simulator, which is a quirk of
  injected events rather than of the control: `SettingsInteractionTests` covers the binding.
- **Shared UI**: 8 headless tests, both themes, no binding errors, every screen rendered to a PNG.
- **Nothing regressed**: the desktop app and its 51 UI tests, and the 41 Core tests, still pass.

## Before TestFlight and Play internal testing

Moved to [MOBILE_RELEASE.md](MOBILE_RELEASE.md), which lists what the code already covers for review
(account deletion, Sign in with Apple, the privacy manifest) and the console steps in order.
