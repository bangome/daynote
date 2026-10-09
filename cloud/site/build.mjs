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
// The Mac app is a notarized Developer ID build, attached to the newest GitHub release under fixed
// names so these links never change between versions.
const MAC_URL = 'https://github.com/bangome/daynote/releases/latest/download/Daynote-mac-arm64.zip';
const MAC_INTEL_URL = 'https://github.com/bangome/daynote/releases/latest/download/Daynote-mac-x64.zip';

// ── Phone stores ──
// Neither listing is public yet. While a switch is false its badge renders without a link
// (aria-disabled, not clickable) and with a "Coming soon" caption under it; flip it to true the day
// the listing goes live and the badge links to the store. One switch per store.
const APP_STORE_LIVE = false;
const PLAY_STORE_LIVE = false;
const APP_STORE_URL = 'https://apps.apple.com/app/id6817146422';
const PLAY_STORE_URL = 'https://play.google.com/store/apps/details?id=cc.arachat.daynote';

// The badges are the stores' own artwork, unmodified, in public/img/badges:
//   app-store-{ko,en}.svg   Apple Marketing Tools, "Download on the App Store", black, ko-kr / en-us
//   google-play-{ko,en}.png play.google.com/intl/en_us/badges ({ko,en}_badge_web_generic.png), with
//                           only the transparent padding cropped so both badges can share one height
// Width/height are the files' own sizes; CSS sets the rendered height (40px minimum, Apple's rule)
// and the clear space around them (a quarter of the height, both brands' rule).
const BADGES = {
  apple: { ko: ['app-store-ko.svg', 130, 40], en: ['app-store-en.svg', 120, 40] },
  google: { ko: ['google-play-ko.png', 646, 192], en: ['google-play-en.png', 564, 168] },
};
function storeBadges(lang) {
  const t = lang === 'ko'
    ? { apple: 'App Store에서 다운로드 하기', google: 'Google Play에서 다운로드', soon: '출시 예정' }
    : { apple: 'Download on the App Store', google: 'Get it on Google Play', soon: 'Coming soon' };
  const one = (store, live, url) => {
    const [file, w, h] = BADGES[store][lang];
    const img = `<img src="/img/badges/${file}" width="${w}" height="${h}" alt="${t[store]}">`;
    return live
      ? `<div class="store"><a class="store__badge" href="${url}" rel="noopener">${img}</a></div>`
      : `<div class="store store--soon"><span class="store__badge" role="link" aria-disabled="true">${img}</span><span class="store__soon">${t.soon}</span></div>`;
  };
  return one('apple', APP_STORE_LIVE, APP_STORE_URL) + one('google', PLAY_STORE_LIVE, PLAY_STORE_URL);
}

// ── Things Paddle's website review looks for. Fill these in before requesting domain approval. ──
// Who operates the service, as it should appear in the terms and on the support page.
const OPERATOR = { ko: '위드큐브', en: 'Withcube' };
// A mailbox somebody reads. Paddle and the Store both want a working support contact.
const SUPPORT_EMAIL = 'aracube@gmail.com';
// The subscription price exactly as sold in Paddle → Catalog. Shown on /pricing and in the terms.
// Decided 2026-09-04: below Obsidian Sync / Bear on the annual plan, annual is the plan to push.
// Premium added 2026-09-30 at roughly twice Pro, for unlimited (fair-use) storage.
const PRICE_LINE = {
  ko: 'Pro 월 ₩2,900 · 연 ₩24,000 / Premium 월 ₩5,900 · 연 ₩48,000',
  en: 'Pro $2.49 / month · $19.99 / year; Premium $4.99 / month · $39.99 / year',
};
const PRICE_NOTE = { ko: '연간 결제 시 월 결제 대비 Pro 31%, Premium 32% 할인', en: 'Annual works out about 33% below monthly' };
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

const common = { storeUrl: STORE_URL, macUrl: MAC_URL, macIntelUrl: MAC_INTEL_URL };

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
  ...common, ...cal, ...linksKo, storeBadges: storeBadges('ko'),
  lang: 'ko', ogLocale: 'ko_KR', home: '/', canonical: 'https://daynote.arachat.cc/',
  altHref: '/en/', altLang: 'en', altLabel: 'EN',
  title: 'Daynote — 하루를 더 또렷하게 정리하세요',
  description: '날짜별 노트, 할 일, 파일을 하나의 일일 작업공간에. 기본은 내 PC에만 저장하고, 원하면 Google 로그인으로 여러 PC에 동기화하는 Windows 노트 앱, Daynote.',
  skip: '본문으로 건너뛰기', navLabel: '주 메뉴', navFeatures: '기능', navSync: '동기화', navPrivacy: '프라이버시', navDownload: '다운로드', navGet: 'Store에서 받기',
  heroEyebrow: '날짜별 노트 · Windows · Mac · iPhone · Android',
  heroTitle: '하루를 더 <em>또렷하게</em><br>정리하세요.',
  heroLede: '노트, 할 일, 파일을 하나의 집중된 일일 작업공간에 모으세요. 캘린더에서 날짜를 고르면 그날의 모든 것이 거기 있습니다.',
  heroCta: 'Microsoft Store에서 받기', heroMac: 'Mac용 다운로드', heroSecondary: '어떻게 다른지 보기',
  heroMeta: '무료 · Windows 11 / 10 (x64) · macOS 12 이상 · 노트 동기화 무료, 파일 동기화는 Pro',
  heroAlt: 'Daynote 메인 화면. 왼쪽에 검색과 노트·할 일·태그·파일 사이드바와 달력, 가운데에 노트 편집기, 오른쪽에 이 날의 할 일과 파일.',
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
  navMobile: '모바일',
  mobEyebrow: 'iPhone · Android', mobTitle: '휴대폰에서도<br>같은 노트.',
  mobLede: 'Daynote는 휴대폰에서도 날짜로 움직입니다. 오늘을 열면 이번 주 달력 아래에 그날의 노트와 할 일이 모여 있고, 로그인하면 PC에서 쓰던 노트도 그 자리에 있습니다.',
  mobStoresLabel: '휴대폰용 Daynote 받기',
  mobShotsLabel: '휴대폰 화면',
  ph1: '오늘', ph1Alt: 'Daynote 휴대폰 앱의 오늘 화면. 위에 이번 주 달력, 그 아래에 이날의 노트 카드 두 장과 할 일 목록.',
  ph2: '노트 편집', ph2Alt: '노트 편집 화면. "3분기 계획 회의" 제목 아래에 체크박스 할 일과 메모, 화면 아래에 할 일·날짜·시간 입력 버튼.',
  ph3: '다크 모드', ph3Alt: '어두운 테마로 본 같은 오늘 화면.',
  mp1T: 'iPhone과 Android', mp1B: '두 휴대폰 모두에서 같은 앱, 같은 화면입니다.',
  mp2T: '계정 없이, 오프라인으로', mp2B: '설치하면 바로 씁니다. 로그인하지 않으면 노트는 휴대폰 안에만 있고, 인터넷이 없어도 그대로 동작합니다.',
  mp3T: '로그인하면 PC와 같은 노트', mp3B: 'PC에서 쓰는 Google 계정으로 로그인하면 휴대폰과 PC에 같은 노트와 할 일이 보입니다. iPhone에서는 Apple로도 로그인할 수 있습니다.',
  mp4T: '<code>-[]</code> 한 줄이 할 일로', mp4B: '본문에 <code>-[]</code>로 시작하는 줄을 쓰면 체크박스가 되고, 그날의 할 일 목록에 모입니다.',
  mp5T: '사진과 파일도 그날에', mp5B: '찍은 사진이나 받은 파일을 그 날짜에 붙여 둡니다. 나중에는 날짜만 기억하면 됩니다.',
  mp6T: '라이트와 다크', mp6B: '밝은 테마와 어두운 테마 가운데 눈에 편한 쪽을 고릅니다.',
  tmNote: 'Apple, Apple 로고, App Store는 미국 및 기타 국가에서 등록된 Apple Inc.의 상표입니다. Google Play 및 Google Play 로고는 Google LLC의 상표입니다.',
  privEyebrow: '프라이버시',
  privTitle: '기본은 내 PC.<br>나가는 건 내가 켠 것만.',
  privLede: 'Daynote에는 텔레메트리가 없습니다. 노트와 파일은 내 Windows 계정만 읽을 수 있는 로컬 폴더에 저장되고, 로그인하기 전까지 앱은 인터넷 연결을 열지 않습니다. 내용이 이 PC를 벗어나는 길은 두 가지뿐이고, 둘 다 내가 직접 켭니다. 클라우드 동기화와 AI 연동입니다.',
  privLink: '프라이버시 정책 전문 읽기',
  p1K: '수집', p1V: '사용 통계도, 오류 보고도 없습니다. 동기화를 켜면 Google 계정 ID와 이메일, 그리고 노트가 바뀐 시각이 서비스에 남습니다.',
  p2K: '저장 위치', p2V: '내 PC의 %LocalAppData%\\Daynote. 앱이 따로 암호화하지 않는 일반 파일이라 언제든 직접 백업할 수 있습니다.',
  p3K: '백그라운드', p3V: '클립보드 감시도, 키 입력 기록도, 스크린샷도 없습니다. 앱 안에서 내가 한 동작에만 반응합니다.',
  p4K: '클라우드 사본', p4V: '노트도 파일도 전송 중에, 그리고 서버에 저장될 때 암호화됩니다. 기본 설정에서는 서비스가 열쇠를 함께 보관하므로 종단간 암호화는 아닙니다. 설정의 "노트 잠금"을 켜면 나만 아는 암호로 열쇠를 다시 잠그고 서비스 쪽 사본은 파기됩니다.',
  p5K: '결제', p5V: 'Daynote는 카드 정보를 보지 않습니다. 결제는 판매자 Paddle의 페이지에서 이뤄지고, 서비스에는 구독 상태와 갱신일만 전달됩니다.',
  dlTitle: '오늘부터 시작하세요.',
  dlLede: 'Microsoft Store에서 무료로 설치합니다. 설치 후 첫 실행에서 짧은 튜토리얼이 기능을 안내합니다.',
  dlCta: 'Microsoft Store에서 받기', dlMac: 'Mac용 다운로드', dlMacIntel: 'Intel Mac용은 여기', dlMacNote: 'Apple Silicon용 · Apple 공증 완료',
  s1K: '지원 OS', s1V: 'Windows 11, Windows 10 21H2 LTSC / Enterprise (x64), macOS 12 이상 (Apple Silicon, Intel)',
  s2K: '가격', s2V: '무료',
  s5K: '동기화', s5V: '노트·할 일 동기화는 무료. 이미지·파일 동기화(Pro)는 월 ₩2,900 또는 연 ₩24,000, 14일 무료 체험',
  s3K: '데이터 위치',
  s4K: '현재 버전', s4V: '1.5.0',
  footNavLabel: '바닥글 메뉴',
  syncEyebrow: '클라우드 동기화', syncTitle: '여러 PC에서, 같은 하루.<br>노트는 무료로.',
  syncLede: '회사 PC에서 쓴 노트를 집에서 이어 쓰고 싶다면 Google 계정으로 로그인하세요. 노트, 할 일, 태그가 로그인한 모든 PC에서 실시간으로 같은 상태를 유지하고, 여기까지는 무료입니다. 첨부한 이미지와 파일까지 함께 따라오게 하려면 Pro를 구독하세요. 로그인하지 않으면 Daynote는 지금처럼 완전히 로컬로만 동작합니다.',
  syncNote: 'Pro는 월 ₩2,900 또는 연 ₩24,000이며 14일 무료 체험이 가입 시 한 번 제공됩니다. 동기화는 백업이 아니라 전파입니다. 한 PC에서 지우면 모든 PC에서 지워지니, 백업은 설정의 백업 기능으로 따로 두세요.',
  st1T: 'Google로 로그인', st1B: '비밀번호를 새로 만들지 않습니다. 시스템 브라우저에서 Google에 로그인하면 끝입니다.',
  st2T: '노트는 바로, 무료로', st2B: '로그인하면 노트·할 일·태그가 곧바로 동기화되기 시작하고, 기간 제한이 없습니다. 이미지·파일 동기화는 14일 Pro 체험으로 함께 켜지며 카드 등록은 필요 없습니다. 체험이 끝나면 파일 동기화만 멈춥니다.',
  st3T: '암호화된 사본, 원하면 잠금까지', st3B: '전송과 저장 모두 암호화됩니다. 서비스도 읽을 수 없게 하려면 "노트 잠금"을 켜세요. 나만 아는 암호가 열쇠가 되고, 잊으면 복구 키가 유일한 길입니다.',
  st4T: '언제든 해지', st4B: 'Pro가 끝나면 이미지·파일 동기화가 멈출 뿐입니다. 노트는 계속 동기화되고, 이 PC의 파일도 그대로이며, 이미 올라간 사본도 삭제되지 않습니다.',
};

const en = {
  ...common, ...cal, ...linksEn, storeBadges: storeBadges('en'),
  lang: 'en', ogLocale: 'en_US', home: '/en/', canonical: 'https://daynote.arachat.cc/en/',
  altHref: '/', altLang: 'ko', altLabel: '한국어',
  title: 'Daynote — See your day clearly',
  description: 'Notes, to-dos, and files gathered by date in one focused daily workspace. A Windows notes app that keeps everything on your PC by default, with optional Google sign-in sync across PCs.',
  skip: 'Skip to content', navLabel: 'Main', navFeatures: 'Features', navSync: 'Sync', navPrivacy: 'Privacy', navDownload: 'Download', navGet: 'Get it on Store',
  heroEyebrow: 'Dated notes · Windows · Mac · iPhone · Android',
  heroTitle: 'See your day<br><em>clearly.</em>',
  heroLede: 'Notes, to-dos, and files in one focused daily workspace. Pick a date on the calendar and everything from that day is right there.',
  heroCta: 'Get it from Microsoft Store', heroMac: 'Download for Mac', heroSecondary: 'See how it works',
  heroMeta: 'Free · Windows 11 / 10 (x64) · macOS 12 or later · Note sync free, file sync is Pro',
  heroAlt: 'Daynote main window: search, a sidebar of notes, to-dos, tags and files with a calendar on the left, the note editor in the middle, and the to-dos and files for that day on the right.',
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
  navMobile: 'Mobile',
  mobEyebrow: 'iPhone · Android', mobTitle: 'The same notes,<br>on your phone.',
  mobLede: 'On the phone, Daynote still runs on dates. Open today and that day\'s notes and to-dos sit under a strip of this week. Sign in, and the notes you wrote on your PC are there too.',
  mobStoresLabel: 'Get Daynote for your phone',
  mobShotsLabel: 'Phone screens',
  ph1: 'Today', ph1Alt: 'The Today screen of the Daynote phone app: a strip of this week at the top, then two note cards and the to-do list for the day.',
  ph2: 'Editing a note', ph2Alt: 'The note editor: under the title "Q3 planning meeting", checkbox to-dos and notes, with To-do, Date and Time buttons along the bottom.',
  ph3: 'Dark mode', ph3Alt: 'The same Today screen in the dark theme.',
  mp1T: 'iPhone and Android', mp1B: 'The same app and the same screens on both.',
  mp2T: 'No account, works offline', mp2B: 'Install it and start writing. Until you sign in, your notes stay on the phone, and it all works without a connection.',
  mp3T: 'Sign in, see your PC\'s notes', mp3B: 'Sign in with the Google account you use on your PC and the same notes and to-dos show up on both. On iPhone you can also sign in with Apple.',
  mp4T: 'A <code>-[]</code> line is a to-do', mp4B: 'Start a line with <code>-[]</code> and it becomes a checkbox, gathered into that day\'s to-do list.',
  mp5T: 'Photos and files, on the day', mp5B: 'Attach a photo you took or a file you received to its date. Later, the date is all you need to remember.',
  mp6T: 'Light and dark', mp6B: 'Pick the light theme or the dark one, whichever is easier on your eyes.',
  tmNote: 'Apple, the Apple logo and App Store are trademarks of Apple Inc., registered in the U.S. and other countries. Google Play and the Google Play logo are trademarks of Google LLC.',
  privEyebrow: 'Privacy',
  privTitle: 'Your PC by default.<br>Only what you switch on leaves.',
  privLede: 'Daynote has no telemetry. Notes and files are stored in a local folder only your Windows account can read, and until you sign in the app opens no internet connection. Exactly two things can carry content off this PC, and you switch on both yourself: cloud sync and the AI integration.',
  privLink: 'Read the full privacy policy',
  p1K: 'Collected', p1V: 'No usage statistics, no crash reports. With sync on, the service holds your Google account id and email, and when each note last changed.',
  p2K: 'Stored at', p2V: '%LocalAppData%\\Daynote on your PC. Plain files the app does not encrypt, so you can back them up yourself at any time.',
  p3K: 'Background', p3V: 'No clipboard monitoring, no keystroke logging, no screenshots. It only acts on what you do inside the app.',
  p4K: 'Cloud copy', p4V: 'Notes and files alike are encrypted in transit and at rest. By default the service also holds the key, so this is not end-to-end encryption. Turn on "Lock my notes" in settings and the key is re-sealed with a passphrase only you know, and the service destroys its copy.',
  p5K: 'Payment', p5V: 'Daynote never sees your card. Checkout happens on a page run by Paddle, the merchant of record; the service receives only a subscription status and a renewal date.',
  dlTitle: 'Start today.',
  dlLede: 'Install it free from the Microsoft Store. A short tour on first launch walks you through the features.',
  dlCta: 'Get it from Microsoft Store', dlMac: 'Download for Mac', dlMacIntel: 'Intel Mac? Get it here', dlMacNote: 'For Apple Silicon · notarized by Apple',
  s1K: 'Supported', s1V: 'Windows 11, Windows 10 21H2 LTSC / Enterprise (x64), macOS 12 or later (Apple Silicon, Intel)',
  s2K: 'Price', s2V: 'Free',
  s5K: 'Sync', s5V: 'Notes and to-dos sync free. Image and file sync (Pro) is $2.49 a month or $19.99 a year, 14-day free trial',
  s3K: 'Data location',
  s4K: 'Current version', s4V: '1.5.0',
  footNavLabel: 'Footer',
  syncEyebrow: 'Cloud sync', syncTitle: 'The same day, on every PC.<br>Notes for free.',
  syncLede: 'Want to pick up at home what you wrote at work? Sign in with your Google account and your notes, to-dos, and tags stay identical in real time on every PC you sign in to. That part is free. To have the images and files you attach travel along too, subscribe to Pro. Stay signed out and Daynote stays exactly as local as it is today.',
  syncNote: 'Pro is $2.49 a month or $19.99 a year, with a 14-day free trial granted once at sign-up. Sync propagates, it does not back up: delete on one PC and it is gone on all of them, so keep backups with the backup feature in settings.',
  st1T: 'Sign in with Google', st1B: 'No new password to invent. Sign in to Google in your system browser and you are done.',
  st2T: 'Notes sync right away, free', st2B: 'Once signed in, notes, to-dos, and tags start syncing at once, with no time limit. Image and file sync switches on alongside as a 14-day Pro trial, no card required. When the trial ends, only file sync stops.',
  st3T: 'An encrypted copy, locked if you want', st3B: 'Encrypted in transit and at rest. To make it unreadable to the service too, turn on "Lock my notes": a passphrase only you know becomes the key, and the recovery key is the only way back if you forget it.',
  st4T: 'Cancel any time', st4B: 'When Pro ends, image and file sync stops. Nothing else happens: notes keep syncing, the files on this PC stay, and the copy already uploaded is kept.',
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
  // The account-deletion URL Google Play's Data safety form asks for. The Worker also answers the
  // bare `/delete-account` (src/deleteAccountPage.ts) with one of these two, by Accept-Language.
  { slug: 'delete-account', cls: 'page--legal',
    ko: { title: '계정 삭제', eyebrow: '계정', desc: 'Daynote 계정을 삭제하는 방법과, 삭제되는 것과 남는 것.', meta: '즉시 · 영구 삭제' },
    en: { title: 'Delete your account', eyebrow: 'Account', desc: 'How to delete a Daynote account, and what is and is not deleted.', meta: 'Immediate and permanent' } },
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
