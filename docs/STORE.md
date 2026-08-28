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
   analytics or telemetry; and it makes no network calls unless the user opts into cloud
   sync and signs in.
   If the build you submit has cloud sync enabled, the listing also has to declare that
   an account exists, that an email address is collected, and that note content is
   uploaded — encrypted on the device, but uploaded. A build with no sync endpoint
   configured makes no network calls at all and can be declared as such.

## 1. Reserve the app + get its identity **(you)**

1. Partner Center → **Apps and games → New product → MSIX/PWA app**.
2. **Reserve the app name** (e.g. "Daynote").
3. Open **Product identity** and copy the three values Partner Center assigns:
   - **Package/Identity/Name** (e.g. `1234MyPublisher.Daynote`)
   - **Package/Identity/Publisher** (e.g. `CN=ABCD1234-...`)
   - **Publisher display name**

## 2. Put the identity into the manifest

Edit `packaging/Daynote.Package/Package.appxmanifest` and replace the three
`PLACEHOLDER-…` values under `<Identity>` and `<Properties>` with the values from
step 1. Keep `Version` with a **4th part of `0`** (Store requires revision = 0) and
bump the first three parts for each submission (e.g. `1.0.4.0` → `1.0.5.0`).

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

- **Full trust**: Daynote is a full-trust WPF/Win32 app (Desktop Bridge). The Store
  allows this; certification may ask you to justify `runFullTrust`.
- **Storage change**: the Store build uses packaged storage, so **uninstall removes
  user data**. Existing sideload users should **Backup** (Settings → 백업 및 복원)
  before switching, then **Restore** after installing the Store build — their old
  `%LocalAppData%\Daynote` path is not shared with the packaged install.
- **Startup**: "start with Windows" is opt-in (Settings toggle); the app never
  auto-enables it, per Store policy.
- **x64 only**: no x86/Arm64 package is produced.
