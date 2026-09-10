# Microsoft Store submission

How to take Daynote from this repo to a published Store listing. Steps marked **(you)**
can only be done in your Partner Center account; the rest are already wired in the repo.

## 0. One-time account setup **(you)**

1. Create or sign in to a **Partner Center** account and enroll as an app developer
   (individual or company). A company account is needed if you want the publisher
   display name to be your organization.
2. Have a **privacy policy URL** ready — the Store expects one for any app that stores
   user data. The cloud Worker serves one at **`https://daynote.arachat.cc/privacy`**,
   rendered from [PRIVACY.md](PRIVACY.md) itself, so updating the document and deploying
   the Worker republishes the policy; there is no page to maintain by hand. Use that URL
   unless you are shipping a build with no Worker deployed, in which case host the
   content of PRIVACY.md anywhere public. Key points it must state: Daynote stores only what
   you create (notes, attached day files, settings) locally in plaintext; it has no
   analytics or telemetry; and it makes no network call until you ask it to.
   Cloud sync ships as of 2026-09-08 (`DaynoteAppOptions.SyncEnabledByDefault` is `true`) and
   the package declares `internetClient`, so the listing has to declare: an account exists
   (Google sign-in), the Google account id and email address are collected, and note
   content is uploaded. Declare note content as personal data the publisher **can** access:
   it is encrypted in transit and at rest, but by default the service holds the key. The
   opt-in lock (CLOUD_SYNC.md §4.1b) removes that access for users who turn it on, which is
   worth describing in the listing but must not be used to claim the default is
   end-to-end-encrypted. See [CLOUD_SYNC.md §12](CLOUD_SYNC.md) for the checklist.

   Note sync is free for a signed-in account; **syncing images and files is a paid
   subscription** billed through Paddle, which policy 10.8.1 and 10.8.6 permit for a non-game
   PC app. Describe the split in the listing — a listing that says "sync is paid" now
   understates what the free app does. That permission comes with obligations, all of which
   are submission-blocking:

   - **Tick the third-party purchase API box** in Partner Center (10.8.2). It is a checkbox on
     the submission, not something certification infers.
   - **Account type**: 10.8.3 requires a Company account for a product that requires financial
     account information. Cloud sync is optional rather than primary functionality, so this may
     not bite — settle it with Partner Center **before** the first paid submission rather than
     discovering it in review.
   - **Listing content** (10.8.4): state the subscription price range and the trial terms,
     including that syncing stops when the trial ends. Say plainly that nothing is deleted.
   - **Purchase flow** (10.8.2): the purchase starts in the app and continues in the browser,
     which is what the settings panel does. The hosted page identifies Paddle as the commerce
     provider, which is the requirement.
   - **Never remove value** from an existing subscriber (10.8.6), and if the subscription is
     ever discontinued, keep serving it until each period expires. The Worker already refuses
     to shorten a paid period; the listing must not promise less.

## 1. Reserve the app + get its identity **(you)** — DONE

1. Partner Center → **Apps and games → New product → MSIX/PWA app**.
2. **Reserve the app name** (e.g. "Daynote").
3. Open **Product identity** and copy the three values Partner Center assigns:
   - **Package/Identity/Name** (e.g. `1234MyPublisher.Daynote`)
   - **Package/Identity/Publisher** (e.g. `CN=ABCD1234-...`)
   - **Publisher display name**

## 2. Put the identity into the manifest — DONE

The reserved values are already in the manifest: `BreadJinhwaJeong.-Daynote`,
`CN=7FDB7ABF-3343-4BA9-9F0C-C601ABED42EE`, "Bread Jinhwa Jeong". What still applies each
time is the version bump below.

In `packaging/Daynote.Package/Package.appxmanifest`, keep `Version` with a **4th part of
`0`** (the Store requires revision = 0) and bump the first three parts for each
submission (e.g. `1.5.0.0` → `1.6.0.0`).

> Alternative: open the solution in Visual Studio → right-click the packaging project
> → **Publish → Associate App with the Store**, sign in, and pick the reserved name.
> VS writes the identity for you (and a `Package.StoreAssociation.xml`).

## 3. Build the Store package

```powershell
scripts\Build-Package.ps1 -Store
```

This produces an **unsigned `.msixupload`** under `artifacts\` using
`UapAppxPackageBuildMode=StoreUpload` (a bundle). Do **not** sign it — the Store
re-signs with your app's Store identity. (The default, `-Store`-less run still makes
the self-signed sideload `.msix` for local testing.)

The script verifies two things about what it produced, and fails the build rather than
letting either reach Partner Center:

- the packaged application's version matches `Package.appxmanifest`;
- the co-located MCP server has every assembly its `deps.json` names.

`artifacts\` accumulates one `.msixupload` per version. **Upload the one matching the
version you just bumped to** — the older ones are still sitting there.

> **Bump the version before you build.** The `bin\...\Upload` tree that the Store path
> writes has been seen surviving an incremental build with the previous version inside
> it, producing a `.msixupload` whose *file name* carried the new version and whose
> *application package* carried the old one. Partner Center rejects that as a duplicate
> of the release you already shipped. The version check above catches it now; the fix is
> to delete `packaging\Daynote.Package\bin` and `\obj`, then build again.

## 4. Complete the submission **(you)**

In Partner Center for the reserved app:

- **Packages**: upload the `.msixupload`. It must target x64, min OS 10.0.19041.
- **Properties**: category (e.g. Productivity), and declare **Run at full trust**
  (`runFullTrust`) — justify it as a Win32 desktop app.
- **Age ratings**: complete the IARC questionnaire.
- **Store listing**: description, at least one **screenshot** (1366×768 or larger),
  the **privacy policy URL** from step 0, support contact.
- **Pricing & availability**: markets, price (free), release schedule.

Submit → Microsoft certification → published.

## Notes & gotchas

- **Full trust**: Daynote is a full-trust Win32 app (Desktop Bridge; the Avalonia shell
  `Daynote.Desktop` since the cutover). The Store allows this; certification may ask you
  to justify `runFullTrust`.
- **Storage**: this bullet used to say packaged storage meant uninstall removes user
  data. Both halves were wrong, and measured so (2026-09-07 and 2026-09-08): the Store
  build writes to the real `%LocalAppData%\Daynote`, and uninstalling leaves that folder
  intact. A sideload user switching to the Store build keeps their notes without a
  Backup/Restore round trip. Back up anyway — see
  [DATA_AND_RECOVERY.md](DATA_AND_RECOVERY.md).
- **Startup**: "start with Windows" is opt-in (Settings toggle); the app never
  auto-enables it, per Store policy.
- **x64 only**: no x86/Arm64 package is produced.
- **Network and data collection**: this bullet used to say the build makes no network
  calls, declares no `internetClient`, and should declare no data collection. All three
  became false when cloud sync shipped on 2026-09-08, and it contradicted §0 above —
  which is the dangerous kind of stale, because the wrong answer here is a false
  declaration rather than a broken build. The manifest declares `internetClient`, the app
  has accounts (Google sign-in), and the listing must declare the Google account id, the
  email address, and note content. The listing copy and every Partner Center answer are
  in [store-listing.md](store-listing.md).
