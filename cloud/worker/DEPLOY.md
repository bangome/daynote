# Deploying the Daynote cloud worker

Current state, verified 2026-08-20 against a real deployment:

| Thing | State |
| --- | --- |
| Worker `daynote-cloud` | deployed, **no route attached** |
| D1 `daynote` | created (`4226d475-a071-44c0-a2c7-4a953cbaa44e`, APAC), migrations 0001–0003 applied, empty |
| `JWT_SECRET` | set |
| `RESEND_API_KEY` | **not set** — password reset returns 500 until it is |
| DNS on `daynote.arachat.cc` | **not created** |
| `workers_dev` | false, `preview_urls` false — the service has no public hostname on purpose |

The worker is deployed but unreachable. That is the intended resting state: it accepts account
registrations, so it should only answer on a hostname somebody deliberately attached.

## 1. Attach the hostname

Needs `zone:edit` on `arachat.cc`, which the OAuth token used for the rest of this does not have. In
the dashboard: **Workers & Pages → daynote-cloud → Settings → Domains & Routes → Add custom domain**,
then `daynote.arachat.cc`. Cloudflare creates the DNS record itself.

Then confirm:

```sh
curl -s https://daynote.arachat.cc/v1/health
```

## 2. Password reset email

Resend, free tier: 3,000/month and 100/day at the time of writing, which is far more reset traffic
than this app will produce. This replaced MailChannels, whose free Workers integration ended in June
2024 and whose replacement is a paid account.

Cloudflare Email Sending *is* a viable native option — an earlier note here said otherwise and was
wrong, having read Email *Routing*'s forwarding rules rather than Email *Sending*'s. It needs no API
key at all. It is not used yet for two reasons: it is in open beta, and password reset is a bad place
to depend on a beta; and onboarding the domain needs permissions the deploy token lacks
(`/email/sending/zones` → `Unauthorized [2036]`). Worth revisiting when it reaches GA — it would
remove both the vendor and the secret, and it is a one-file change.

### The key

Create a Resend account, add `daynote.arachat.cc` as a domain, and create an API key. Then:

```sh
npx wrangler secret put RESEND_API_KEY
```

Paste at the prompt — it reads stdin, so the key does not land in shell history. There is no DKIM
secret to set: Resend holds the private half and gives you a public key to publish, so no signing key
lives in this Worker.

### The DNS records

Resend shows the exact values for your domain; these are the shapes. All three on
`daynote.arachat.cc`, and **Cloudflare proxying must be off (DNS only)** on each:

| Type | Name | Value | Purpose |
| --- | --- | --- | --- |
| MX | `send` | `feedback-smtp.<region>.amazonses.com`, priority 10 | bounce and complaint handling |
| TXT | `send` | `v=spf1 include:amazonses.com ~all` | SPF |
| TXT | `resend._domainkey` | the public key Resend gives you | DKIM |

Take the MX host and the DKIM value from the Resend dashboard rather than from this table — the region
and the key are specific to your domain.

### Verifying it actually works

Request a reset for an address you control and read the received headers. Both of these must say
`pass`:

```
Authentication-Results: ... spf=pass ... dkim=pass
```

A message that arrives while failing one of them will reach most inboxes today and start silently
failing later, so treat a partial pass as not done.

Until the key is set, `/v1/auth/reset/request` returns 500 rather than reporting success for mail it
never sent. That is deliberate, and it is what a live check against the current deployment confirms.

## 3. Point the app at it

Nothing to do: `DaynoteAppOptions.DefaultSyncEndpoint` is `https://daynote.arachat.cc`, so a shipped
build already points here. `DAYNOTE_SYNC_ENDPOINT` overrides it for local development:

```
DAYNOTE_SYNC_ENDPOINT=https://localhost:8787   # a wrangler dev instance
DAYNOTE_SYNC_ENDPOINT=off                      # no account section at all
```

Only `https` is accepted; anything else resolves to null rather than being downgraded, and null means
the app registers no sync services, has no `HttpClient`, and makes no network calls.

The default used to be absent, and that was the bug: every installed copy had no endpoint, so the
account section was missing from the settings panel while this service was deployed and working. See
[docs/CLOUD_SYNC.md §12](../../docs/CLOUD_SYNC.md).

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
