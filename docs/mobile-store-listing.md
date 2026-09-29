# App Store and Google Play listing — every field, ready to paste

The phone counterpart of [store-listing.md](store-listing.md) (Microsoft Store). Written 2026-09-29
for the first TestFlight / Play internal-testing release. Console steps are in
[MOBILE_RELEASE.md](MOBILE_RELEASE.md); images are in `docs/brand/app-store/` and
`docs/brand/google-play/` (rendered by `tests/Daynote.Mobile.Tests/StoreScreenshotTests.cs` with
`DAYNOTE_STORE_SHOTS=1`).

**Three rules this copy keeps, because breaking any of them is a rejection or a false declaration:**

1. **Nothing about buying.** No price, no "Pro", no Paddle, no "subscribe on the web" (App Store
   3.1.1 and Play's payments policy). The phone sells nothing; attachment sync is simply not promised.
2. **Not end-to-end encrypted by default.** The server holds the key unless the user turns the note
   lock on. Say exactly that (CLOUD_SYNC.md §1, PRIVACY.md).
3. **Only what the phone does.** No timeline, sticky notes, MCP, backup archive or hotkeys — those
   are desktop features.

---

## App Store Connect

| Field | Limit | Korean (primary) | English |
| --- | --- | --- | --- |
| Name | 30 | `Daynote - 날짜별 노트` | `Daynote - Notes by Day` |
| Subtitle | 30 | `날짜를 고르면 그날의 노트와 할 일` | `Pick a day, see its notes` |
| Category | — | Productivity (secondary: none) | |
| Age rating | — | 4+ (no objectionable content, no user-to-user interaction) | |
| Privacy policy URL | — | `https://daynote.arachat.cc/privacy` | |
| Support URL | — | `https://daynote.arachat.cc/` | |
| Copyright | — | `2026 Daynote` | |

**Keywords** (100 bytes, comma-separated, no spaces; Korean characters count 3 bytes each)

```
ko: 노트,메모,일기,할일,캘린더,달력,회의록,태그,동기화
en: notes,memo,journal,todo,calendar,daily,planner,tags,sync,meeting
```

**Promotional text** (170)

```
ko: 캘린더에서 날짜를 고르면 그날 쓴 노트와 할 일이 한자리에. 로그인하지 않아도 모든 기능을 쓸 수 있고, 원하면 PC와 휴대폰을 동기화합니다.
en: Pick a day on the calendar and everything you wrote that day is there. Every feature works without an account; sign in only if you want your phone and computer in step.
```

**Description** (4,000) — Korean

```
Daynote는 날짜별로 정리되는 노트 앱입니다. 캘린더에서 하루를 고르면 그날 쓴 노트와 그날 할 일이 모두 거기에 있습니다.

■ 하루가 단위입니다
날짜를 고르면 그날의 노트가 열립니다. 한 날짜에 노트를 여러 개 두고, 이름을 바꾸고, 즐겨찾기에 올릴 수 있습니다. 노트가 있는 날은 달력에 점으로 표시됩니다.

■ 할 일은 본문에 쓰면 됩니다
본문에 "-[] 장보기"라고 쓰면 목록 탭의 할 일에 나타납니다. 따로 할 일 목록을 관리할 필요가 없습니다.

■ 태그와 검색
노트에 태그를 붙이고, 본문의 #해시태그도 자동으로 인식됩니다. 검색은 제목, 본문, 태그를 한 번에 찾고 한글도 정확히 찾습니다.

■ 라이트·다크, 한국어·영어

■ 계정 없이도 전부
로그인하지 않으면 노트는 이 기기에만 저장되고, 앱은 인터넷에 연결하지 않습니다.

■ 동기화 (선택)
Google 또는 Apple 계정으로 로그인하면 노트, 할 일, 태그, 즐겨찾기가 휴대폰과 PC의 Daynote에서 같은 상태로 유지됩니다. 앱을 열 때와 저장한 뒤 자동으로 동기화됩니다.

동기화된 노트는 전송 중과 저장 시 암호화되지만, 기본 설정에서는 서비스가 암호화 키를 보관합니다. 운영자도 내용을 볼 수 없게 하려면 '노트 잠금'을 켜세요. 잠금 암호는 기기에서만 쓰이며 서버로 보내지지 않습니다.

계정은 설정에서 언제든 삭제할 수 있습니다. 삭제하면 서버에 있는 계정과 동기화된 노트가 즉시 지워지고, 기기에 있는 노트는 그대로 남습니다.

Apple 계정과 Google 계정은 서로 다른 계정입니다. PC와 함께 쓰려면 Google 계정으로 로그인하세요.
```

**Description** — English

```
Daynote is a notes app organised by day. Pick a date on the calendar and everything you wrote that day, and everything you meant to do, is right there.

■ The day is the unit
Choose a date and its notes open. Keep several notes on one day, rename them, and star the ones you come back to. Days with notes get a dot on the calendar.

■ To-dos live in the text
Write "-[] groceries" in any note and it shows up under To-dos. There is no separate list to keep.

■ Tags and search
Tag a note, or just write #hashtags in the body. Search looks through titles, text and tags at once.

■ Light and dark, Korean and English

■ Everything works without an account
Signed out, your notes stay on this device and the app does not go online at all.

■ Sync (optional)
Sign in with Google or Apple and your notes, to-dos, tags and favourites stay the same on your phone and in Daynote on your computer. It syncs on its own when you open the app and after you save.

Synced notes are encrypted in transit and at rest, but by default the service holds the key. Turn on the note lock and not even the people running the service can read them; the passphrase is used on your device and never sent.

You can delete your account from Settings at any time. That removes the account and your synced notes from the server immediately; the notes on your device stay.

An Apple sign-in and a Google sign-in are separate accounts. To use Daynote on your computer too, sign in with Google.
```

**App Review notes**

```
Signing in is optional: every feature works signed out. To review sync, use Sign in with Apple or any Google account; a new account is created on first sign-in. Account deletion: Settings → account card → Delete account → Delete permanently. The app offers no purchases.
```

**App Privacy** ("Data Types")

| Data type | Collected | Linked to user | Tracking | Purpose |
| --- | --- | --- | --- | --- |
| Contact Info → Email Address | Yes, only with an account | Yes | No | App Functionality |
| User Content → Other User Content (notes, tags, dates) | Yes, only with an account | Yes | No | App Functionality |
| Identifiers, Usage Data, Diagnostics, Location, Contacts | No | | | |

These match `src/Daynote.Mobile.iOS/PrivacyInfo.xcprivacy`; change both together.

---

## Google Play Console

| Field | Limit | Korean (default) | English |
| --- | --- | --- | --- |
| App name | 30 | `Daynote - 날짜별 노트` | `Daynote - Notes by Day` |
| Short description | 80 | `캘린더에서 날짜를 고르면 그날의 노트와 할 일이 한자리에` | `Pick a day on the calendar and see that day's notes and to-dos` |
| Full description | 4,000 | the App Store description above, Korean | the App Store description above, English |
| Category | — | Productivity | |
| Contact email | — | the support address on the privacy page | |
| Privacy policy | — | `https://daynote.arachat.cc/privacy` | |

Play's full description may not mention Apple sign-in, since the Android app has only Google: in
the two descriptions, change "Google 또는 Apple 계정으로" to "Google 계정으로" / "Sign in with Google
or Apple" to "Sign in with Google", and drop the last paragraph.

**App content**

| Section | Answer |
| --- | --- |
| Ads | No ads |
| App access | All functionality available without special access (sign-in optional) |
| Target audience | 18 and over |
| Content rating | Utility / productivity; no violence, no user-generated content shared with others |
| Account deletion | Web link: `https://daynote.arachat.cc/delete-account`; in-app: Settings → account card |
| Government / financial / health | No |

**Data safety**

| Question | Answer |
| --- | --- |
| Collects or shares user data? | Collects: yes. Shares: no |
| Encrypted in transit | Yes |
| Users can request deletion | Yes |
| Personal info → Email address | Collected, not shared, required only for sync, purpose: Account management, App functionality |
| App activity / App info and performance / Device IDs / Location | Not collected |
| Messages / Files and docs → "Other in-app content" (notes) | Collected, not shared, optional (only with an account), purpose: App functionality |
| Is data processed ephemerally? | No |
