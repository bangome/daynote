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
| `PADDLE_WEBHOOK_SECRET`, `PADDLE_API_KEY` (secrets) | **must be set** for subscriptions — see §2b |
| `PADDLE_PRICE_ID_MONTHLY`, `PADDLE_PRICE_ID_ANNUAL` (vars) | set in `wrangler.toml` (₩2,900 / $2.49 monthly, ₩24,000 / $19.99 annual) — see §2b |
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
4. **Add two recurring prices** to it: monthly (₩2,900 / $2.49) and yearly (₩24,000 / $19.99),
   tax-inclusive for KRW. Copy the `pri_...` ids into `PADDLE_PRICE_ID_MONTHLY` and
   `PADDLE_PRICE_ID_ANNUAL` in `wrangler.toml`. The same prices have to be stated in the Store
   listing as a range (policy 10.8.4) and on the site's `/pricing/` page.
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
npm test                                        # 84 cases in workerd against a local D1
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

Two things production cannot confirm yet: DKIM (no sender configured) and attachment sync (not built).
