# Daynote cloud worker

The auth and sync API for Daynote's optional cloud sync. Read
[`docs/CLOUD_SYNC.md`](../../docs/CLOUD_SYNC.md) first — this README only covers running the thing.

**Phase 1 (current): accounts and sessions.** No note, file, or asset endpoints exist yet.

## The one rule

This Worker must never be able to read a user's notes. It stores:

- a verifier over the client's `auth_key` (never a password),
- the client's wrapped DEK envelopes, which it cannot open,
- and, from Phase 4 onward, ciphertext blobs.

Any change that puts a password, a key, or readable content into a request or a column is a
blocker, not a review comment. `test/auth.test.ts` asserts the `users` column list for exactly this
reason — if you add a column, that test fails on purpose and you have to justify the change.

## Endpoints

| Method | Path | Auth |
| --- | --- | --- |
| GET | `/v1/health` | — |
| POST | `/v1/auth/register` | — |
| POST | `/v1/auth/login` | — |
| POST | `/v1/auth/refresh` | refresh token in body |
| POST | `/v1/auth/logout` | refresh token in body |
| POST | `/v1/auth/password` | Bearer + `current_auth_key` |
| GET | `/v1/auth/me` | Bearer |

`/v1/auth/password` requires `current_auth_key` on top of the Bearer token: without it, a stolen
15-minute access token would be enough to change the password and lock the owner out.

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

Phases 4–7 from the design doc: sync push/pull, password reset (needs a transactional-email
provider), DEK re-wrap after reset, and R2 attachments. `wrangler.toml` keeps the R2 binding
commented out so a bucket is not created before the code that uses it exists.
