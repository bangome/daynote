# Daynote cloud worker

The auth and sync API for Daynote's optional cloud sync. Read
[`docs/CLOUD_SYNC.md`](../../docs/CLOUD_SYNC.md) first — this README only covers running the thing.

**Current: Google sign-in, sessions, and note sync.** Attachments (R2) are not built yet.

## The one rule

This Worker holds each account's data key **by default**, so it *can* read note content. That is the
deliberate cost of one-click Google sign-in (docs/CLOUD_SYNC.md §1). An account that turns on the
opt-in lock takes that key away (`/v1/auth/protect`), and from then on this Worker holds only
envelopes it cannot open. The rule that replaces the old "never able to read a note" one is about
honesty, not capability:

- **Nothing may claim otherwise.** Not a UI string, not PRIVACY.md, not the Store listing.
- The data key is sealed under `DEK_WRAP_KEY` before it touches D1, so a database dump alone is not
  enough — defence in depth, not a privacy guarantee.
- No password ever reaches this Worker, because there is no password anywhere in the product.
- The OAuth client secret stays a Worker secret; it must never be shipped in the app.

## Endpoints

| Method | Path | Auth |
| --- | --- | --- |
| GET | `/v1/health` | — |
| POST | `/v1/auth/google` | authorization code + PKCE verifier in body |
| POST | `/v1/auth/refresh` | refresh token in body |
| POST | `/v1/auth/logout` | refresh token in body |
| GET | `/v1/auth/me` | Bearer |
| GET | `/v1/auth/data-key` | Bearer |
| POST | `/v1/auth/protect` | Bearer + both envelopes |
| POST | `/v1/auth/unprotect` | Bearer + the raw data key |
| POST | `/v1/sync/push` | Bearer |
| GET | `/v1/sync/pull` | Bearer |

`/v1/auth/google` is both sign-up and sign-in: the first successful exchange for a Google subject
creates the account. `/v1/auth/data-key` re-issues the key to a device that is still signed in but
lost its local copy, so a restored Windows profile does not need another trip through the browser.

## Secrets

```sh
npx wrangler secret put GOOGLE_CLIENT_SECRET
npx wrangler secret put DEK_WRAP_KEY
```

`GOOGLE_CLIENT_SECRET` redeems the authorization code the app collects in the browser;
`DEK_WRAP_KEY` seals each account's data key at rest. Neither ships in the app. There is no email
sender any more — password reset left with the password. See
[DEPLOY.md](DEPLOY.md#2-google-sign-in).

## Local development

```sh
npm install

# One-time: create the database, then paste the printed id into wrangler.toml.
npx wrangler d1 create daynote

# Apply migrations to the local (miniflare) copy.
npm run db:apply:local

# Set the access-token signing secret for local runs.
#   node -e "console.log(require('crypto').randomBytes(48).toString('base64url'))"
echo 'JWT_SECRET=<paste>' > .dev.vars

npm run dev
```

Smoke test:

```sh
curl -s http://localhost:8787/v1/health
```

## Tests

```sh
npm test          # vitest inside workerd, against a real local D1
npm run typecheck
```

Tests run in the Workers runtime rather than in Node, so SQL errors, `UNIQUE` violations, and
`batch()` atomicity are exercised for real. `test/helpers.ts` applies `migrations/0001_auth.sql`
directly, so a migration that does not parse fails the suite immediately.

The tests do **not** reproduce the client's Argon2id derivation — an `auth_key` there is just 32
random bytes, which is exactly what the server is entitled to assume. Verifying that the client
derives it correctly is Phase 2's job.

## Deploying

```sh
npx wrangler secret put JWT_SECRET
npm run db:apply:remote
npm run deploy
```

`workers_dev = false` in `wrangler.toml`: attach a route on a domain you control rather than
publishing a `*.workers.dev` hostname.

## Version pinning

`@cloudflare/vitest-pool-workers` pins an exact `wrangler` version, which in turn constrains
`vitest`. The three move together — when bumping one, check
`npm view @cloudflare/vitest-pool-workers peerDependencies` and match all three, or `npm install`
fails with `ERESOLVE`.

## Not yet built

Phase 7: R2 attachments. `wrangler.toml` keeps the R2 binding commented out so a bucket is not
created before the code that uses it exists.

## Brand site

`https://daynote.arachat.cc/` (Korean) and `/en/` (English) are static pages served by this same
Worker through the `[assets]` block in `wrangler.toml`. The hostname is a Custom Domain of this
Worker, and Cloudflare does not let a second Worker share it, so the site rides along here.

- Source: `../site/template.html` + the ko/en strings in `../site/build.mjs`; output lands in
  `../site/public/` (committed, so a deploy never depends on the build having run).
- `npm run build:site` regenerates the two HTML files; `npm run deploy` runs it first (`predeploy`).
- `run_worker_first = ["/v1/*", "/privacy"]` keeps the API and the privacy policy in code; every
  other path is answered from `public/`, with `404.html` for misses.
- The Store button points at a Store search until the listing has a product id — change
  `STORE_URL` in `build.mjs` to `https://apps.microsoft.com/detail/<ProductId>` then.
- Images under `public/img/` are derived from `docs/brand/` (see the PIL snippet in the git history
  of this section, or just re-export: hero crop from the store overview, screenshots at 1200 px,
  WebP q≈84). The store screenshots predate the removal of the Clipboard tab; replace them when
  the listing gets new ones.

### Pages Paddle's domain review needs

`/pricing/`, `/terms/`, `/refund/`, `/support/` (each also under `/en/`) plus `/privacy` cover
Paddle's website checklist: what is sold, the price, terms, refund policy, privacy policy, and a
support contact. `/checkout/` is the **default payment link** (Checkout → Checkout settings): it
loads Paddle.js and opens the server-created transaction from `?_ptxn=`.

Bodies live in `../site/content/<slug>.<lang>.html`; the shell is `../site/page.html`. Before
asking Paddle to approve the domain, fill in the constants at the top of `../site/build.mjs`:
`OPERATOR`, `SUPPORT_EMAIL` (`PRICE_LINE` is set: ₩2,900/mo · ₩24,000/yr, $2.49 / $19.99; the Paddle catalog must match),
`PADDLE_CLIENT_TOKEN`, and `PADDLE_ENVIRONMENT`. Then `npm run build:site` and deploy.
