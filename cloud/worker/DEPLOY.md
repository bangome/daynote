# Deploying the Daynote cloud worker

Current state, verified 2026-08-20 against a real deployment:

| Thing | State |
| --- | --- |
| Worker `daynote-cloud` | deployed, **no route attached** |
| D1 `daynote` | created (`4226d475-a071-44c0-a2c7-4a953cbaa44e`, APAC), migrations 0001–0003 applied, empty |
| `JWT_SECRET` | set |
| `MAILCHANNELS_API_KEY`, `DKIM_PRIVATE_KEY` | **not set** — password reset returns 500 until they are |
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

MailChannels' free Workers integration ended in June 2024, so this needs a MailChannels account and
an API key. `EmailSender` in `src/email.ts` is the entire surface if you would rather use Resend.

```sh
npx wrangler secret put MAILCHANNELS_API_KEY
```

DKIM, generated on a machine you trust — this private key signs your outbound mail, so it should not
pass through anyone else's terminal:

```sh
openssl genrsa -out dkim.key 2048
openssl rsa -in dkim.key -pubout -outform der | openssl base64 -A   # the TXT record value
npx wrangler secret put DKIM_PRIVATE_KEY < dkim.key                 # then delete dkim.key
```

Three DNS records on `daynote.arachat.cc`. All three matter, and they fail differently:

| Record | Value | What happens without it |
| --- | --- | --- |
| `_mailchannels` TXT | from the MailChannels console — **do not** copy the value from here | MailChannels rejects the send outright |
| `mailchannels._domainkey` TXT | `v=DKIM1; p=<the base64 above>` | delivered but unsigned → spam folder |
| `@` TXT (SPF) | `v=spf1 a mx include:relay.mailchannels.net ~all` | weakens alignment → spam folder |

**Domain Lockdown** (`_mailchannels`) is MailChannels authorising *your* account to send as this
domain. Its exact value is account-specific and the syntax has changed across their plan changes, so
take it verbatim from their console rather than from this document or from anyone's recollection.

The other two are ordinary email authentication and the values above are correct as written.

Verify by requesting a reset for an address you control and checking the received headers say
`dkim=pass` and `spf=pass`. A message that arrives but fails either one will reach most inboxes today
and start silently failing later, so treat a partial pass as not done.

Until the sender is configured, `/v1/auth/reset/request` returns 500 rather than reporting success
for mail it never sent. That is deliberate.

## 3. Point the app at it

```
DAYNOTE_SYNC_ENDPOINT=https://daynote.arachat.cc
```

Only `https` is accepted; anything else is refused rather than downgraded. With the variable unset the
app registers no sync services at all, has no `HttpClient`, and makes no network calls.

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
