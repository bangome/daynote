// Renders template.html into public/index.html (ko) and public/en/index.html (en).
// Run: node cloud/site/build.mjs   (also runs before `wrangler deploy` via the worker's predeploy).
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const template = readFileSync(join(here, 'template.html'), 'utf8');

// Until the Store listing is live this searches the Store for the app; replace with the
// listing URL (https://apps.microsoft.com/detail/<ProductId>) once Partner Center assigns one.
const STORE_URL = 'https://apps.microsoft.com/search?query=Daynote';

// ── Things Paddle's website review looks for. Fill these in before requesting domain approval. ──
// Who operates the service, as it should appear in the terms and on the support page.
const OPERATOR = { ko: '위드큐브', en: 'Withcube' };
// A mailbox somebody reads. Paddle and the Store both want a working support contact.
const SUPPORT_EMAIL = 'aracube@gmail.com';
// The subscription price exactly as sold in Paddle → Catalog. Shown on /pricing and in the terms.
// Decided 2026-09-04: below Obsidian Sync / Bear on the annual plan, annual is the plan to push.
const PRICE_LINE = { ko: '월 ₩2,900 · 연 ₩24,000', en: '$2.49 / month · $19.99 / year' };
const PRICE_NOTE = { ko: '연간 결제 시 월 ₩2,000, 월 결제 대비 31% 할인', en: 'Annual works out to $1.67 a month, 31% off monthly' };
// Paddle.js client-side token (Developer tools → Authentication → Client-side tokens) and the
// environment it belongs to. The /checkout page is the "default payment link" Paddle opens
// server-created transactions on; it needs both to render the checkout.
// A client-side token is meant to be public: it can only open checkouts, never read or charge.
const PADDLE_CLIENT_TOKEN = 'live_1a35a8eff5ee10bb87eeac3b922';
const PADDLE_ENVIRONMENT = 'production';                                      // 'sandbox' | 'production'

const calDow = (d) => d.map((x) => `<span>${x}</span>`).join('');
function calDays(today, dots, dots2) {
  const cells = ['', '', '']; // July 2026 starts on a Wednesday
  for (let d = 1; d <= 31; d++) cells.push(String(d));
  return cells
    .map((d) => {
      const n = Number(d);
      const cls = [n === today && 'today', dots.includes(n) && 'dot', dots2.includes(n) && 'dot2'].filter(Boolean).join(' ');
      return `<span${cls ? ` class="${cls}"` : ''}>${d}</span>`;
    })
    .join('');
}
const cal = { calDays: calDays(27, [3, 8, 14, 15, 21, 22, 27, 29], [15, 27]) };

const common = { storeUrl: STORE_URL };

const linksKo = {
  pricingHref: '/pricing/', termsHref: '/terms/', refundHref: '/refund/', supportHref: '/support/',
  navPricing: '요금', navTerms: '이용약관', navRefund: '환불 정책', navSupport: '지원',
  operator: OPERATOR.ko, supportEmail: SUPPORT_EMAIL, priceLine: PRICE_LINE.ko, priceNote: PRICE_NOTE.ko,
};
const linksEn = {
  pricingHref: '/en/pricing/', termsHref: '/en/terms/', refundHref: '/en/refund/', supportHref: '/en/support/',
  navPricing: 'Pricing', navTerms: 'Terms', navRefund: 'Refund policy', navSupport: 'Support',
  operator: OPERATOR.en, supportEmail: SUPPORT_EMAIL, priceLine: PRICE_LINE.en, priceNote: PRICE_NOTE.en,
};

const ko = {
  ...common, ...cal, ...linksKo,
  lang: 'ko', ogLocale: 'ko_KR', home: '/', canonical: 'https://daynote.arachat.cc/',
  altHref: '/en/', altLang: 'en', altLabel: 'EN',
  title: 'Daynote — 하루를 더 또렷하게 정리하세요',
  description: '날짜별 노트, 할 일, 파일을 하나의 일일 작업공간에. 기본은 내 PC에만 저장하고, 원하면 Google 로그인으로 여러 PC에 동기화하는 Windows 노트 앱, Daynote.',
  skip: '본문으로 건너뛰기', navLabel: '주 메뉴', navFeatures: '기능', navSync: '동기화', navPrivacy: '프라이버시', navDownload: '다운로드', navGet: 'Store에서 받기',
  heroEyebrow: 'Windows용 날짜별 노트',
  heroTitle: '하루를 더 <em>또렷하게</em><br>정리하세요.',
  heroLede: '노트, 할 일, 파일을 하나의 집중된 일일 작업공간에 모으세요. 캘린더에서 날짜를 고르면 그날의 모든 것이 거기 있습니다.',
  heroCta: 'Microsoft Store에서 받기', heroSecondary: '어떻게 다른지 보기',
  heroMeta: '무료 · Windows 11 / 10 (x64) · 클라우드 동기화는 선택 구독',
  heroAlt: 'Daynote 메인 화면. 왼쪽에 2026년 7월 캘린더와 그날의 노트 목록, 가운데에 노트 편집기, 오른쪽에 할 일·파일 탭.',
  calMonth: '2026년 7월', calDow: calDow(['일', '월', '화', '수', '목', '금', '토']),
  thesisTitle: '폴더 대신 날짜.',
  thesisBody1: '메모는 대부분 "언제"와 함께 떠오릅니다. 지난주 화요일 회의, 어제 받은 파일, 오늘 처리할 일. Daynote는 그 기억 방식을 그대로 구조로 씁니다. 캘린더의 하루가 하나의 작업공간이고, 노트도 할 일도 파일도 그 날짜 아래에 모입니다.',
  thesisBody2: '폴더 이름을 고민하거나 태그 체계를 관리할 필요가 없습니다. 날짜를 누르면 그날이 열리고, 검색하면 어느 날이든 바로 찾아갑니다.',
  featEyebrow: '기능', featTitle: '적게 만들고, 매일 쓰이게.',
  f1Title: '본문에 적으면 할 일이 됩니다',
  f1Body: '노트 본문에 <code>-[]</code>라고 쓰면 체크박스가 되고, <code>(7/27 14:00)</code>처럼 마감을 붙일 수 있습니다. 오른쪽 "할 일" 탭에 모든 날짜의 항목이 날짜별로 모이고, 마감이 지나면 붉게 표시됩니다.',
  f1Demo: '<b>-[]</b> 오전 스탠드업 정리 <i>(7/27 10:00)</i>\n<b>-[x]</b> 지난주 주간 보고 검토\n<b>-[]</b> 고객 회신 보내기 <i>(7/27 15:00)</i>',
  f1Today: '오늘', f1Item1: '오전 스탠드업 정리', f1Item2: '고객 회신 보내기', f1Item3: '지난주 주간 보고 검토',
  f2Title: '파일은 그날에 붙여 둡니다',
  f2Body: '파일 탭에 끌어다 놓거나 본문에 이미지·파일을 붙여넣으면 그날의 파일로 저장되고, 본문에는 링크 한 줄만 남습니다. 링크를 누르면 파일 탭에서 바로 열립니다. 나중에 "그때 받은 문서"를 찾을 때 날짜만 기억하면 됩니다.',
  f2Drop: '+ 파일 · 이미지 추가', f2File1: '회의-화이트보드.png', f2File2: '배포-릴리스노트-v1.5.pdf', f2File3: '8월-일정표.xlsx',
  f2Demo: '회의 메모는 사진 참고 → <b>[[file:회의-화이트보드.png]]</b>',
  f3Title: '제목·본문·파일을 한 번에',
  f3Body: '검색창 하나로 노트 제목과 본문, 첨부 파일 이름, 그리고 날짜를 함께 찾습니다. 한글 한두 글자도 그대로 검색되고, 결과를 누르면 정확히 그 노트, 그 항목으로 이동합니다.',
  f3Query: '배포', f3GroupNotes: '노트', f3GroupClip: '날짜', f3GroupFiles: '파일',
  f3Hit1: '8월 <mark>배포</mark> 체크리스트 초안', f3Hit2: '2026년 8월 12일 — 노트 3개', f3Hit3: '<mark>배포</mark>-릴리스노트-v1.5.pdf',
  f4Title: '트레이에 있어도, 한 번의 키로',
  f4Body: 'Daynote는 트레이에 상주합니다. 전역 단축키로 어디서든 불러오거나 오늘 날짜에 포스트잇을 바로 띄울 수 있고, 앱 안의 단축키는 설정에서 원하는 조합으로 바꿀 수 있습니다.',
  f4K1: '어디서든 Daynote 불러오기', f4K2: '오늘 새 포스트잇', f4K3: '새 노트', f4K4: '오늘로 이동', f4K5: '포스트잇으로 띄우기', f4K6: '설정',
  moreTitle: '그리고',
  m1T: '타임라인 보기', m1B: '하루씩 넘기는 대신, 노트를 날짜순으로 길게 펼쳐 한 번에 훑어봅니다.',
  m2T: '포스트잇', m2B: '노트를 항상 위에 고정되는 작은 창으로 띄우고, 본문과 실시간으로 함께 편집합니다.',
  m3T: '태그와 즐겨찾기', m3B: '노트에 태그를 붙이고, 본문의 #해시태그는 자동으로 인식됩니다. 자주 여는 노트는 즐겨찾기 탭에.',
  m4T: 'AI 연동 (MCP)', m4B: 'Claude Desktop 같은 MCP 클라이언트가 내 노트를 읽고 쓸 수 있습니다. 직접 등록하기 전까지는 꺼져 있습니다.',
  m5T: '백업과 복원', m5B: '설정에서 데이터를 한 파일로 내보내고 되돌립니다. 업데이트·재설치를 거쳐도 데이터는 그대로 남습니다.',
  m6T: '라이트와 다크', m6B: 'Windows 테마를 따르거나 한 번의 키로 바꿉니다. 한국어와 영어 UI를 모두 제공합니다.',
  shotsTitle: '화면',
  shot1: '캘린더, 노트 목록, 편집기, 할 일 탭이 한 화면에', shot3: '끌어다 놓은 파일과 본문 링크가 만나는 파일 탭', shot4: '전역 단축키와 앱 내 단축키 설정',
  privEyebrow: '프라이버시',
  privTitle: '기본은 내 PC.<br>나가는 건 내가 켠 것만.',
  privLede: 'Daynote에는 텔레메트리가 없습니다. 노트와 파일은 내 Windows 계정만 읽을 수 있는 로컬 폴더에 저장되고, 로그인하기 전까지 앱은 인터넷 연결을 열지 않습니다. 내용이 이 PC를 벗어나는 길은 두 가지뿐이고, 둘 다 내가 직접 켭니다. 클라우드 동기화와 AI 연동입니다.',
  privLink: '프라이버시 정책 전문 읽기',
  p1K: '수집', p1V: '사용 통계도, 오류 보고도 없습니다. 동기화를 켜면 Google 계정 ID와 이메일, 그리고 노트가 바뀐 시각이 서비스에 남습니다.',
  p2K: '저장 위치', p2V: '내 PC의 %LocalAppData%\\Daynote. 앱이 따로 암호화하지 않는 일반 파일이라 언제든 직접 백업할 수 있습니다.',
  p3K: '백그라운드', p3V: '클립보드 감시도, 키 입력 기록도, 스크린샷도 없습니다. 앱 안에서 내가 한 동작에만 반응합니다.',
  p4K: '클라우드 사본', p4V: '전송 중에도, 서버에 저장될 때도 암호화됩니다. 기본 설정에서는 서비스가 열쇠를 함께 보관하므로 종단간 암호화는 아닙니다. 설정의 "노트 잠금"을 켜면 나만 아는 암호로 열쇠를 다시 잠그고 서비스 쪽 사본은 파기됩니다.',
  p5K: '결제', p5V: 'Daynote는 카드 정보를 보지 않습니다. 결제는 판매자 Paddle의 페이지에서 이뤄지고, 서비스에는 구독 상태와 갱신일만 전달됩니다.',
  dlTitle: '오늘부터 시작하세요.',
  dlLede: 'Microsoft Store에서 무료로 설치합니다. 설치 후 첫 실행에서 짧은 튜토리얼이 기능을 안내합니다.',
  dlCta: 'Microsoft Store에서 받기',
  s1K: '지원 OS', s1V: 'Windows 11, Windows 10 21H2 LTSC / Enterprise (x64)',
  s2K: '가격', s2V: '무료',
  s5K: '클라우드 동기화', s5V: '월 ₩2,900 또는 연 ₩24,000. 14일 무료 체험 후 앱 안에서 결제',
  s3K: '데이터 위치',
  s4K: '현재 버전', s4V: '1.5.0',
  footNavLabel: '바닥글 메뉴',
  syncEyebrow: '클라우드 동기화', syncTitle: '여러 PC에서, 같은 하루.',
  syncLede: '회사 PC에서 쓴 노트를 집에서 이어 쓰고 싶다면 Google 계정으로 로그인하세요. 노트와 할 일이 로그인한 모든 PC에서 같은 상태로 유지됩니다. 켜지 않으면 Daynote는 지금처럼 완전히 로컬로만 동작합니다.',
  syncNote: '동기화는 유료 구독입니다. 14일 무료 체험이 가입 시 한 번 제공되고, 이후 월 ₩2,900 또는 연 ₩24,000입니다. 동기화는 백업이 아니라 전파입니다. 한 PC에서 지우면 모든 PC에서 지워지니, 백업은 설정의 백업 기능으로 따로 두세요.',
  st1T: 'Google로 로그인', st1B: '비밀번호를 새로 만들지 않습니다. 시스템 브라우저에서 Google에 로그인하면 끝입니다.',
  st2T: '14일 무료 체험', st2B: '가입과 함께 시작되고 카드 등록은 필요 없습니다. 체험이 끝나면 동기화만 멈추고, 노트는 어디서도 지워지지 않습니다.',
  st3T: '암호화된 사본, 원하면 잠금까지', st3B: '전송과 저장 모두 암호화됩니다. 서비스도 읽을 수 없게 하려면 "노트 잠금"을 켜세요. 나만 아는 암호가 열쇠가 되고, 잊으면 복구 키가 유일한 길입니다.',
  st4T: '언제든 해지', st4B: '구독이 끝나면 동기화가 멈출 뿐입니다. 이 PC의 노트는 그대로이고, 이미 올라간 사본도 삭제되지 않습니다.',
};

const en = {
  ...common, ...cal, ...linksEn,
  lang: 'en', ogLocale: 'en_US', home: '/en/', canonical: 'https://daynote.arachat.cc/en/',
  altHref: '/', altLang: 'ko', altLabel: '한국어',
  title: 'Daynote — See your day clearly',
  description: 'Notes, to-dos, and files gathered by date in one focused daily workspace. A Windows notes app that keeps everything on your PC by default, with optional Google sign-in sync across PCs.',
  skip: 'Skip to content', navLabel: 'Main', navFeatures: 'Features', navSync: 'Sync', navPrivacy: 'Privacy', navDownload: 'Download', navGet: 'Get it on Store',
  heroEyebrow: 'Dated notes for Windows',
  heroTitle: 'See your day<br><em>clearly.</em>',
  heroLede: 'Notes, to-dos, and files in one focused daily workspace. Pick a date on the calendar and everything from that day is right there.',
  heroCta: 'Get it from Microsoft Store', heroSecondary: 'See how it works',
  heroMeta: 'Free · Windows 11 / 10 (x64) · Cloud sync is an optional subscription',
  heroAlt: 'Daynote main window: a July 2026 calendar and the day\'s note list on the left, the note editor in the middle, and To-do and Files tabs on the right.',
  calMonth: 'July 2026', calDow: calDow(['S', 'M', 'T', 'W', 'T', 'F', 'S']),
  thesisTitle: 'Dates, not folders.',
  thesisBody1: 'Most memories come attached to a "when": the meeting last Tuesday, the file you received yesterday, what you need to finish today. Daynote uses that as its structure. Each day on the calendar is a workspace, and notes, to-dos, and files all gather under its date.',
  thesisBody2: 'No folder names to invent, no tag taxonomy to maintain. Click a date and the day opens. Search, and you jump straight to whichever day it was.',
  featEyebrow: 'Features', featTitle: 'Few things, used every day.',
  f1Title: 'Type it in the note. It becomes a to-do.',
  f1Body: 'Write <code>-[]</code> in a note body and it turns into a checkbox. Add a due time like <code>(7/27 14:00)</code>. The To-do tab on the right gathers items from every date, grouped by day, and overdue ones turn red.',
  f1Demo: '<b>-[]</b> Write up the morning standup <i>(7/27 10:00)</i>\n<b>-[x]</b> Review last week\'s status report\n<b>-[]</b> Send the reply to the client <i>(7/27 15:00)</i>',
  f1Today: 'Today', f1Item1: 'Write up the morning standup', f1Item2: 'Send the reply to the client', f1Item3: 'Review last week\'s status report',
  f2Title: 'Files stay with their day',
  f2Body: 'Drop files onto the Files tab, or paste an image or file into the body: it is stored as that day\'s file and only a one-line link stays in the text. Click the link and it opens in the Files tab. When you later need "that document I got", the date is all you have to remember.',
  f2Drop: '+ Add files or images', f2File1: 'meeting-whiteboard.png', f2File2: 'release-notes-v1.5.pdf', f2File3: 'august-schedule.xlsx',
  f2Demo: 'See the photo for the notes → <b>[[file:meeting-whiteboard.png]]</b>',
  f3Title: 'Titles, bodies, and files in one search',
  f3Body: 'One search box covers note titles and bodies, attachment names, and dates. Short queries work as typed, including one or two Korean characters, and each result deep-links to exactly that note or item.',
  f3Query: 'release', f3GroupNotes: 'Notes', f3GroupClip: 'Dates', f3GroupFiles: 'Files',
  f3Hit1: 'August <mark>release</mark> checklist draft', f3Hit2: 'August 12, 2026 — 3 notes', f3Hit3: '<mark>release</mark>-notes-v1.5.pdf',
  f4Title: 'One keystroke, even from the tray',
  f4Body: 'Daynote lives in the tray. Global shortcuts summon it from anywhere or drop a sticky note onto today, and every in-app shortcut can be rebound in settings.',
  f4K1: 'Summon Daynote from anywhere', f4K2: 'New sticky note for today', f4K3: 'New note', f4K4: 'Go to today', f4K5: 'Pop out as a sticky note', f4K6: 'Settings',
  moreTitle: 'And also',
  m1T: 'Timeline view', m1B: 'Instead of paging day by day, unroll your notes in date order and skim them in one pass.',
  m2T: 'Sticky notes', m2B: 'Pop a note out into a small always-on-top window. The sticky and the body edit together in real time.',
  m3T: 'Tags and favorites', m3B: 'Tag notes, and #hashtags in the body are recognized automatically. Notes you keep coming back to live in the Favorites tab.',
  m4T: 'AI integration (MCP)', m4B: 'Let an MCP client such as Claude Desktop read and write your notes. Off until you register it yourself.',
  m5T: 'Backup and restore', m5B: 'Export everything to a single file from settings and restore it later. Updates and reinstalls keep your data in place.',
  m6T: 'Light and dark', m6B: 'Follow the Windows theme or switch with one key. The UI ships in English and Korean.',
  shotsTitle: 'Screens',
  shot1: 'Calendar, note list, editor, and the To-do tab in one window', shot3: 'The Files tab, where dropped files meet body links', shot4: 'Global and in-app shortcut settings',
  privEyebrow: 'Privacy',
  privTitle: 'Your PC by default.<br>Only what you switch on leaves.',
  privLede: 'Daynote has no telemetry. Notes and files are stored in a local folder only your Windows account can read, and until you sign in the app opens no internet connection. Exactly two things can carry content off this PC, and you switch on both yourself: cloud sync and the AI integration.',
  privLink: 'Read the full privacy policy',
  p1K: 'Collected', p1V: 'No usage statistics, no crash reports. With sync on, the service holds your Google account id and email, and when each note last changed.',
  p2K: 'Stored at', p2V: '%LocalAppData%\\Daynote on your PC. Plain files the app does not encrypt, so you can back them up yourself at any time.',
  p3K: 'Background', p3V: 'No clipboard monitoring, no keystroke logging, no screenshots. It only acts on what you do inside the app.',
  p4K: 'Cloud copy', p4V: 'Encrypted in transit and at rest. By default the service also holds the key, so this is not end-to-end encryption. Turn on "Lock my notes" in settings and the key is re-sealed with a passphrase only you know, and the service destroys its copy.',
  p5K: 'Payment', p5V: 'Daynote never sees your card. Checkout happens on a page run by Paddle, the merchant of record; the service receives only a subscription status and a renewal date.',
  dlTitle: 'Start today.',
  dlLede: 'Install it free from the Microsoft Store. A short tour on first launch walks you through the features.',
  dlCta: 'Get it from Microsoft Store',
  s1K: 'Supported', s1V: 'Windows 11, Windows 10 21H2 LTSC / Enterprise (x64)',
  s2K: 'Price', s2V: 'Free',
  s5K: 'Cloud sync', s5V: '$2.49 a month or $19.99 a year. 14-day free trial, then purchased in the app',
  s3K: 'Data location',
  s4K: 'Current version', s4V: '1.5.0',
  footNavLabel: 'Footer',
  syncEyebrow: 'Cloud sync', syncTitle: 'The same day, on every PC.',
  syncLede: 'Want to pick up at home what you wrote at work? Sign in with your Google account and your notes and to-dos stay identical on every PC you sign in to. Leave it off and Daynote stays exactly as local as it is today.',
  syncNote: 'Sync is a paid subscription. A 14-day free trial is granted once at sign-up; after that it is $2.49 a month or $19.99 a year. Sync propagates, it does not back up: delete on one PC and it is gone on all of them, so keep backups with the backup feature in settings.',
  st1T: 'Sign in with Google', st1B: 'No new password to invent. Sign in to Google in your system browser and you are done.',
  st2T: '14-day free trial', st2B: 'Starts at sign-up, no card required. When it ends only the syncing stops; no note is deleted anywhere.',
  st3T: 'An encrypted copy, locked if you want', st3B: 'Encrypted in transit and at rest. To make it unreadable to the service too, turn on "Lock my notes": a passphrase only you know becomes the key, and the recovery key is the only way back if you forget it.',
  st4T: 'Cancel any time', st4B: 'When a subscription ends, syncing stops. Nothing else happens: the notes on this PC stay, and the copy already uploaded is kept.',
};

function render(source, strings) {
  if (strings === undefined) { strings = source; source = template; }
  const missing = [];
  const html = source.replace(/\{\{(\w+)\}\}/g, (_, key) => {
    if (!(key in strings)) { missing.push(key); return ''; }
    return strings[key];
  });
  if (missing.length) throw new Error(`missing strings: ${[...new Set(missing)].join(', ')}`);
  return html;
}

const out = join(here, 'public');
writeFileSync(join(out, 'index.html'), render(ko));
mkdirSync(join(out, 'en'), { recursive: true });
writeFileSync(join(out, 'en', 'index.html'), render(en));

// ── Sub-pages: one shell (page.html), bodies in content/<slug>.<lang>.html ──
const pageShell = readFileSync(join(here, 'page.html'), 'utf8');
const UPDATED = '2026-09-04';

const PADDLE_HEAD = `<script src="https://cdn.paddle.com/paddle/v2/paddle.js"></script>
<script>
  // Paddle opens server-created transactions here via ?_ptxn=txn_...; Paddle.Initialize picks the
  // parameter up on its own. Without a client token the page only shows the explanatory text.
  (function () {
    var token = ${JSON.stringify(PADDLE_CLIENT_TOKEN)};
    if (!token || !window.Paddle) return;
    if (${JSON.stringify(PADDLE_ENVIRONMENT)} === 'sandbox') Paddle.Environment.set('sandbox');
    // Retain: the Worker appends the returning customer's Paddle id (ctm_...) to the checkout URL.
    var ctm = new URLSearchParams(location.search).get('ctm');
    var init = { token: token, checkout: { settings: { displayMode: 'inline', frameTarget: 'checkout-frame', frameInitialHeight: '480', frameStyle: 'width:100%;min-width:312px;background-color:transparent;border:none;' } } };
    if (ctm && /^ctm_[a-z0-9]+$/.test(ctm)) init.pwCustomer = { id: ctm };
    Paddle.Initialize(init);
    var wait = document.querySelector('.checkout-wait'); if (wait) wait.remove();
  })();
</script>`;

const PAGES = [
  { slug: 'pricing', cls: 'page--pricing',
    ko: { title: '요금', eyebrow: '요금', desc: 'Daynote 앱은 무료, 클라우드 동기화는 14일 체험 뒤 구독. 요금과 결제 방식.', meta: '앱은 무료 · 동기화는 선택 구독' },
    en: { title: 'Pricing', eyebrow: 'Pricing', desc: 'The Daynote app is free; cloud sync is a subscription after a 14-day trial. Prices and how payment works.', meta: 'App is free · sync is an optional subscription' } },
  { slug: 'terms', cls: 'page--legal',
    ko: { title: '이용약관', eyebrow: '법적 고지', desc: 'Daynote 앱과 클라우드 동기화 서비스의 이용 조건.', meta: `최종 수정 ${UPDATED}` },
    en: { title: 'Terms of Service', eyebrow: 'Legal', desc: 'The terms that govern the Daynote app and the cloud sync service.', meta: `Last updated ${UPDATED}` } },
  { slug: 'refund', cls: 'page--legal',
    ko: { title: '환불 정책', eyebrow: '법적 고지', desc: '클라우드 동기화 구독의 체험, 해지, 환불 규정.', meta: `최종 수정 ${UPDATED}` },
    en: { title: 'Refund Policy', eyebrow: 'Legal', desc: 'Trial, cancellation, and refund rules for the cloud sync subscription.', meta: `Last updated ${UPDATED}` } },
  { slug: 'support', cls: 'page--support',
    ko: { title: '지원', eyebrow: '지원', desc: 'Daynote 문의 방법과 자주 겪는 문제.', meta: '영업일 기준 3일 안에 회신' },
    en: { title: 'Support', eyebrow: 'Support', desc: 'How to reach Daynote, and answers to common problems.', meta: 'Replies within 3 business days' } },
  { slug: 'checkout', cls: 'page--checkout', head: PADDLE_HEAD, noindex: true,
    ko: { title: '결제', eyebrow: '클라우드 동기화', desc: 'Daynote 클라우드 동기화 구독 결제.', meta: 'Paddle이 처리하는 안전한 결제' },
    en: { title: 'Checkout', eyebrow: 'Cloud sync', desc: 'Checkout for the Daynote cloud sync subscription.', meta: 'Secure payment processed by Paddle' } },
];

for (const page of PAGES) {
  for (const [lang, base] of [['ko', ko], ['en', en]]) {
    const meta = page[lang];
    const body = readFileSync(join(here, 'content', `${page.slug}.${lang}.html`), 'utf8');
    const prefix = lang === 'ko' ? '' : '/en';
    const strings = {
      ...base, slug: page.slug, body, pageClass: page.cls,
      pageTitle: meta.title, pageEyebrow: meta.eyebrow, pageDescription: meta.desc, pageMeta: meta.meta,
      canonical: `https://daynote.arachat.cc${prefix}/${page.slug}/`,
      altHref: lang === 'ko' ? `/en/${page.slug}/` : `/${page.slug}/`,
      head: (page.head ?? '') + (page.noindex ? '\n<meta name="robots" content="noindex">' : ''),
    };
    // The body may itself reference {{supportEmail}} etc., so render twice.
    const html = render(render(pageShell, strings), strings);
    const dir = join(out, ...(lang === 'ko' ? [page.slug] : ['en', page.slug]));
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'index.html'), html);
  }
}
console.log(`site: wrote index (ko/en) and ${PAGES.length} sub-pages x 2 languages`);
