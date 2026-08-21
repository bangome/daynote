namespace Daynote.App.Localization;

/// <summary>
/// The Korean string catalog. Korean is the product's original language, so this file holds the
/// canonical copy that <see cref="EnglishStrings"/> mirrors key-for-key. Keys match the member
/// names on <see cref="AppStrings"/>; a parity test fails the build if the two catalogs or the
/// accessor class ever drift apart. Format strings use <c>{0}</c> placeholders.
/// </summary>
internal static class KoreanStrings
{
    internal static IReadOnlyDictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Command region
        ["SearchHint"] = "노트 검색",
        ["SearchAutomation"] = "노트 검색",
        ["Settings"] = "설정",
        ["OpenSettings"] = "설정 열기",

        // Capture state (command-row chip, tray, settings)

        // Sidebar
        ["Today"] = "오늘",
        ["GoToToday"] = "오늘로 이동",
        ["AddNote"] = "노트 추가",
        ["PreviousMonth"] = "이전 달",
        ["NextMonth"] = "다음 달",
        ["CalendarMonth"] = "달력 월",
        ["SidebarNoteList"] = "사이드바 노트 목록",
        ["MiniCalendar"] = "미니 월 달력",
        ["MoveNoteUpFormat"] = "{0} 위로 이동",
        ["MoveNoteDownFormat"] = "{0} 아래로 이동",

        // Note region
        ["DateHeadingAutomation"] = "선택한 날짜 제목",
        ["OrderedNoteTabs"] = "노트 탭 목록",
        ["MarkdownEditor"] = "마크다운 편집기",
        ["CloseNoteFormat"] = "{0} 닫기",
        ["EditorEmptyHint"] = "오늘의 첫 기록을 시작하세요",

        // Editor toolbar
        ["EditorToolbar"] = "편집기 도구 모음",
        ["SaveStatusAutomation"] = "저장 상태",
        ["FormattingCommands"] = "마크다운 서식 명령",
        ["Bold"] = "굵게",
        ["Italic"] = "기울임",
        ["BulletedList"] = "글머리 기호 목록",
        ["NumberedList"] = "번호 매기기 목록",
        ["InlineCode"] = "인라인 코드",

        // Save status text
        ["SaveDirty"] = "저장 안 됨",
        ["SaveSaving"] = "저장 중",
        ["SaveSaved"] = "저장됨",
        ["SaveFailed"] = "저장 실패",
        ["Retry"] = "다시 시도",
        ["RetrySave"] = "저장 다시 시도",

        // Clipboard drawer
        ["Delete"] = "삭제",

        // Search
        ["SearchResultsOverlay"] = "검색 결과 오버레이",
        ["SearchSummary"] = "검색 요약",
        ["SearchQuery"] = "검색어",
        ["SearchResultCount"] = "검색 결과 수",
        ["SearchScope"] = "검색 범위",
        ["SearchScopeValue"] = "노트 및 파일",
        ["SearchLoadingAutomation"] = "검색 중",
        ["SearchLoading"] = "검색 중…",
        ["SearchEmptyAutomation"] = "검색 결과 없음",
        ["SearchEmpty"] = "검색어와 일치하는 노트가 없습니다.",
        ["SearchErrorAutomation"] = "검색 오류",
        ["SearchError"] = "검색을 사용할 수 없습니다. 저장소를 사용할 수 있을 때 다시 시도하세요.",
        ["SearchResults"] = "검색 결과",
        ["SearchResultSnippet"] = "결과 미리보기",
        ["ResultDate"] = "결과 날짜",
        ["NoteSource"] = "노트 항목",
        ["ClipboardSourceLabel"] = "파일 항목",
        ["SearchKindNote"] = "노트",
        ["SearchKindClipboard"] = "파일",
        ["StaleSearchResult"] = "이 결과는 더 이상 사용할 수 없습니다. 검색 결과를 새로 고쳤습니다.",
        ["StaleSearchResultAutomation"] = "오래된 검색 결과",
        ["SearchNoResults"] = "결과 없음",
        ["SearchUnavailableShort"] = "검색 사용 불가",
        ["SearchResultsShownFormat"] = "결과 {0}개 표시됨",
        ["SearchResultsCountFormat"] = "결과 {0}개",

        // Consent

        // Settings
        ["SettingsTitle"] = "설정",
        ["CloseSettings"] = "설정 닫기",
        ["SettingsStartupRow"] = "Windows 시작 시 실행",
        ["SettingsStartupLabel"] = "로그인할 때 Daynote 실행",
        ["SettingsStartupAutomation"] = "로그인 시 Daynote 실행",
        ["SettingsStorageRow"] = "저장 위치",
        ["SettingsStorageLabel"] = "저장 위치",
        ["SettingsPrivacyRow"] = "개인정보",
        ["SettingsPrivacyLabel"] = "개인정보",
        ["SettingsStartupEnabledText"] = "로그인하면 Daynote가 자동으로 실행됩니다.",
        ["SettingsStartupDisabledText"] = "Daynote가 자동으로 실행되지 않습니다.",
        ["SettingsStartupEnabledByPolicyText"] = "관리자 정책으로 설정되어 여기서 변경할 수 없습니다.",
        ["SettingsStartupDisabledByPolicyText"] = "관리자 정책으로 사용 안 함으로 설정되어 여기서 변경할 수 없습니다.",
        ["SettingsStartupDisabledByUserText"] = "Windows 시작 프로그램 설정에서 꺼져 있습니다. 그곳에서 다시 켜세요.",
        ["SettingsStartupUnavailableText"] = "이 기기에서는 시작 프로그램 설정을 사용할 수 없습니다.",
        ["SettingsPrivacyText"] =
            "노트는 이 PC의 평문 SQLite 데이터베이스에, 첨부 파일은 로컬 파일로 저장됩니다. " +
            "Daynote 자체에는 분석 도구나 원격 측정이 없습니다.",
        ["SettingsPrivacyMcp"] =
            "AI 연동(MCP)을 등록하면 연결한 AI 클라이언트가 노트를 읽고 쓸 수 있습니다. " +
            "그 클라이언트가 노트 내용을 자체 서비스로 전송할 수 있습니다.",
        ["SettingsPrivacySync"] =
            "클라우드 동기화는 기본적으로 꺼져 있습니다. 로그인하면 노트가 이 PC에서 암호화된 뒤 업로드됩니다.",

        // AI integration / MCP (settings)
        ["SettingsMcpRow"] = "AI 연동",
        ["SettingsMcpLabel"] = "AI 연동 (MCP)",
        ["SettingsMcpDesc"] =
            "Claude 같은 AI 도구가 Daynote 노트를 직접 읽고 쓸 수 있습니다. MCP 서버는 Daynote에 함께 설치되어 " +
            "있으므로 따로 빌드하거나 내려받을 필요 없이 버튼 한 번으로 등록됩니다.",
        ["SettingsMcpRegister"] = "Claude Desktop에 등록",
        ["SettingsMcpRegistered"] = "등록했습니다. Claude Desktop을 재시작하면 daynote 도구가 나타납니다.",
        ["SettingsMcpAlreadyRegistered"] = "이미 등록되어 있습니다.",
        ["SettingsMcpFailedFormat"] = "등록하지 못했습니다. 설정 파일을 직접 확인해 주세요: {0}",
        ["SettingsMcpUnavailable"] =
            "이 실행 환경에는 등록할 MCP 서버가 없습니다. Microsoft Store에서 설치한 Daynote에서 쓸 수 있는 기능입니다.",
        ["SettingsMcpCodeHint"] = "Claude Code에서는 터미널에 아래 한 줄을 실행하세요.",
        ["SettingsMcpFooter"] =
            "제공 도구: 노트 검색 · 날짜별 조회 · 최근 목록 · 생성 · 수정 · 삭제. 자세한 내용은 docs/MCP.md 를 참고하세요.",
        ["SettingsMcpCopy"] = "복사",
        ["SettingsMcpCopied"] = "복사됨!",

        // Shortcuts (settings)
        ["SettingsShortcutsRow"] = "단축키",
        ["SettingsSummonHotkeyLabel"] = "전역 소환 단축키",
        ["SettingsSummonHotkeyDesc"] =
            "Daynote가 트레이에 숨어 있어도 이 조합을 누르면 어디서든 창이 바로 열립니다.",
        ["HotkeyChange"] = "변경",
        ["HotkeyCapturing"] = "키 조합을 누르세요…",
        ["HotkeyConflict"] = "이 조합은 다른 앱이 사용 중입니다. 다른 조합을 선택하세요.",
        ["HotkeyInvalid"] = "수정자(Ctrl/Alt/Shift/Win)와 일반 키를 함께 눌러야 합니다.",
        ["HotkeyReset"] = "기본값",
        ["SettingsInAppShortcutsLabel"] = "앱 내 단축키",

        // File links (body paste/drop)
        ["PastedImagePrefix"] = "이미지",

        // Backup / restore (settings)
        ["SettingsBackupRow"] = "백업 및 복원",
        ["SettingsBackupLabel"] = "데이터 백업 및 복원",
        ["SettingsBackupDesc"] =
            "모든 노트·첨부 파일·설정을 하나의 zip 파일로 백업하거나, 백업 파일에서 복원합니다.",
        ["BackupButton"] = "백업",
        ["RestoreButton"] = "복원",
        ["BackupInProgress"] = "백업하는 중…",
        ["BackupSucceeded"] = "백업이 완료되었습니다.",
        ["BackupFailed"] = "백업에 실패했습니다. 대상 위치를 확인하세요.",
        ["BackupFlushBlocked"] = "저장하지 못한 변경이 있어 백업을 진행할 수 없습니다. 먼저 저장 문제를 해결하세요.",
        ["RestoreStagedRestarting"] =
            "복원을 준비했습니다. 앱을 다시 시작하면 적용됩니다. (현재 데이터는 pre-restore-backup 폴더에 보관됩니다.)",
        ["RestoreIncompatible"] = "이 백업은 더 최신 버전에서 만들어져 복원할 수 없습니다.",
        ["RestoreInvalid"] = "올바른 Daynote 백업 파일이 아닙니다.",
        ["RestoreFailed"] = "복원 준비에 실패했습니다.",

        // In-app shortcut action labels
        ["ShortcutNewNote"] = "새 노트",
        ["ShortcutGoToday"] = "오늘로 이동",
        ["ShortcutSettings"] = "설정 열기/닫기",
        ["ShortcutToggleTheme"] = "테마 전환",
        ["ShortcutToggleLeft"] = "왼쪽 패널 접기/펴기",
        ["ShortcutToggleRight"] = "오른쪽 패널 접기/펴기",
        ["ShortcutOpenSticky"] = "포스트잇으로 띄우기",
        ["ShortcutQuickSticky"] = "오늘 새 포스트잇 (전역)",

        // Onboarding tutorial
        ["TutorialBack"] = "이전",
        ["TutorialNext"] = "다음",
        ["TutorialSkip"] = "건너뛰기",
        ["TutorialFinish"] = "시작하기",
        ["TutorialProgressFormat"] = "{0} / {1}",
        ["TutorialWelcomeTitle"] = "Daynote에 오신 걸 환영합니다",
        ["TutorialWelcomeBody"] = "날짜별로 노트를 쓰고 정리하는 데스크 앱입니다. 잠깐만 둘러볼게요.",
        ["TutorialNotesTitle"] = "노트와 캘린더",
        ["TutorialNotesBody"] = "왼쪽 캘린더에서 날짜를 고르고 가운데에서 노트를 씁니다. + 버튼으로 새 노트를 추가하며, 입력하는 동안 자동으로 저장됩니다.",
        ["TutorialTodoTitle"] = "할 일 체크박스",
        ["TutorialTodoBody"] = "본문에 -[] 를 적으면 체크박스가 되고, (7/25 14:00) 처럼 마감을 붙일 수 있습니다. 오른쪽 '할 일' 탭에 날짜별로 모여요.",
        ["TutorialFilesTitle"] = "파일 첨부와 본문 링크",
        ["TutorialFilesBody"] = "파일 탭에 파일을 끌어다 놓거나, 본문에 이미지·파일을 붙여넣으면 파일로 저장되고 본문에는 링크만 남습니다. 링크를 누르면 파일 탭에서 열립니다.",
        ["TutorialStickyTitle"] = "포스트잇",
        ["TutorialStickyBody"] = "노트를 포스트잇 창으로 띄워 항상 위에 고정할 수 있습니다. 포스트잇과 본문은 실시간으로 함께 편집됩니다.",
        ["TutorialSearchTitle"] = "통합 검색",
        ["TutorialSearchBody"] = "제목·본문·파일을 한 번에 검색합니다. 결과에는 키워드가 나온 부분이 강조되어 보입니다.",
        ["TutorialShortcutsTitle"] = "단축키",
        ["TutorialShortcutsBody"] = "자주 쓰는 동작은 단축키로 빠르게. 전역 단축키는 앱이 트레이에 있어도 동작하고, 앱 내 단축키는 설정에서 바꿀 수 있습니다.",
        ["TutorialWrapTitle"] = "백업과 설정",
        ["TutorialWrapBody"] = "설정은 타이틀바의 톱니 버튼, 트레이 메뉴, 또는 Ctrl+, 로 엽니다. 설정에서 데이터 백업·복원, 단축키 변경, 시작 옵션을 관리하세요.",
        ["TutorialClosingTitle"] = "이제 시작해볼까요",
        ["TutorialClosingBody"] =
            "오늘 날짜에 예시 노트를 하나 넣어두었어요. 자유롭게 고치거나 지우면서 익혀 보세요. " +
            "이 안내는 설정 → 도움말에서 언제든 다시 볼 수 있습니다. Daynote를 잘 부탁드립니다!",

        // Sample note seeded on first run (dated to-dos use today's month/day via {0}/{1}).
        ["SampleNoteTitle"] = "오늘 업무 메모 (예시)",
        ["SampleNoteBodyFormat"] =
            "Daynote를 이렇게 활용해 보세요. 이 노트는 예시이며 지워도 됩니다.\n\n" +
            "-[] 오전 팀 스탠드업 정리 ({0}/{1} 10:00)\n" +
            "-[x] 지난주 주간보고 검토\n" +
            "-[] 거래처 회신 메일 보내기 ({0}/{1} 15:00)\n\n" +
            "[회의 메모]\n" +
            "- 신규 기능 릴리즈 일정 공유\n" +
            "- QA 이슈 3건 우선순위 재조정\n\n" +
            "팁: 파일을 본문에 붙여넣으면 파일 탭에 저장되고 링크가 남습니다. 상단 검색으로 모든 노트를 한 번에 찾을 수 있어요.",
        ["TutorialShortcutSummon"] = "어디서든 Daynote 불러오기 (전역)",
        ["TutorialShortcutQuickSticky"] = "오늘 새 포스트잇 (전역)",
        ["SettingsTutorialRow"] = "도움말",
        ["SettingsTutorialLabel"] = "사용법 튜토리얼",
        ["SettingsTutorialButton"] = "튜토리얼 다시 보기",

        // About / author (settings)
        ["SettingsAboutRow"] = "제작자",
        ["SettingsAboutLabel"] = "제작자",
        ["AuthorName"] = "Bread Jinhwa Jeong",
        ["AuthorEmail"] = "aracube@gmail.com",

        // Tray menu
        ["TrayAppName"] = "Daynote",
        ["TrayShow"] = "Daynote 보기",
        ["TraySettings"] = "설정",
        ["TrayQuit"] = "종료",

        // Calendar automation suffix
        ["TodaySuffix"] = ", 오늘",

        // ── Redesign (calendar-notes.dc.html, DESIGN.md Revision 2026-07-21) ──
        ["AppTitle"] = "달력 노트",
        ["UnifiedSearchHint"] = "통합검색 — 노트, 파일, 날짜",
        ["ClearSearch"] = "검색어 지우기",
        ["ThemeToggle"] = "테마 전환",
        ["SidebarsExpand"] = "사이드바 펼치기",
        ["SidebarsCollapse"] = "사이드바 접기",
        ["TabTodoName"] = "할 일 탭",
        ["TabFilesName"] = "파일 탭",
        ["WindowMinimize"] = "최소화",
        ["WindowMaximize"] = "최대화",
        ["WindowRestore"] = "이전 크기로",
        ["WindowClose"] = "닫기",

        // Timeline view mode
        ["TimelineToggle"] = "타임라인 보기",
        ["TimelineExpand"] = "펼치기",
        ["TimelineCollapse"] = "접기",
        ["TimelineEmpty"] = "표시할 노트가 없습니다",

        // Note list
        ["NewNote"] = "새 노트",
        ["NoteCountFormat"] = "노트 {0}개",
        ["DayNotesEmpty"] = "이 날짜에 노트가 없습니다",

        // Editor
        ["TitlePlaceholder"] = "제목",
        ["Favorite"] = "즐겨찾기",
        ["OpenStickyNote"] = "포스트잇으로 열기",
        ["StickyNoteWindow"] = "포스트잇",
        ["PinStickyNote"] = "화면 위에 고정",
        ["UnpinStickyNote"] = "화면 위 고정 해제",
        ["CloseStickyNote"] = "포스트잇 닫기",
        ["StickyNoteBody"] = "포스트잇 본문",
        ["DeleteNoteTip"] = "노트 삭제",
        ["TagAddPlaceholder"] = "+ 태그",
        ["NoNoteSelected"] = "왼쪽 목록에서 노트를 선택하거나 새 노트를 만드세요",
        ["NoteMetaFormat"] = "{0}자 · {1}줄",
        ["NoteUpdatedFormat"] = "수정: {0}",
        ["EditorBodyPlaceholder"] = "메모를 입력하세요.  '-[] 할 일 (07/25 14:00)' 형식으로 쓰면 Todo 패널에 자동 등록됩니다.",

        // Right panel tabs
        ["TabTodo"] = "할 일",
        ["TabTodoFormat"] = "할 일 ({0})",
        ["TabTags"] = "태그",
        ["TabTagsFormat"] = "태그 ({0})",
        ["TabTagsName"] = "태그 탭",
        ["TabFiles"] = "파일",

        // Tags tab
        ["TagPanelEmpty"] = "본문에 #태그를 입력하면 여기 모입니다.",

        // Todo tab
        ["TodoEmptyText"] = "(내용 없음)",
        ["TodoEmptyPrefix"] = "노트에 ",
        ["TodoEmptyCode"] = "-[] 할 일",
        ["TodoEmptySuffix"] = " 을 입력하면 여기에 자동으로 표시됩니다",

        // Clipboard tab

        // Files tab
        ["AddFile"] = "파일 · 이미지 추가",
        ["FileTabEmpty"] = "이 날짜에 보관된 파일이 없습니다",

        // Search dropdown result kinds
        ["SearchKindDate"] = "날짜",
        ["SearchKindFile"] = "파일",
        ["SearchNoResultsRow"] = "검색 결과가 없습니다",
        ["SearchDateNoteCountFormat"] = "노트 {0}개",

        // Language (settings)
        ["SettingsLanguageRow"] = "언어",
        ["SettingsLanguageLabel"] = "표시 언어",
        ["SettingsLanguageDesc"] = "고른 언어가 앱 전체에 바로 적용됩니다. 다시 시작하지 않아도 됩니다.",
        ["LanguageKorean"] = "한국어",
        ["LanguageEnglish"] = "English",

        // Date patterns. These are .NET custom format strings, not prose: literal text inside a
        // pattern must stay single-quoted, and the field letters (yyyy/M/d/ddd) must survive
        // translation or the date renders wrong.
        ["DateFormatLong"] = "yyyy'년' M'월' d'일' dddd",
        ["DateFormatMonth"] = "yyyy'년' M'월'",
        ["DateFormatDayHeading"] = "M'월' d'일' (ddd)",

        // Mini-calendar weekday header, Sunday first
        ["WeekdaySun"] = "일",
        ["WeekdayMon"] = "월",
        ["WeekdayTue"] = "화",
        ["WeekdayWed"] = "수",
        ["WeekdayThu"] = "목",
        ["WeekdayFri"] = "금",
        ["WeekdaySat"] = "토",

        // Compact shell (narrow-window layout)
        ["CommandRegion"] = "명령 영역",
        ["StatusRegion"] = "상태 영역",
        ["CompactWorkspaceSwitch"] = "컴팩트 작업 영역 전환",
        ["CompactNavigate"] = "탐색",
        ["CompactNavigateAutomation"] = "탐색 보기",
        ["CompactNotes"] = "노트",
        ["CompactNotesAutomation"] = "노트 보기",
        ["CalendarWeekdayHeader"] = "달력 요일 머리글",
        ["CalendarDays"] = "달력 날짜",

        // Default display title for a note the user never named ({0} = note number)
        ["UntitledNoteFormat"] = "노트 {0}",

        // Cloud sync: account section and the command-row status chip
        ["CloudSyncTitle"] = "클라우드 동기화",
        ["CloudSyncBlurb"] = "선택 기능입니다. 로그인하면 노트가 이 PC에서 암호화된 뒤 업로드되어 다른 기기와 동기화됩니다. 저장된 내용은 서버에서 열어볼 수 없습니다.",
        ["CloudSyncPrivacyNote"] = "서버에는 암호문과 함께 이메일 주소, 각 노트의 수정 시각, 노트 개수가 남습니다. 제목·본문·태그·날짜는 남지 않습니다.",
        ["CloudSyncLocalNote"] = "이 PC의 데이터베이스는 계속 평문입니다. 동기화는 백업을 대신하지 않습니다.",
        ["AccountEmail"] = "이메일",
        ["AccountPassword"] = "비밀번호",
        ["AccountSignIn"] = "로그인",
        ["AccountCreate"] = "계정 만들기",
        ["AccountSignOut"] = "로그아웃",
        ["AccountSyncNow"] = "지금 동기화",
        ["AccountBusy"] = "처리 중…",
        // {0} = the signed-in email address.
        ["AccountSignedInAsFormat"] = "{0} 계정으로 로그인됨",
        // {0} = a formatted local time.
        ["AccountLastSyncFormat"] = "마지막 동기화: {0}",
        ["AccountNeverSynced"] = "아직 동기화하지 않았습니다",
        // {0} = the minimum password length.
        ["AccountPasswordHint"] = "비밀번호는 {0}자 이상이어야 합니다. 비밀번호를 잊으면 복구 키가 유일한 방법입니다.",
        ["RecoveryKeyTitle"] = "복구 키",
        ["RecoveryKeyBlurb"] = "이 키를 안전한 곳에 적어 두세요. 비밀번호를 잊었을 때 클라우드에 있는 노트를 되찾을 수 있는 유일한 방법이며, 다시 보여줄 수 없습니다.",
        ["RecoveryKeyCopy"] = "복사",
        ["RecoveryKeyCopied"] = "복사했습니다",
        ["RecoveryKeySaveToFile"] = "파일로 저장",
        ["RecoveryKeyConfirm"] = "복구 키를 안전한 곳에 저장했습니다",
        ["RecoveryKeyDone"] = "완료",
        ["RecoveryKeyFileFilter"] = "텍스트 파일 (*.txt)|*.txt",
        ["AccountLockedTitle"] = "노트가 잠겨 있습니다",
        ["AccountLockedBlurb"] = "비밀번호가 재설정되어 이 기기에서는 클라우드 노트를 열 수 없습니다. 복구 키를 입력하거나, 이전에 사용한 기기에서 로그인하세요. 이 PC에 있는 노트는 그대로입니다.",
        // {0} = number of notes replaced by a newer version from another device.
        ["AccountConflictsFormat"] = "노트 {0}개가 다른 기기의 최신 버전으로 교체되었습니다. 이전 내용은 사본으로 남겨 두었습니다.",
        ["AccountOpenConflicts"] = "사본 폴더 열기",
        ["SyncChipSynced"] = "동기화됨",
        ["SyncChipSyncing"] = "동기화 중",
        ["SyncChipPending"] = "대기 중",
        ["SyncChipOffline"] = "오프라인",
        ["SyncChipLocked"] = "잠김",
        ["SyncChipError"] = "동기화 오류",
        ["SyncStatusAutomation"] = "클라우드 동기화 상태",
        ["AccountErrorInvalidCredentials"] = "이메일 주소 또는 비밀번호가 올바르지 않습니다.",
        ["AccountErrorEmailTaken"] = "이미 등록된 이메일 주소입니다.",
        ["AccountErrorInvalidEmail"] = "이메일 주소 형식이 올바르지 않습니다.",
        // {0} = the minimum password length.
        ["AccountErrorWeakPassword"] = "비밀번호는 {0}자 이상이어야 합니다.",
        ["AccountErrorRewrapRequired"] = "복구 키가 필요합니다.",
        ["AccountErrorUnsupportedVersion"] = "이 계정은 더 새로운 버전의 Daynote에서 만들어졌습니다. 앱을 업데이트하세요.",
        ["AccountErrorOffline"] = "동기화 서비스에 연결할 수 없습니다. 연결을 확인하고 다시 시도하세요.",
        ["AccountErrorServer"] = "동기화 서비스에서 오류가 발생했습니다. 잠시 후 다시 시도하세요.",

        // Cloud sync: password reset and unlock
        ["AccountForgotPassword"] = "비밀번호를 잊으셨나요?",
        ["AccountResetSend"] = "재설정 코드 받기",
        // {0} = the email address the code was sent to.
        ["AccountResetSentFormat"] = "{0} 으로 코드를 보냈습니다. 등록된 주소가 아니면 메일은 오지 않습니다.",
        ["AccountResetCode"] = "재설정 코드",
        ["AccountResetNewPassword"] = "새 비밀번호",
        ["AccountResetConfirm"] = "비밀번호 변경",
        ["AccountResetCancel"] = "취소",
        ["AccountResetWarning"] = "비밀번호를 바꾸면 계정에는 다시 들어올 수 있지만, 클라우드에 있는 노트는 복구 키가 있어야 열립니다. 이 PC에서 재설정하는 경우에는 자동으로 열립니다.",
        ["AccountUnlockWithRecoveryKey"] = "복구 키로 잠금 해제",
        ["AccountUnlockKeyLabel"] = "복구 키",
        ["AccountUnlockConfirm"] = "잠금 해제",
        ["AccountUnlockPasswordLabel"] = "현재 비밀번호",
        ["AccountDiscardCloudCopy"] = "클라우드 사본 폐기",
        ["AccountDiscardCloudCopyBlurb"] = "복구 키도 이전 기기도 없다면, 클라우드 사본을 버리고 이 PC의 노트로 다시 시작할 수 있습니다. 이 PC의 노트는 그대로입니다.",
        ["AccountErrorInvalidResetCode"] = "재설정 코드가 올바르지 않습니다. 새 코드를 요청하세요.",
        ["AccountErrorInvalidRecoveryKeyEntered"] = "이 계정의 복구 키가 아닙니다.",
        ["AccountErrorNoWayToUnlock"] = "이 기기에서는 클라우드 사본을 열 수 없습니다. 이전에 사용한 기기에서 로그인하거나, 클라우드 사본을 폐기하세요.",

        // File-dialog filter (pipe-delimited Win32 syntax; only the label is translated)
        ["BackupZipFilter"] = "Daynote 백업 (*.zip)|*.zip",
    };
}
