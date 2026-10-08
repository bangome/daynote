# Subscription policy

> **Status 2026-10-08: proposed, not built.** What is on sale today is unchanged: Pro and Premium
> sell image and file sync and differ in storage alone ([CLOUD_SYNC.md](CLOUD_SYNC.md) §14.7). This
> document decides what the two tiers grow into, and the rules any future paid feature has to pass.
> Prices are not changed by it.

## 1. Why this exists

Premium is Pro with a bigger number. That is a thin reason to pay twice as much, and it gives Pro
itself one reason to exist. Every feature below was weighed against the same few rules, so that the
next one can be placed without reopening the whole question.

## 2. The rules

Each is a rule because breaking it once costs more than the feature earns.

1. **Nothing free becomes paid.** Local note-taking, text sync, the note lock, the local MCP server,
   export and backup stay free for good. The site and the Store listings already promise free text
   sync; taking it back is the one move that loses users who were never going to pay anyway *and*
   the ones who were.
2. **Paid means it costs us to run or to keep working.** Server storage, server compute, a
   per-request AI bill, or a two-way integration that has to be maintained against someone else's
   API forever. A feature that runs entirely on the user's device is not sold.
3. **Security is never sold.** The note lock (CLOUD_SYNC.md §4.1b), account deletion, and the
   connected-device list with remote revoke when it exists (§13.2) are free on every account.
4. **A sold feature never moves up.** Nothing leaves Pro for Premium, and nothing leaves a tier for
   a higher one. Moving down is allowed. Store policy 10.8.6 forbids removing value from a
   subscriber; this extends that to tiers.
5. **A lapse stops a feature, never deletes what the user made.** Already true of attachments
   (§5.5, §14.7). Every new feature states its own lapse behaviour in §5 before it ships.
6. **The core of each tier must work with the lock on.** Some features need the server to read
   content (§4); those can be sold, but they cannot be the reason a tier is worth buying, or a
   locked account pays for half a product.
7. **Only what is deployed is sold.** No feature appears on the pricing page, the in-app plan table,
   or a Store listing before its server side is live. Pro was once on sale with nothing behind it;
   this rule exists so that does not happen again.
8. **One catalogue, every store.** A paid feature is sold identically through Paddle and the App
   Store (guideline 3.1.3(b), §14.8). Android honours what was bought elsewhere and sells nothing.

## 3. The tiers

Grouped by what each tier is *for*. Pro is **"keep more"**; Premium is **"keep everything, and
connect it."**

| Feature | State | Free | Pro | Premium |
| --- | --- | --- | --- | --- |
| Notes, to-dos, events, tags, favorites — local | built | ✓ | ✓ | ✓ |
| Text sync between the user's devices | built | ✓ | ✓ | ✓ |
| Note lock (opt-in E2EE) | built | ✓ | ✓ | ✓ |
| Local MCP server (`Daynote.Mcp`) | built | ✓ | ✓ | ✓ |
| Export, backup, local conflict copies (§7.4) | built | ✓ | ✓ | ✓ |
| Phone reminders | built | ✓ | ✓ | ✓ |
| **Image and file sync** | built | — | 2 GB | unlimited (200 GB fair use) |
| **Per-file size limit** | config | — | 256 MB (today's) | higher, see §7 |
| **Note version history** (text only) | new | — | 30 days | 1 year |
| **Account point-in-time restore** | new | — | — | ✓ |
| **`.ics` calendar feed** (TODOS.md §10, tier 1) | designed | — | ✓ | ✓ |
| **Public share link** for a note | new | — | ✓ | ✓ |
| **CLI, against the cloud** (§6) | new | — | ✓ | ✓ |
| **Two-way CalDAV** (Apple Reminders and the self-hosted tail) | designed | — | — | ✓ |
| **Microsoft To Do, Todoist** | designed | — | — | ✓ |
| **AI features** (§8) | later | — | — | ✓ |

Why history is the centre of Pro: sync today is last-writer-wins, and the losing body survives only
as a file on the device that lost (§7.4). Fear of losing writing is the strongest reason anyone pays
for a notes service. And it passes rule 6 completely — the server keeps old ciphertext envelopes it
was already storing, and never needs to read one.

Why integrations are the centre of Premium: each two-way client is "permanent maintenance" in
TODOS.md's own words — a conflict rule, delete propagation, etags and token expiry, forever. That is
exactly the cost rule 2 says a subscription pays for.

## 4. What works with the lock on

The lock means the Worker has no key. Anything the Worker would have to read cannot run.

| Works locked | Does not work locked |
| --- | --- |
| File sync, storage, per-file size | `.ics` feed (the Worker cannot render it) |
| Version history, point-in-time restore | Public share links |
| CalDAV, To Do, Todoist — **if run on the device** | Server-side AI (§8) |
| CLI (it is a client and decrypts itself) | Remote MCP connector (§8.3) |

The app says which features a locked account loses **before** checkout and before the lock is
turned on, never after. A locked account is billed like any other (§14.2); no discount, no
surcharge.

Two-way integrations are to be built device-side for this reason as well as for credentials, which
TODOS.md §10.1 already keeps on the device that holds them.

## 5. When it lapses, or goes down a tier

| Feature | On lapse or downgrade |
| --- | --- |
| File sync | Unchanged from §14.7: uploads stop, nothing deleted, downloads and deletes still work |
| Version history | Current notes are untouched. History older than the tier now in force is kept **30 days**, then pruned. Resubscribing within 30 days loses nothing |
| `.ics` feed | The feed URL stops updating. Calendar apps keep their last copy; nothing is pushed out to empty them |
| Share links | Links stop resolving. The notes themselves are untouched |
| CalDAV, To Do, Todoist | Sync stops. Items stay on both sides, list mappings are kept, and resubscribing resumes |
| CLI | Tokens stop authenticating; the account and its tokens are kept, so resubscribing revives them unchanged. Nothing the CLI wrote is touched |
| AI | Stops. Derived data (§8.2) kept 30 days, then deleted |

## 6. The CLI

**Cloud only, from Pro** (decided 2026-10-08). One `daynote` command that signs in to the account
with a **personal access token** and works against the cloud copy, with no app installed: a Linux
box, a server, a CI job, a script on a machine that is not one of the user's devices. Add a note to
a date, add a to-do with the same grammar as the `@` command, search, list a day, export.

**There is no local mode.** A CLI that reads the database on the PC it runs on would cost nothing to
run and so, by rule 2, could not be sold — two products under one name, one free and one paid, is a
pricing page nobody reads correctly. The CLI is the cloud's client, and it behaves the same on every
machine. Local programmatic access stays where it already is: the free local MCP server.

It is sold because it is a public API surface — versioning, rate limits, revocation — that has to be
kept stable once people script against it. It starts at Pro rather than Premium because the people
who script their notes are the people who buy the first tier; putting the API behind the dearest one
would make it a curiosity.

- To the sync engine the CLI is one more device: its writes go through the same push and LWW rule
  (§7.3) as the apps', with no path of its own.
- Tokens are **scoped** (read, write) and **revocable** from the account window, each with a name
  and a last-used time.
- A token never carries the data key. On a locked account the CLI asks for the lock passphrase, or
  takes the recovery key, and unwraps the key itself — the same path the apps use (§4.1b).
- Attachments through the CLI follow the same entitlement and quota as the apps.

## 7. Per-file size

Today `FileCapturePolicy.MaxFileBytes` is 256 MB on every tier. Premium's figure is to be set only
after the upload path is measured against the Worker's request-body limit for the account's plan;
a limit the app advertises and the Worker refuses is a broken promise. The figure becomes per tier
on both sides at once — the client policy and `assets.put`.

## 8. AI — later, on these terms

Not built and not scheduled. When it comes, it comes under these rules, so it is decided now rather
than argued over at launch.

### 8.1 Two kinds, priced differently

- **The user's own AI, through the local MCP server — free, always.** The user's model, the user's bill,
  the user's machine. Daynote only exposes the data. Nothing is sold, so rule 2 says nothing may be
  charged.
- **AI that Daynote runs — Premium.** Day and week summaries, a written weekly review, search by
  meaning, text recognised inside attached images and PDFs, `@` readback for phrasings the grammar
  does not cover. Every one has a per-request cost, which is what puts it under rule 2.

### 8.2 Conditions on AI that Daynote runs

1. **Opt-in per account, off by default.** Subscribing to Premium does not turn it on.
2. **Not available with the lock on**, and says so plainly. A locked account that wants AI uses its
   own model through §8.1.
3. **Disclosed in full** before it is turned on: the provider, what is sent, how long the provider
   keeps it, and that it is not used for training. [PRIVACY.md](PRIVACY.md), the Store data
   declarations and the App Store privacy label are updated in the release that ships it.
4. **It suggests; it does not edit.** A summary or a recognised to-do is offered and the user accepts
   it. Nothing written by the user is changed by a model, in the same spirit as the `@` command
   leaving typed text alone (TODOS.md §7).
5. **Derived data is ours and is deletable.** Embeddings, recognised text and cached summaries are
   deleted when AI is turned off, and 30 days after a lapse (§5). They are never exported as if they
   were the user's notes.
6. **A monthly allowance, not "unlimited".** Reaching it pauses AI until the next period. There are
   no overage charges.
7. **The honesty rule still holds.** No AI copy may imply that the service cannot read notes; it can,
   by default, and AI is the clearest case of it doing so.

### 8.3 Remote MCP

A hosted MCP endpoint, so that a cloud assistant can reach the user's Daynote without a PC running,
falls under 8.2 — it is the Worker reading content on the user's behalf. Premium, opt-in, not with
the lock on. The local server stays free under 8.1.

## 9. Order

Each step is sellable on its own, and nothing is listed before it is live (rule 7).

| Step | What | Depends on |
| --- | --- | --- |
| 1 | Version history in Pro and Premium | A history table and pruning job on the Worker; a restore surface in the app |
| 2 | Per-file size by tier | §7's measurement |
| 3 | `.ics` feed | The agenda cutover (TODOS.md §12): until something reads the to-do entities, a feed of them is empty |
| 4 | Share links | — |
| 5 | Personal access tokens and the CLI | Token table, scopes, revoke surface; a stable, versioned API for the CLI to call |
| 6 | Point-in-time restore | Step 1 |
| 7 | CalDAV two-way | Step 3's cutover and lists (TODOS.md §10.1) |
| 8 | To Do, Todoist | Step 7 |
| 9 | AI | A provider decision, PRIVACY.md and store declarations |

## 10. Open questions

1. **Trial scope.** The 14-day trial grants Pro (§14). Should it show Premium-only features, or is a
   trial of the cheaper tier the honest one to give?
2. **History for attachments.** §3 keeps text history only. File versions would multiply R2 storage;
   worth it on Premium alone, or never?
3. **Share links on Free.** A link is a server cost but also the app's only way to be seen by
   someone who does not use it. One active link on Free may earn more than it costs.
4. **AI allowance size**, once a provider and its prices are known.
5. **CLI rate limits by tier.** Pro and Premium get the same CLI. Whether Premium's difference
   there should be a higher request rate, or nothing, is open until real usage exists.
