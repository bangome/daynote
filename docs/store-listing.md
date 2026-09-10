# Partner Center listing — every field, ready to paste

Written 2026-09-10 for the first submission that carries cloud sync. [STORE.md](STORE.md)
is the mechanical route from repo to package; this file is the *content*, because the
listing is where the submission-blocking policy obligations actually land (10.8.2,
10.8.4) and where a wrong answer is a false declaration rather than a broken build.

Wording is derived from `cloud/site/public/index.html`, which is already reviewed and
consistent with [PRIVACY.md](PRIVACY.md). **Keep the three in step.** A listing that
promises more than the privacy page allows is the failure mode worth guarding against.

---

## Product declarations

Partner Center → **Properties → Product declarations**.

| Question | Answer | Why |
| --- | --- | --- |
| This app makes use of a third-party commerce/purchase API | **Yes** ✔ | Required by 10.8.2 whenever payment is not Microsoft's. It is a checkbox; certification does not infer it |
| This app depends on non-Microsoft drivers or NT services | No | |
| This app has been tested for accessibility | Leave unticked unless you have run the audit | Only tick what you have actually done |
| Can this app run without a network connection? | **Yes** | Every feature except cloud sync and the MCP server works offline; the app opens no connection until you sign in |
| Does this app call, support, or contain non-Microsoft commerce? | **Yes** — Paddle | Same obligation as the first row |

**Category**: Productivity. **Subcategory**: Personal finance → no; leave blank or
"Other".

**Capability justification** — certification will ask about `runFullTrust`:

> Daynote is a Win32 desktop application packaged with the Desktop Bridge. Full trust is
> required to read and write the user's notes and attachments in `%LocalAppData%`, to
> register an optional stdio MCP server with a locally installed AI client, and to
> register an optional "start with Windows" entry the user turns on themselves.

---

## Privacy and data collection

Partner Center asks what personal data the app collects and whether the publisher can
access it. The honest answers, which match PRIVACY.md:

| Data type | Collected | Publisher can access | Note |
| --- | --- | --- | --- |
| Email address | **Yes**, only after Google sign-in | Yes | Identifies the account |
| Other identifiers (Google account id) | **Yes**, only after sign-in | Yes | |
| User-generated content (note text, attachments) | **Yes**, only after sign-in | **Yes** | Encrypted in transit and at rest, but the service holds the key by default |
| Payment information | **No** | No | Paddle is the merchant of record; card details never reach Daynote or its service |
| Usage data / analytics / crash reports | **No** | — | There is no telemetry of any kind |
| Location, contacts, camera, microphone | **No** | — | |

**Do not describe the default as end-to-end encrypted.** The opt-in "note lock" removes
the service's access for users who turn it on, and is worth describing — but the default
is a server-held key, and PRIVACY.md says so. See CLOUD_SYNC.md §4.1a.

**Privacy policy URL**: `https://daynote.arachat.cc/privacy`

---

## Store listing — Korean (default)

### Short description (max 1,000)

```
캘린더에서 날짜를 고르면 그날의 노트, 할 일, 파일이 한자리에. 완전히 로컬로 동작하며, 원하면 Google 계정으로 여러 PC를 잇습니다.
```

### Description (max 10,000)

```
Daynote는 날짜별로 정리되는 Windows 노트 앱입니다. 캘린더에서 하루를 고르면 그날 쓴 노트, 그날 할 일, 그날 붙여둔 파일이 모두 거기에 있습니다.

■ 하루가 단위입니다
캘린더에서 날짜를 고르면 그날의 작업공간이 열립니다. 한 날짜에 노트를 여러 개 둘 수 있고, 순서를 바꾸거나 이름을 바꾸거나 복제할 수 있습니다.

■ 할 일은 따로 관리하지 않습니다
본문에 "-[] 장보기"라고 쓰면 할 일 탭에 나타납니다. 별도의 목록을 유지할 필요가 없습니다.

■ 타임라인
하루씩 넘기는 대신, 노트를 날짜순으로 길게 펼쳐 한 번에 훑어봅니다.

■ 포스트잇
노트를 항상 위에 고정되는 작은 창으로 띄우고, 본문과 실시간으로 함께 편집합니다.

■ 태그와 즐겨찾기
노트에 태그를 붙이고, 본문에 쓴 #해시태그는 자동으로 인식됩니다. 자주 여는 노트는 즐겨찾기 탭에 모입니다.

■ 통합 검색
제목과 본문, 태그를 한 번에 찾습니다. 한글도 정확히 검색됩니다.

■ 파일 첨부
이미지와 문서를 날짜에 붙여둡니다. 본문에서 링크로 참조할 수 있고, 더블클릭하면 원하는 위치로 내려받습니다.

■ AI 연동 (MCP)
Claude Desktop 같은 MCP 클라이언트가 내 노트를 읽고 쓸 수 있습니다. 직접 등록하기 전까지는 꺼져 있습니다.

■ 백업과 복원
설정에서 데이터를 한 파일로 내보내고 되돌립니다. 업데이트와 재설치를 거쳐도 데이터는 그대로 남습니다.

■ 라이트와 다크, 한국어와 영어
Windows 테마를 따르거나 한 번의 키로 바꿉니다. UI는 한국어와 영어를 모두 제공합니다.


── 클라우드 동기화 (선택) ──

로그인하지 않으면 Daynote는 완전히 로컬로만 동작합니다. 인터넷 연결을 아예 열지 않습니다.

회사 PC에서 쓴 노트를 집에서 이어 쓰고 싶다면 Google 계정으로 로그인하세요.

• 노트, 할 일, 태그, 즐겨찾기 동기화는 무료입니다. 기간 제한이 없습니다.
• 첨부한 이미지와 파일까지 함께 따라오게 하려면 Pro 구독이 필요합니다.

Pro는 월 ₩2,900 또는 연 ₩24,000입니다. 가입 시 14일 무료 체험이 한 번 제공되며, 카드 등록은 필요하지 않습니다.

체험이 끝나거나 구독을 해지하면 이미지·파일 동기화만 멈춥니다. 노트는 계속 동기화되고, 이 PC의 파일은 그대로이며, 이미 클라우드에 올라간 파일도 삭제되지 않습니다. 다시 구독하면 멈춘 지점에서 이어집니다.

결제는 판매자인 Paddle이 처리합니다. 앱에서 구독을 시작하면 브라우저의 Paddle 페이지에서 결제가 진행되며, Daynote는 카드 정보를 보지 않습니다.

동기화는 백업이 아니라 전파입니다. 한 PC에서 지우면 모든 PC에서 지워집니다. 백업은 설정의 백업 기능으로 따로 두세요.


── 프라이버시 ──

Daynote에는 사용 통계도, 오류 보고도, 텔레메트리도 없습니다. 클립보드를 감시하지 않고, 키 입력을 기록하지 않으며, 스크린샷을 찍지 않습니다.

노트와 파일은 내 Windows 계정만 읽을 수 있는 로컬 폴더(%LocalAppData%\Daynote)에 일반 파일로 저장됩니다. 앱을 지워도 이 폴더는 남습니다.

내용이 이 PC를 벗어나는 길은 두 가지뿐이고, 둘 다 직접 켜야 합니다 — 클라우드 동기화와 AI 연동입니다.

클라우드 동기화를 켜면 노트와 파일은 전송 중에도, 서버에 저장될 때도 암호화됩니다. 다만 기본 설정에서는 서비스가 열쇠를 함께 보관하므로 종단간 암호화는 아닙니다. 설정의 "노트 잠금"을 켜면 나만 아는 암호로 열쇠를 다시 잠그고 서비스 쪽 사본은 파기됩니다.

Windows 11 / 10 (x64)
```

### Product features (each max 200 chars, up to 20)

```
캘린더에서 날짜를 고르면 그날의 노트·할 일·파일이 한자리에
본문에 "-[] 할 일"이라고 쓰면 할 일 탭에 자동으로 나타납니다
타임라인으로 여러 날의 노트를 한 번에 훑어봅니다
포스트잇 창으로 노트를 화면 위에 띄워 두고 함께 편집합니다
태그와 #해시태그, 즐겨찾기로 노트를 다시 찾습니다
제목·본문·태그를 한 번에 찾는 통합 검색 (한글 정확 검색)
이미지와 파일을 날짜에 첨부하고 본문에서 링크로 참조합니다
Claude Desktop 등 MCP 클라이언트와 연동 (직접 켜야 동작)
데이터를 한 파일로 백업하고 되돌립니다
라이트·다크 테마, 한국어·영어 UI
로그인 전까지 인터넷에 연결하지 않습니다. 텔레메트리 없음
Google 로그인 시 노트·할 일·태그 동기화 무료
이미지·파일 동기화는 Pro 구독 (월 ₩2,900 / 연 ₩24,000, 14일 무료 체험)
```

### Search terms (max 7, 30 chars each)

```
노트
일기
캘린더 노트
할 일
데일리 노트
daynote
markdown
```

### Copyright and trademark info

```
© 2026 Bread Jinhwa Jeong
```

### Additional license terms

Leave blank (the Standard Application License applies).

---

## Store listing — English

### Short description

```
Pick a day on the calendar and everything from that day is there: notes, to-dos, files. Fully local, with optional sign-in to keep several PCs in step.
```

### Description

```
Daynote is a Windows notes app organised by date. Pick a day on the calendar and you get everything you wrote, planned, and attached on that day.

■ The day is the unit
Choosing a date opens that day's workspace. A day can hold several notes, which you can reorder, rename, and duplicate.

■ To-dos need no separate list
Type "-[] buy milk" in a note and it appears in the To-do tab. There is no second list to maintain.

■ Timeline
Instead of paging one day at a time, read your notes as one long, date-ordered stream.

■ Sticky notes
Float a note in a small always-on-top window and edit it alongside the main body, live.

■ Tags and favorites
Tag a note, or just write #hashtags in the body — they are picked up automatically. Notes you open often collect in the Favorites tab.

■ Unified search
Search titles, bodies, and tags at once, with correct Korean matching.

■ Attachments
Attach images and documents to a day, reference them from the body as links, and double-click one to save a copy wherever you like.

■ AI integration (MCP)
An MCP client such as Claude Desktop can read and write your notes. Off until you register it yourself.

■ Backup and restore
Export your data to a single file from Settings and put it back. Your data survives updates and reinstalls.

■ Light and dark, Korean and English
Follow the Windows theme or switch with one key. The interface is available in both languages.


── Cloud sync (optional) ──

Without signing in, Daynote is entirely local. It opens no network connection at all.

Sign in with a Google account when you want the notes you wrote at work to continue at home.

• Syncing notes, to-dos, tags and favorites is free, with no time limit.
• Syncing the images and files you attach needs a Pro subscription.

Pro is $2.49 per month or $19.99 per year. A 14-day free trial is granted once at sign-up, and no card is required for it.

When the trial ends or a subscription is cancelled, only image and file syncing stops. Your notes keep syncing, the files on this PC are untouched, and the copies already uploaded are kept. Subscribing again resumes from where it stopped.

Payments are handled by Paddle as the merchant of record. You start a subscription in the app and complete it on Paddle's page in your browser; Daynote never sees your card details.

Sync is propagation, not backup. Deleting on one PC deletes on all of them, so keep a backup with the Backup feature in Settings.


── Privacy ──

Daynote has no usage statistics, no crash reporting, and no telemetry. It does not watch your clipboard, record keystrokes, or take screenshots.

Notes and files are stored as ordinary files in a local folder only your Windows account can read (%LocalAppData%\Daynote). Uninstalling the app leaves that folder in place.

There are exactly two ways content leaves this PC, and you switch both on yourself: cloud sync and AI integration.

With cloud sync on, notes and files are encrypted in transit and at rest. It is not end-to-end encrypted by default, because the service also holds the key. Turning on "Lock notes" in Settings re-locks that key with a passphrase only you know and has the service destroy its own copy.

Windows 11 / 10 (x64)
```

### Product features

```
Pick a date and that day's notes, to-dos and files are all in one place
Write "-[] task" in a note and it appears in the To-do tab automatically
Read many days at once in the Timeline view
Float a note as an always-on-top sticky and edit it alongside the body
Tags, #hashtags in the body, and favorites to find notes again
Unified search across titles, bodies and tags
Attach images and files to a day and link them from the body
Works with MCP clients such as Claude Desktop (off until you enable it)
Back up and restore all your data as a single file
Light and dark themes, Korean and English interface
No network connection until you sign in. No telemetry
Notes, to-dos and tags sync free with a Google account
Image and file sync with Pro ($2.49/mo or $19.99/yr, 14-day free trial)
```

### Search terms

```
notes
journal
daily notes
calendar notes
to-do
daynote
markdown
```

---

## Pricing and availability

- **Price**: Free. The app is free; the subscription is sold outside the Store by Paddle,
  which is why there is no Store add-on to configure.
- **Markets**: all, unless you want to limit them.
- **Visibility**: public.
- **Release**: as soon as it passes certification, unless you want to hold it.

---

## Age rating (IARC)

Answer "no" to every content question — no violence, no sexual content, no profanity,
no gambling, no drugs. Then the three that are **yes** and are easy to miss:

- **Does the app allow users to interact or exchange content?** No. Sync is between the
  same person's own devices; there is no sharing between users, no comments, no messages.
- **Does the app share the user's location?** No.
- **Does the app allow purchases?** **Yes** — a digital subscription, purchased through
  an external browser page.

Expected result: everyone / 3+.

---

## What is still unresolved

- **Company vs Individual account (10.8.3).** The policy requires a Company account for a
  product that *requires* financial account information. Daynote's cloud sync is optional
  rather than primary functionality, so an Individual account may be fine — but this is a
  question to put to Partner Center support **before** the first submission that offers
  the subscription, not one to discover in review. If it does bite, the fallback that
  needs no company enrolment is to submit with the subscription hidden (clear
  `PADDLE_PRICE_ID_MONTHLY` and `PADDLE_PRICE_ID_ANNUAL` in `cloud/worker/wrangler.toml`
  and redeploy — `/v1/billing/status` then reports `can_checkout: false` and the app hides
  the buttons, with no app update needed).
- ~~**Screenshots.**~~ Regenerated 2026-09-10 at 1440×900 by `StoreScreenshotTests`,
  which renders the shipping Avalonia shell through the real service graph and writes
  four images per language:

  | File | Shows |
  | --- | --- |
  | `daynote-store-ko-01-overview.png` | The day: calendar with a month of activity, note list, editor, to-dos |
  | `daynote-store-ko-02-tags.png` | Tag chips on the note and the Tags panel |
  | `daynote-store-ko-03-files.png` | Attachments on a day |
  | `daynote-store-ko-04-dark.png` | The same shell in dark |

  English equivalents are under `en/` with the `-en-` prefix. Upload the Korean four for
  the ko-KR listing and the English four for en-US.

  The old four were deleted rather than kept: one of them advertised the clipboard
  drawer, and a stale marketing asset that still opens is worse than a missing one.

  Regenerating after a UI change is `dotnet test tests/Daynote.Desktop.Tests --filter
  StoreScreenshotTests` — the images are an output of the test suite now, so they cannot
  rot again without a run rewriting them.
