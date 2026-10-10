# Deploying the Daynote cloud worker

Current state, 2026-09-02:

| Thing | State |
| --- | --- |
| Worker `daynote-cloud` | deployed and answering on `https://daynote.arachat.cc` |
| Brand site (`cloud/site`) | served by the same Worker via `[assets]`; `npm run deploy` rebuilds it first — see README §Brand site |
| D1 `daynote` | `4226d475-a071-44c0-a2c7-4a953cbaa44e` (APAC), migrations 0001–0006 |
| `JWT_SECRET` | set |
| `GOOGLE_CLIENT_ID` (var) | set in `wrangler.toml` |
| `GOOGLE_CLIENT_SECRET` (secret) | **must be set** — `wrangler secret put GOOGLE_CLIENT_SECRET` |
| `DEK_WRAP_KEY` (secret) | **must be set** — `wrangler secret put DEK_WRAP_KEY` |
| `APPLE_BUNDLE_ID` (var) | set in `wrangler.toml` (`cc.arachat.daynote`) |
| `APPLE_TEAM_ID`, `APPLE_KEY_ID` (vars), `APPLE_PRIVATE_KEY` (secret) | **empty** — Apple sign-in is refused until they are set; see §2c |
| Migration `0009_apple_and_deletion.sql` | **must be applied before** deploying the Worker that serves `/v1/auth/apple` and `DELETE /v1/account` — see §2c |
| `PADDLE_WEBHOOK_SECRET`, `PADDLE_API_KEY` (secrets) | **must be set** for subscriptions — see §2b |
| `PADDLE_PRICE_ID_MONTHLY`, `PADDLE_PRICE_ID_ANNUAL` (vars) | Pro — **empty on purpose** in `wrangler.toml` (₩2,900 / $2.49 monthly, ₩24,000 / $19.99 annual; the verified ids are in the comment there) — see §2b |
| `PADDLE_PRICE_ID_PREMIUM_MONTHLY`, `PADDLE_PRICE_ID_PREMIUM_ANNUAL` (vars) | Premium — **empty**; the Paddle prices do not exist yet (₩5,900 / $4.99 monthly, ₩48,000 / $39.99 annual) — see §2b |
| Migration `0010_tiers.sql` | **must be applied before** deploying the Worker that reads `subscriptions.tier` and `users.quota_override_bytes` — see §2b |
| Google consent screen | Testing or Production — see §2 |
| `workers_dev` | false, `preview_urls` false — only the custom domain answers |
| The app | **does not use this service.** Cloud sync is held back; see §3 |

Sign-in is Google OAuth as of migration `0004_oauth.sql`. The password endpoints, the recovery key,
the reset flow, Resend, and its DNS records are all gone — see
[docs/CLOUD_SYNC.md §1](../../docs/CLOUD_SYNC.md). The trade that came with it: the Worker generates
and holds each account's data key, so it can read the notes it stores.

## 1. Attach the hostname — done

Already attached; `curl -s https://daynote.arachat.cc/v1/health` returns `{"ok":true,...}`. Kept here
because it needs `zone:edit` on `arachat.cc`, which the OAuth token used for everything else does not
have, so a rebuild from scratch runs into it again. In the dashboard: **Workers & Pages → daynote-cloud → Settings → Domains & Routes → Add custom domain**,
then `daynote.arachat.cc`. Cloudflare creates the DNS record itself.

Then confirm:

```sh
curl -s https://daynote.arachat.cc/v1/health
```

## 2. Google sign-in

The app never talks to Google's token endpoint. It opens the system browser, catches the
authorization code on a loopback redirect, and posts the code plus its PKCE verifier to
`POST /v1/auth/google`; the Worker redeems it. That is why the client secret lives here and not in
the app — Google documents an installed app's secret as non-confidential, but a value inside a WPF
binary can be lifted out with a hex editor, and there is no reason to publish one.

### The OAuth client

Google Cloud Console → **APIs & Services → Credentials → Create credentials → OAuth client ID**,
application type **Desktop app**. No redirect URI is registered: the desktop type allows
`http://127.0.0.1:<port>` loopback automatically, and the old out-of-band flow is withdrawn.

On the consent screen, request **only** `openid`, `email`, and `profile`. Those are non-sensitive
scopes, so the app needs no Google verification review and can be published to Production directly.
Left in Testing, only listed test users can sign in and their Google refresh tokens expire after
seven days — harmless here, because the app switches to its own refresh tokens after the first
sign-in, but it will stop new users.

The client id is public and lives in `wrangler.toml`; the same value is pinned in
`DaynoteAppOptions.GoogleClientId` and the two must agree.

### The secrets

```sh
npx wrangler secret put GOOGLE_CLIENT_SECRET
npx wrangler secret put DEK_WRAP_KEY
```

Both read stdin, so neither lands in shell history.

`DEK_WRAP_KEY` seals every account's data key before it is stored (`src/dek.ts`), so a D1 dump on its
own is not enough to read notes. **Rotating it is a data-loss event, not routine maintenance**: every
stored key would have to be re-sealed under the new secret first, and the Worker refuses to start
without it rather than silently sealing keys under an absent value.

Generate one the same way as `JWT_SECRET`:

```sh
node -e "console.log(require('crypto').randomBytes(48).toString('base64url'))"
```

### Verifying it actually works

There is no mail to check any more. Sign in from a development build (§3) and confirm, in order:

1. the browser opens, and the loopback page says Daynote is signed in;
2. `GET /v1/auth/me` with the returned access token names the right address;
3. `SELECT COUNT(*) FROM users` in D1 is 1 after the first sign-in and still 1 after the second;
4. a note written on one data root appears on a second, empty one after signing in there.


### Webhook fences

Deliveries to `/v1/billing/webhook` pass two checks: the source address must be in the list Paddle
publishes at `https://api.paddle.com/ips` (fetched, cached an hour per isolate, never copied into
the code — `src/paddleIps.ts`), and the `Paddle-Signature` must verify against
`PADDLE_WEBHOOK_SECRET`. If the address list cannot be fetched, the signature alone decides, so a
hiccup at Paddle's endpoint does not stop subscriptions from being recorded. Local secrets for
`wrangler dev` go in `.dev.vars` (see `.dev.vars.example`).

## 2b. Subscriptions (Paddle)

Cloud sync is a paid subscription (docs/CLOUD_SYNC.md §14). Four values are needed, and they come
from four different places — none of them from this repository.

| Value | Kind | Where it comes from |
| --- | --- | --- |
| `PADDLE_WEBHOOK_SECRET` | secret | Paddle dashboard → **Developer tools → Notifications** → create a destination pointing at `https://daynote.arachat.cc/v1/billing/webhook`, then the destination's overflow menu → **Edit destination** → copy **secret key** (starts `pdl_ntfset_`). Each destination has its own key, so sandbox and live differ |
| `PADDLE_API_KEY` | secret | Paddle dashboard → **Developer tools → Authentication → API keys**. Used for one call only: minting customer-portal links |
| `PADDLE_PRICE_ID_MONTHLY`, `PADDLE_PRICE_ID_ANNUAL` | vars | The two recurring prices (`pri_...`) under the "Daynote Cloud Sync" product, from **Catalog → Products**. Public, so they live in `wrangler.toml`. The app sends `{"plan": "monthly" | "annual"}`; no body means annual |
| Default payment link | — | **Checkout → Checkout settings → Default payment link**, set to an approved domain. Server-created checkouts use it; without one, `POST /transactions` has no URL to build |

```sh
npx wrangler secret put PADDLE_WEBHOOK_SECRET
npx wrangler secret put PADDLE_API_KEY
```

### Setting up the product, in order

Nothing above exists until there is something to sell, so this comes first:

1. **Verify the Paddle account.** Paddle reviews the seller (business or individual details, the
   website, what is being sold) before it will process live payments. Sandbox works immediately, so
   development is not blocked, but leave time for this before launch.
2. **Checkout → Checkout settings → Default payment link.** Set it to an approved domain. A
   server-created transaction passes `checkout.url = null`, which means "use the default", so an
   unset default is the one configuration error that makes checkout fail at the last step.
3. **Catalog → Products → New product** — "Daynote cloud sync". The name and description are what
   the customer sees on the checkout and the invoice.
4. **Add four recurring prices** to it, tax-inclusive for KRW: Pro monthly (₩2,900 / $2.49) and
   yearly (₩24,000 / $19.99), Premium monthly (₩5,900 / $4.99) and yearly (₩48,000 / $39.99).
   Copy the `pri_...` ids into `PADDLE_PRICE_ID_MONTHLY`, `PADDLE_PRICE_ID_ANNUAL`,
   `PADDLE_PRICE_ID_PREMIUM_MONTHLY` and `PADDLE_PRICE_ID_PREMIUM_ANNUAL` in `wrangler.toml`. The
   webhook reads the tier from the price, and a price it does not know reads as Pro — so set the
   Premium ids before a Premium price can be bought anywhere. The same amounts are written in
   `src/billing.ts` (`PRICE_LIST`, what the app displays), stated in the Store listing as a range
   (policy 10.8.4) and on the site's `/pricing/` page; change one and change all four.
   Then apply the tier migration: `npm run db:apply:remote` (it runs `0010_tiers.sql`, which
   makes every existing subscription Pro), and deploy.
5. **Developer tools → Notifications → New destination** pointing at
   `https://daynote.arachat.cc/v1/billing/webhook`, subscribed to the events below, then copy its
   secret key.
6. **Developer tools → Authentication → API keys** for `PADDLE_API_KEY`.

### There is no hosted-checkout URL to configure either

An earlier revision of this file had `PADDLE_CHECKOUT_URL`, pointing at a Paddle-hosted checkout
link. That was wrong for one specific reason: a hosted-checkout URL takes `user_email` and a price
id, but it **cannot carry `custom_data`** — and `custom_data.user_id` is what the webhook matches a
subscription to a Daynote account with. Without it, the account would have to be guessed from the
email the customer happened to type.

So `/v1/billing/checkout` creates the transaction server-side with `custom_data`, and returns the
`checkout.url` Paddle builds from the default payment link. Same for the portal
(`/v1/billing/portal`). Neither URL is ever stored.

An existing subscriber changes tier or interval through `/v1/billing/change`, which is Paddle's
subscription update (`PATCH /subscriptions/{id}`, `proration_billing_mode: prorated_immediately`,
`on_payment_failure: prevent_change`) — not the portal, which cannot switch prices, and not a
second checkout, which would bill twice (the checkout refuses a live subscription with 409).
Try an upgrade and a downgrade in the sandbox before going live.

Both read stdin, so neither lands in shell history. **Neither belongs in a commit, a screenshot, or a
chat window**; if one is exposed, revoke it in the dashboard and issue a new one — the webhook secret
is what stops anyone from granting themselves a subscription by POSTing to the webhook.

### Which events to send

Subscribe the destination to `subscription.created`, `subscription.activated`,
`subscription.updated`, `subscription.canceled`, `subscription.paused`, `subscription.resumed`,
`subscription.past_due`, and `transaction.payment_failed`. Anything else is recorded and ignored, so
sending more is harmless; sending fewer means a state change the Worker never hears about.

### Sandbox first

Paddle's sandbox has its own dashboard, its own keys, and its own API host. Point a development
build at a `wrangler dev` Worker configured with the sandbox secret and run one subscription
end to end — checkout, `subscription.activated`, a cancellation — before touching live keys.

### There is no "manage subscription" URL

The customer portal is not a static address: Paddle mints a **single-use, short-lived** link per
customer (`POST /customers/{id}/portal-sessions`), which must never be stored. `/v1/billing/portal`
creates one per click, which is why `PADDLE_API_KEY` is needed and why there is no
`PADDLE_MANAGE_URL` to configure. An earlier revision of this file had one; it was wrong.

## 2c. Sign in with Apple, and account deletion

The iPhone app offers Sign in with Apple next to Google (App Store guideline 4.8), and both phone
apps offer **Delete account** (App Store 5.1.1(v), Google Play). The flow is the Google one with a
different token endpoint: the app posts Apple's authorization code and the raw nonce to
`POST /v1/auth/apple`, and the Worker redeems the code with a client secret it signs itself. See
docs/CLOUD_SYNC.md §4.1c and §4.12.

### In the Apple Developer console

1. **Certificates, Identifiers & Profiles → Identifiers →** the App ID `cc.arachat.daynote` →
   tick **Sign in with Apple** (leave it as a primary App ID) → **Save**. The app's provisioning
   profiles have to be regenerated afterwards, and the Xcode/MSBuild entitlements must carry
   `com.apple.developer.applesignin = ["Default"]`.
2. **Keys → +** → name it (e.g. "Daynote Sign in with Apple") → tick **Sign in with Apple** →
   **Configure** → primary App ID `cc.arachat.daynote` → **Save → Continue → Register**.
3. **Download** the `.p8` file. Apple lets you download it **once**; keep it somewhere safe, and
   never commit it. Note the **Key ID** shown on the key's page (10 characters).
4. Note the **Team ID**: top right of the developer site, or **Membership details** (10 characters).

### On the Worker

Put the two ids in `wrangler.toml` (`APPLE_TEAM_ID`, `APPLE_KEY_ID`; `APPLE_BUNDLE_ID` is already
`cc.arachat.daynote`), and the key in as a secret, whole, header lines included:

```sh
npx wrangler secret put APPLE_PRIVATE_KEY < AuthKey_XXXXXXXXXX.p8
```

Redirecting the file keeps its line breaks and keeps it out of shell history. A PEM pasted with its
line breaks flattened to literal `\n` is accepted too. While any of the four values is empty,
`/v1/auth/apple` answers `400 bad_request` "Signing in with Apple is not configured on this server
yet." — the app shows it — rather than failing inside Apple.

The same key signs the revoke call made on account deletion, so **revoking the key in the console
also stops those revokes** (deletion itself still succeeds; the failure is logged). Rotate it by
creating a new key, switching both ids and the secret, then revoking the old one.

### Before deploying

Migration 0009 has to be applied first, and it is heavier than it looks: it rebuilds `users` and the
five tables that reference it (the header explains why a plain rebuild would have emptied them).
It runs in one transaction, so it either completes or leaves the database as it was.

```sh
npx wrangler d1 time-travel info daynote      # note the bookmark, in case it is needed
npx wrangler d1 migrations apply daynote --remote
npx wrangler deploy
```

Deploying the new Worker first would fail every sign-in on the missing `apple_sub` column.

### Account deletion needs `PADDLE_API_KEY`

`DELETE /v1/account` cancels a live subscription at Paddle before deleting anything. Without
`PADDLE_API_KEY` it cannot, and an account whose subscription is still billing is refused with
`409 subscription_active` rather than deleted. Accounts with no subscription, or a cancelled one,
are unaffected.

### The web page Google Play asks for

Play Console → **App content → Data safety → Data deletion** wants a URL where a user can request
deletion without the app. Use `https://daynote.arachat.cc/delete-account`. It is built from
`../site/content/delete-account.*.html` and served by this Worker; the email fallback on it is
`SUPPORT_EMAIL` in `../site/build.mjs`, so that mailbox has to be read.

## 2d. Subscriptions in the iPhone app (In-App Purchase)

App Store guideline 3.1.3(b) lets the iPhone app unlock file sync bought on the desktop only if it
also sells the same subscriptions through In-App Purchase, so it does (docs/CLOUD_SYNC.md §14.8).
Android sells nothing: Google allows an app to honour an entitlement bought elsewhere.

What exists in App Store Connect (created 2026-10-02 through the API): subscription group **Daynote
Cloud** (22432564) with four auto-renewable subscriptions, Premium ranked above Pro —
`cc.arachat.daynote.premium.annual` (level 1), `.premium.monthly` (2), `.pro.annual` (3),
`.pro.monthly` (4). KOR base prices ₩48,000 / ₩5,900 / ₩24,000 / ₩2,900. The US prices are set by
hand to Paddle's ($39.99 / $4.99 / $19.99 / $2.49, since 2026-10-02) so the iPhone is never the
cheaper place to buy; every other storefront is Apple's equalisation of the KRW base. No introductory offer: the server's 14-day trial is the only one. App Store
Server Notifications V2 point at `https://daynote.arachat.cc/v1/billing/apple/notifications` for
both production and sandbox.

| Value | Kind | Where it comes from |
| --- | --- | --- |
| `APPLE_IAP_PRIVATE_KEY` | secret | App Store Connect → **Users and Access → Integrations → In-App Purchase** → key `F47T589ZC4`, the `.p8` downloaded once. Not the Sign in with Apple key |
| `APPLE_IAP_KEY_ID`, `APPLE_IAP_ISSUER_ID` | vars | Same page: the key id, and the issuer id at the top. Public, in `wrangler.toml` |
| `APPLE_IAP_PRODUCTS` | var | The product ids the app offers. Empty takes the App Store off sale in the app |
| `APPLE_APP_ID` | var | The app's Apple id, checked on production notifications |

### Going live, in order

```sh
cd cloud/worker
npx wrangler d1 time-travel info daynote                       # bookmark, in case
npx wrangler d1 migrations apply daynote --remote              # 0011 (one ADD COLUMN), 0012 (unique index)
npx wrangler secret put APPLE_IAP_PRIVATE_KEY < ~/.config/schooling/SubscriptionKey_F47T589ZC4.p8
npm run deploy
curl -s https://daynote.arachat.cc/v1/health
```

The migration goes first: the Worker that writes App Store rows names `subscriptions.environment`,
and before it every Paddle webhook would fail on the missing column (and be retried by Paddle, so
nothing would be lost, but nothing would apply either). The Worker deployed before the secret is
harmless — the app is offered nothing to buy — so the order of the last two does not matter.

Then App Store Connect → the app → **App Information → App Store Server Notifications** → **Request
a Test Notification** for Sandbox; the Worker logs nothing on success, and `billing_events` gains a
row `apple.TEST`:

```sh
npx wrangler d1 execute daynote --remote --command \
  "SELECT event_type, received_utc FROM billing_events WHERE event_type LIKE 'apple.%' ORDER BY received_utc DESC LIMIT 5"
```

0012's index covers App Store rows only, and none exists before 0011 is deployed, so it cannot
fail on existing data. To check anyway before applying:

```sh
npx wrangler d1 execute daynote --remote --command \
  "SELECT subscription_id, COUNT(*) FROM subscriptions WHERE provider = 'apple' AND subscription_id IS NOT NULL GROUP BY subscription_id HAVING COUNT(*) > 1"
```

Sandbox (TestFlight, App Review) purchases entitle on the production server by design — see
docs/CLOUD_SYNC.md §14.8 for the trade and the queries that list or end them.

### 0015: an App Store change booked for the renewal

```sh
npx wrangler d1 migrations apply daynote --remote              # 0015 (one ADD COLUMN)
npm run deploy
```

Apply it before the Worker that reads `subscriptions.pending_price_id`: that Worker selects it for
every `/v1/billing/status`, checkout and plan change, which would fail on the missing column. The
column is nullable and nothing older writes it, so applying it first breaks nothing.

### Account deletion and an App Store subscription

The server cannot cancel an App Store subscription — only the subscriber can, in Settings — so
`DELETE /v1/account` does not wait for it; the app says, before deleting, that the subscription
goes on until it is cancelled there. It stays visible and cancellable in the Apple ID after the
account is gone.

## 3. Point the app at it

**The shipped app does not talk to this service.** `DaynoteAppOptions.SyncEnabledByDefault` is
`false`, so a released build resolves no endpoint at all — see
[docs/CLOUD_SYNC.md §12](../../docs/CLOUD_SYNC.md) for why, and for what flipping it requires. This
service stays deployed so the remaining email work can be finished against it.

Reach it from a development build with the environment variable, which overrides the flag:

```
DAYNOTE_SYNC_ENDPOINT=https://daynote.arachat.cc   # this service
DAYNOTE_SYNC_ENDPOINT=https://localhost:8787       # a wrangler dev instance
DAYNOTE_SYNC_ENDPOINT=off                          # force it off
```

Only `https` is accepted; anything else resolves to null rather than being downgraded, and null means
the app registers no sync services, has no `HttpClient`, and makes no network calls.

### One thing that will waste your time

The zone's Browser Integrity Check rejects some non-browser clients by User-Agent with a Cloudflare
`error code: 1010`, not a Worker response. `Python-urllib/3.x` is one of them, so a quick probe
script can report a broken service that is in fact fine. `curl` is unaffected, and so is the app's
`HttpClient`. If you see 1010, check the User-Agent before you check the Worker.

## Routine operations

```sh
npm test                                        # the suite, in workerd against a local D1
npx wrangler deploy --dry-run                   # validate config and build
npx wrangler d1 migrations apply daynote --remote
npx wrangler tail daynote-cloud                 # live logs
```

## What has actually been exercised in production

A verification run against the deployed service covered: registration, the identical answer for a
wrong password and an unknown email, refresh-token rotation with family revocation on replay, note
push and pull with the payload returned byte-identical, stale-push rejection, tombstones, account
isolation, bearer enforcement, and rejection of a .NET-style timestamp. A second pass drove the real
.NET client — Argon2id at 64 MiB, AES-256-GCM, both HTTP clients, two real SQLite databases — and
confirmed notes, tags, and the custom-title flag survive a round trip, that two PCs adding a note to
the same date converge on one dense order, that deletes propagate and stay deleted, and that a clean
data root recovers everything from the password alone.

It also confirmed the registration rate limit works, by blocking the second run. The `@example.test`
accounts that run created were deleted afterwards; the database is empty.

One thing production cannot confirm yet: DKIM, because no sender is configured.

Attachment sync was built on 2026-09-10 and needs one setup step before the first deploy that
carries it, because the Worker now binds an R2 bucket that does not exist yet:

```sh
npx wrangler r2 bucket create daynote-assets
npx wrangler d1 migrations apply daynote --remote   # 0007_files.sql
npx wrangler deploy
```

Deploying without the bucket fails at upload rather than at runtime, which is the good order: the
old Worker keeps serving. Remember that `wrangler deploy` does **not** apply migrations — the line
above is not optional, and skipping it leaves `/v1/files/push` failing on a missing table while
text sync carries on working, which is a confusing way to find out.
