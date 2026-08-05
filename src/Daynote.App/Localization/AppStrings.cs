namespace Daynote.App.Localization;

/// <summary>
/// Strongly typed access to user-visible product copy and UI Automation names. Members resolve
/// through <see cref="LocalizationService"/> at call time, so they follow the language the user
/// picked in settings rather than baking in one language at compile time. XAML binds the same keys
/// live through <see cref="TrExtension"/> (<c>{loc:Tr Key}</c>); view models read these members and
/// re-raise change notifications via <see cref="ILanguageAware"/> so visible text and accessible
/// names stay in sync. Format strings use <c>{0}</c> placeholders and are applied with the current
/// culture.
/// </summary>
public static class AppStrings
{
    // Command region
    public static string SearchHint => LocalizationService.Instance[nameof(SearchHint)];
    public static string SearchAutomation => LocalizationService.Instance[nameof(SearchAutomation)];
    public static string Settings => LocalizationService.Instance[nameof(Settings)];
    public static string OpenSettings => LocalizationService.Instance[nameof(OpenSettings)];

    // Capture state (command-row chip, tray, settings)

    // Sidebar
    public static string Today => LocalizationService.Instance[nameof(Today)];
    public static string GoToToday => LocalizationService.Instance[nameof(GoToToday)];
    public static string AddNote => LocalizationService.Instance[nameof(AddNote)];
    public static string PreviousMonth => LocalizationService.Instance[nameof(PreviousMonth)];
    public static string NextMonth => LocalizationService.Instance[nameof(NextMonth)];
    public static string CalendarMonth => LocalizationService.Instance[nameof(CalendarMonth)];
    public static string SidebarNoteList => LocalizationService.Instance[nameof(SidebarNoteList)];
    public static string MiniCalendar => LocalizationService.Instance[nameof(MiniCalendar)];
    /// <summary>{0} = note title.</summary>
    public static string MoveNoteUpFormat => LocalizationService.Instance[nameof(MoveNoteUpFormat)];
    /// <summary>{0} = note title.</summary>
    public static string MoveNoteDownFormat => LocalizationService.Instance[nameof(MoveNoteDownFormat)];

    // Note region
    public static string DateHeadingAutomation => LocalizationService.Instance[nameof(DateHeadingAutomation)];
    public static string OrderedNoteTabs => LocalizationService.Instance[nameof(OrderedNoteTabs)];
    public static string MarkdownEditor => LocalizationService.Instance[nameof(MarkdownEditor)];
    /// <summary>{0} = note title.</summary>
    public static string CloseNoteFormat => LocalizationService.Instance[nameof(CloseNoteFormat)];
    public static string EditorEmptyHint => LocalizationService.Instance[nameof(EditorEmptyHint)];

    // Editor toolbar
    public static string EditorToolbar => LocalizationService.Instance[nameof(EditorToolbar)];
    public static string SaveStatusAutomation => LocalizationService.Instance[nameof(SaveStatusAutomation)];
    public static string FormattingCommands => LocalizationService.Instance[nameof(FormattingCommands)];
    public static string Bold => LocalizationService.Instance[nameof(Bold)];
    public static string Italic => LocalizationService.Instance[nameof(Italic)];
    public static string BulletedList => LocalizationService.Instance[nameof(BulletedList)];
    public static string NumberedList => LocalizationService.Instance[nameof(NumberedList)];
    public static string InlineCode => LocalizationService.Instance[nameof(InlineCode)];

    // Save status text
    public static string SaveDirty => LocalizationService.Instance[nameof(SaveDirty)];
    public static string SaveSaving => LocalizationService.Instance[nameof(SaveSaving)];
    public static string SaveSaved => LocalizationService.Instance[nameof(SaveSaved)];
    public static string SaveFailed => LocalizationService.Instance[nameof(SaveFailed)];
    public static string Retry => LocalizationService.Instance[nameof(Retry)];
    public static string RetrySave => LocalizationService.Instance[nameof(RetrySave)];

    // Clipboard drawer
    public static string Delete => LocalizationService.Instance[nameof(Delete)];

    // Search
    public static string SearchResultsOverlay => LocalizationService.Instance[nameof(SearchResultsOverlay)];
    public static string SearchSummary => LocalizationService.Instance[nameof(SearchSummary)];
    public static string SearchQuery => LocalizationService.Instance[nameof(SearchQuery)];
    public static string SearchResultCount => LocalizationService.Instance[nameof(SearchResultCount)];
    public static string SearchScope => LocalizationService.Instance[nameof(SearchScope)];
    public static string SearchScopeValue => LocalizationService.Instance[nameof(SearchScopeValue)];
    public static string SearchLoadingAutomation => LocalizationService.Instance[nameof(SearchLoadingAutomation)];
    public static string SearchLoading => LocalizationService.Instance[nameof(SearchLoading)];
    public static string SearchEmptyAutomation => LocalizationService.Instance[nameof(SearchEmptyAutomation)];
    public static string SearchEmpty => LocalizationService.Instance[nameof(SearchEmpty)];
    public static string SearchErrorAutomation => LocalizationService.Instance[nameof(SearchErrorAutomation)];
    public static string SearchError => LocalizationService.Instance[nameof(SearchError)];
    public static string SearchResults => LocalizationService.Instance[nameof(SearchResults)];
    public static string SearchResultSnippet => LocalizationService.Instance[nameof(SearchResultSnippet)];
    public static string ResultDate => LocalizationService.Instance[nameof(ResultDate)];
    public static string NoteSource => LocalizationService.Instance[nameof(NoteSource)];
    public static string ClipboardSourceLabel => LocalizationService.Instance[nameof(ClipboardSourceLabel)];
    public static string SearchKindNote => LocalizationService.Instance[nameof(SearchKindNote)];
    public static string SearchKindClipboard => LocalizationService.Instance[nameof(SearchKindClipboard)];
    public static string StaleSearchResult => LocalizationService.Instance[nameof(StaleSearchResult)];
    public static string StaleSearchResultAutomation => LocalizationService.Instance[nameof(StaleSearchResultAutomation)];
    public static string SearchNoResults => LocalizationService.Instance[nameof(SearchNoResults)];
    public static string SearchUnavailableShort => LocalizationService.Instance[nameof(SearchUnavailableShort)];
    /// <summary>{0} = result count.</summary>
    public static string SearchResultsShownFormat => LocalizationService.Instance[nameof(SearchResultsShownFormat)];
    /// <summary>{0} = result count.</summary>
    public static string SearchResultsCountFormat => LocalizationService.Instance[nameof(SearchResultsCountFormat)];

    // Consent

    // Settings
    public static string SettingsTitle => LocalizationService.Instance[nameof(SettingsTitle)];
    public static string CloseSettings => LocalizationService.Instance[nameof(CloseSettings)];
    public static string SettingsStartupRow => LocalizationService.Instance[nameof(SettingsStartupRow)];
    public static string SettingsStartupLabel => LocalizationService.Instance[nameof(SettingsStartupLabel)];
    public static string SettingsStartupAutomation => LocalizationService.Instance[nameof(SettingsStartupAutomation)];
    public static string SettingsStorageRow => LocalizationService.Instance[nameof(SettingsStorageRow)];
    public static string SettingsStorageLabel => LocalizationService.Instance[nameof(SettingsStorageLabel)];
    public static string SettingsPrivacyRow => LocalizationService.Instance[nameof(SettingsPrivacyRow)];
    public static string SettingsPrivacyLabel => LocalizationService.Instance[nameof(SettingsPrivacyLabel)];
    public static string SettingsStartupEnabledText => LocalizationService.Instance[nameof(SettingsStartupEnabledText)];
    public static string SettingsStartupDisabledText => LocalizationService.Instance[nameof(SettingsStartupDisabledText)];
    public static string SettingsStartupEnabledByPolicyText => LocalizationService.Instance[nameof(SettingsStartupEnabledByPolicyText)];
    public static string SettingsStartupDisabledByPolicyText => LocalizationService.Instance[nameof(SettingsStartupDisabledByPolicyText)];
    public static string SettingsStartupDisabledByUserText => LocalizationService.Instance[nameof(SettingsStartupDisabledByUserText)];
    public static string SettingsStartupUnavailableText => LocalizationService.Instance[nameof(SettingsStartupUnavailableText)];
    public static string SettingsPrivacyText => LocalizationService.Instance[nameof(SettingsPrivacyText)];

    // AI integration / MCP (settings)
    public static string SettingsMcpRow => LocalizationService.Instance[nameof(SettingsMcpRow)];
    public static string SettingsMcpLabel => LocalizationService.Instance[nameof(SettingsMcpLabel)];
    public static string SettingsMcpDesc => LocalizationService.Instance[nameof(SettingsMcpDesc)];
    public static string SettingsMcpStep1 => LocalizationService.Instance[nameof(SettingsMcpStep1)];
    public static string SettingsMcpStep2 => LocalizationService.Instance[nameof(SettingsMcpStep2)];
    public static string SettingsMcpStep3 => LocalizationService.Instance[nameof(SettingsMcpStep3)];
    public static string SettingsMcpFooter => LocalizationService.Instance[nameof(SettingsMcpFooter)];
    public static string SettingsMcpCopy => LocalizationService.Instance[nameof(SettingsMcpCopy)];
    public static string SettingsMcpCopied => LocalizationService.Instance[nameof(SettingsMcpCopied)];

    // Shortcuts (settings)
    public static string SettingsShortcutsRow => LocalizationService.Instance[nameof(SettingsShortcutsRow)];
    public static string SettingsSummonHotkeyLabel => LocalizationService.Instance[nameof(SettingsSummonHotkeyLabel)];
    public static string SettingsSummonHotkeyDesc => LocalizationService.Instance[nameof(SettingsSummonHotkeyDesc)];
    public static string HotkeyChange => LocalizationService.Instance[nameof(HotkeyChange)];
    public static string HotkeyCapturing => LocalizationService.Instance[nameof(HotkeyCapturing)];
    public static string HotkeyConflict => LocalizationService.Instance[nameof(HotkeyConflict)];
    public static string HotkeyInvalid => LocalizationService.Instance[nameof(HotkeyInvalid)];
    public static string HotkeyReset => LocalizationService.Instance[nameof(HotkeyReset)];
    public static string SettingsInAppShortcutsLabel => LocalizationService.Instance[nameof(SettingsInAppShortcutsLabel)];

    // File links (body paste/drop)
    public static string PastedImagePrefix => LocalizationService.Instance[nameof(PastedImagePrefix)];

    // Backup / restore (settings)
    public static string SettingsBackupRow => LocalizationService.Instance[nameof(SettingsBackupRow)];
    public static string SettingsBackupLabel => LocalizationService.Instance[nameof(SettingsBackupLabel)];
    public static string SettingsBackupDesc => LocalizationService.Instance[nameof(SettingsBackupDesc)];
    public static string BackupButton => LocalizationService.Instance[nameof(BackupButton)];
    public static string RestoreButton => LocalizationService.Instance[nameof(RestoreButton)];
    public static string BackupInProgress => LocalizationService.Instance[nameof(BackupInProgress)];
    public static string BackupSucceeded => LocalizationService.Instance[nameof(BackupSucceeded)];
    public static string BackupFailed => LocalizationService.Instance[nameof(BackupFailed)];
    public static string BackupFlushBlocked => LocalizationService.Instance[nameof(BackupFlushBlocked)];
    public static string RestoreStagedRestarting => LocalizationService.Instance[nameof(RestoreStagedRestarting)];
    public static string RestoreIncompatible => LocalizationService.Instance[nameof(RestoreIncompatible)];
    public static string RestoreInvalid => LocalizationService.Instance[nameof(RestoreInvalid)];
    public static string RestoreFailed => LocalizationService.Instance[nameof(RestoreFailed)];

    // In-app shortcut action labels
    public static string ShortcutNewNote => LocalizationService.Instance[nameof(ShortcutNewNote)];
    public static string ShortcutGoToday => LocalizationService.Instance[nameof(ShortcutGoToday)];
    public static string ShortcutSettings => LocalizationService.Instance[nameof(ShortcutSettings)];
    public static string ShortcutToggleTheme => LocalizationService.Instance[nameof(ShortcutToggleTheme)];
    public static string ShortcutToggleLeft => LocalizationService.Instance[nameof(ShortcutToggleLeft)];
    public static string ShortcutToggleRight => LocalizationService.Instance[nameof(ShortcutToggleRight)];
    public static string ShortcutOpenSticky => LocalizationService.Instance[nameof(ShortcutOpenSticky)];
    public static string ShortcutQuickSticky => LocalizationService.Instance[nameof(ShortcutQuickSticky)];

    // Onboarding tutorial
    public static string TutorialBack => LocalizationService.Instance[nameof(TutorialBack)];
    public static string TutorialNext => LocalizationService.Instance[nameof(TutorialNext)];
    public static string TutorialSkip => LocalizationService.Instance[nameof(TutorialSkip)];
    public static string TutorialFinish => LocalizationService.Instance[nameof(TutorialFinish)];
    public static string TutorialProgressFormat => LocalizationService.Instance[nameof(TutorialProgressFormat)];
    public static string TutorialWelcomeTitle => LocalizationService.Instance[nameof(TutorialWelcomeTitle)];
    public static string TutorialWelcomeBody => LocalizationService.Instance[nameof(TutorialWelcomeBody)];
    public static string TutorialNotesTitle => LocalizationService.Instance[nameof(TutorialNotesTitle)];
    public static string TutorialNotesBody => LocalizationService.Instance[nameof(TutorialNotesBody)];
    public static string TutorialTodoTitle => LocalizationService.Instance[nameof(TutorialTodoTitle)];
    public static string TutorialTodoBody => LocalizationService.Instance[nameof(TutorialTodoBody)];
    public static string TutorialFilesTitle => LocalizationService.Instance[nameof(TutorialFilesTitle)];
    public static string TutorialFilesBody => LocalizationService.Instance[nameof(TutorialFilesBody)];
    public static string TutorialStickyTitle => LocalizationService.Instance[nameof(TutorialStickyTitle)];
    public static string TutorialStickyBody => LocalizationService.Instance[nameof(TutorialStickyBody)];
    public static string TutorialSearchTitle => LocalizationService.Instance[nameof(TutorialSearchTitle)];
    public static string TutorialSearchBody => LocalizationService.Instance[nameof(TutorialSearchBody)];
    public static string TutorialShortcutsTitle => LocalizationService.Instance[nameof(TutorialShortcutsTitle)];
    public static string TutorialShortcutsBody => LocalizationService.Instance[nameof(TutorialShortcutsBody)];
    public static string TutorialWrapTitle => LocalizationService.Instance[nameof(TutorialWrapTitle)];
    public static string TutorialWrapBody => LocalizationService.Instance[nameof(TutorialWrapBody)];
    public static string TutorialClosingTitle => LocalizationService.Instance[nameof(TutorialClosingTitle)];
    public static string TutorialClosingBody => LocalizationService.Instance[nameof(TutorialClosingBody)];

    // Sample note seeded on first run (dated to-dos use today's month/day via {0}/{1}).
    public static string SampleNoteTitle => LocalizationService.Instance[nameof(SampleNoteTitle)];
    public static string SampleNoteBodyFormat => LocalizationService.Instance[nameof(SampleNoteBodyFormat)];
    public static string TutorialShortcutSummon => LocalizationService.Instance[nameof(TutorialShortcutSummon)];
    public static string TutorialShortcutQuickSticky => LocalizationService.Instance[nameof(TutorialShortcutQuickSticky)];
    public static string SettingsTutorialRow => LocalizationService.Instance[nameof(SettingsTutorialRow)];
    public static string SettingsTutorialLabel => LocalizationService.Instance[nameof(SettingsTutorialLabel)];
    public static string SettingsTutorialButton => LocalizationService.Instance[nameof(SettingsTutorialButton)];

    // About / author (settings)
    public static string SettingsAboutRow => LocalizationService.Instance[nameof(SettingsAboutRow)];
    public static string SettingsAboutLabel => LocalizationService.Instance[nameof(SettingsAboutLabel)];
    public static string AuthorName => LocalizationService.Instance[nameof(AuthorName)];
    public static string AuthorEmail => LocalizationService.Instance[nameof(AuthorEmail)];

    // Tray menu
    public static string TrayAppName => LocalizationService.Instance[nameof(TrayAppName)];
    public static string TrayShow => LocalizationService.Instance[nameof(TrayShow)];
    public static string TraySettings => LocalizationService.Instance[nameof(TraySettings)];
    public static string TrayQuit => LocalizationService.Instance[nameof(TrayQuit)];

    // Calendar automation suffix
    public static string TodaySuffix => LocalizationService.Instance[nameof(TodaySuffix)];

    // ── Redesign (calendar-notes.dc.html, DESIGN.md Revision 2026-07-21) ──
    public static string AppTitle => LocalizationService.Instance[nameof(AppTitle)];
    public static string UnifiedSearchHint => LocalizationService.Instance[nameof(UnifiedSearchHint)];
    public static string ClearSearch => LocalizationService.Instance[nameof(ClearSearch)];
    public static string ThemeToggle => LocalizationService.Instance[nameof(ThemeToggle)];
    public static string SidebarsExpand => LocalizationService.Instance[nameof(SidebarsExpand)];
    public static string SidebarsCollapse => LocalizationService.Instance[nameof(SidebarsCollapse)];
    public static string TabTodoName => LocalizationService.Instance[nameof(TabTodoName)];
    public static string TabFilesName => LocalizationService.Instance[nameof(TabFilesName)];
    public static string WindowMinimize => LocalizationService.Instance[nameof(WindowMinimize)];
    public static string WindowMaximize => LocalizationService.Instance[nameof(WindowMaximize)];
    public static string WindowRestore => LocalizationService.Instance[nameof(WindowRestore)];
    public static string WindowClose => LocalizationService.Instance[nameof(WindowClose)];

    // Timeline view mode
    public static string TimelineToggle => LocalizationService.Instance[nameof(TimelineToggle)];
    public static string TimelineExpand => LocalizationService.Instance[nameof(TimelineExpand)];
    public static string TimelineCollapse => LocalizationService.Instance[nameof(TimelineCollapse)];
    public static string TimelineEmpty => LocalizationService.Instance[nameof(TimelineEmpty)];

    // Note list
    public static string NewNote => LocalizationService.Instance[nameof(NewNote)];
    /// <summary>{0} = note count.</summary>
    public static string NoteCountFormat => LocalizationService.Instance[nameof(NoteCountFormat)];
    public static string DayNotesEmpty => LocalizationService.Instance[nameof(DayNotesEmpty)];

    // Editor
    public static string TitlePlaceholder => LocalizationService.Instance[nameof(TitlePlaceholder)];
    public static string Favorite => LocalizationService.Instance[nameof(Favorite)];
    public static string OpenStickyNote => LocalizationService.Instance[nameof(OpenStickyNote)];
    public static string StickyNoteWindow => LocalizationService.Instance[nameof(StickyNoteWindow)];
    public static string PinStickyNote => LocalizationService.Instance[nameof(PinStickyNote)];
    public static string UnpinStickyNote => LocalizationService.Instance[nameof(UnpinStickyNote)];
    public static string CloseStickyNote => LocalizationService.Instance[nameof(CloseStickyNote)];
    public static string StickyNoteBody => LocalizationService.Instance[nameof(StickyNoteBody)];
    public static string DeleteNoteTip => LocalizationService.Instance[nameof(DeleteNoteTip)];
    public static string TagAddPlaceholder => LocalizationService.Instance[nameof(TagAddPlaceholder)];
    public static string NoNoteSelected => LocalizationService.Instance[nameof(NoNoteSelected)];
    /// <summary>{0} = character count, {1} = line count.</summary>
    public static string NoteMetaFormat => LocalizationService.Instance[nameof(NoteMetaFormat)];
    /// <summary>{0} = updated timestamp.</summary>
    public static string NoteUpdatedFormat => LocalizationService.Instance[nameof(NoteUpdatedFormat)];
    public static string EditorBodyPlaceholder => LocalizationService.Instance[nameof(EditorBodyPlaceholder)];

    // Right panel tabs
    public static string TabTodo => LocalizationService.Instance[nameof(TabTodo)];
    /// <summary>{0} = open todo count.</summary>
    public static string TabTodoFormat => LocalizationService.Instance[nameof(TabTodoFormat)];
    public static string TabTags => LocalizationService.Instance[nameof(TabTags)];
    /// <summary>{0} = distinct inline-tag count.</summary>
    public static string TabTagsFormat => LocalizationService.Instance[nameof(TabTagsFormat)];
    public static string TabTagsName => LocalizationService.Instance[nameof(TabTagsName)];
    public static string TabFiles => LocalizationService.Instance[nameof(TabFiles)];

    // Tags tab
    public static string TagPanelEmpty => LocalizationService.Instance[nameof(TagPanelEmpty)];

    // Todo tab
    public static string TodoEmptyText => LocalizationService.Instance[nameof(TodoEmptyText)];
    public static string TodoEmptyPrefix => LocalizationService.Instance[nameof(TodoEmptyPrefix)];
    public static string TodoEmptyCode => LocalizationService.Instance[nameof(TodoEmptyCode)];
    public static string TodoEmptySuffix => LocalizationService.Instance[nameof(TodoEmptySuffix)];

    // Clipboard tab

    // Files tab
    public static string AddFile => LocalizationService.Instance[nameof(AddFile)];
    public static string FileTabEmpty => LocalizationService.Instance[nameof(FileTabEmpty)];

    // Search dropdown result kinds
    public static string SearchKindDate => LocalizationService.Instance[nameof(SearchKindDate)];
    public static string SearchKindFile => LocalizationService.Instance[nameof(SearchKindFile)];
    public static string SearchNoResultsRow => LocalizationService.Instance[nameof(SearchNoResultsRow)];
    /// <summary>{0} = note count on a matched date.</summary>
    public static string SearchDateNoteCountFormat => LocalizationService.Instance[nameof(SearchDateNoteCountFormat)];

    // Language (settings)
    public static string SettingsLanguageRow => LocalizationService.Instance[nameof(SettingsLanguageRow)];
    public static string SettingsLanguageLabel => LocalizationService.Instance[nameof(SettingsLanguageLabel)];
    public static string SettingsLanguageDesc => LocalizationService.Instance[nameof(SettingsLanguageDesc)];
    public static string LanguageKorean => LocalizationService.Instance[nameof(LanguageKorean)];
    public static string LanguageEnglish => LocalizationService.Instance[nameof(LanguageEnglish)];

    // Date patterns (.NET custom format strings, not prose — see the catalogs).
    /// <summary>Full date with weekday, e.g. the calendar day heading's accessible name.</summary>
    public static string DateFormatLong => LocalizationService.Instance[nameof(DateFormatLong)];
    /// <summary>Month and year, e.g. the mini calendar's month label.</summary>
    public static string DateFormatMonth => LocalizationService.Instance[nameof(DateFormatMonth)];
    /// <summary>Compact month/day with abbreviated weekday, shown above the note list.</summary>
    public static string DateFormatDayHeading => LocalizationService.Instance[nameof(DateFormatDayHeading)];

    // Mini-calendar weekday header, Sunday first
    public static string WeekdaySun => LocalizationService.Instance[nameof(WeekdaySun)];
    public static string WeekdayMon => LocalizationService.Instance[nameof(WeekdayMon)];
    public static string WeekdayTue => LocalizationService.Instance[nameof(WeekdayTue)];
    public static string WeekdayWed => LocalizationService.Instance[nameof(WeekdayWed)];
    public static string WeekdayThu => LocalizationService.Instance[nameof(WeekdayThu)];
    public static string WeekdayFri => LocalizationService.Instance[nameof(WeekdayFri)];
    public static string WeekdaySat => LocalizationService.Instance[nameof(WeekdaySat)];

    // Compact shell (narrow-window layout)
    public static string CommandRegion => LocalizationService.Instance[nameof(CommandRegion)];
    public static string StatusRegion => LocalizationService.Instance[nameof(StatusRegion)];
    public static string CompactWorkspaceSwitch => LocalizationService.Instance[nameof(CompactWorkspaceSwitch)];
    public static string CompactNavigate => LocalizationService.Instance[nameof(CompactNavigate)];
    public static string CompactNavigateAutomation => LocalizationService.Instance[nameof(CompactNavigateAutomation)];
    public static string CompactNotes => LocalizationService.Instance[nameof(CompactNotes)];
    public static string CompactNotesAutomation => LocalizationService.Instance[nameof(CompactNotesAutomation)];
    public static string CalendarWeekdayHeader => LocalizationService.Instance[nameof(CalendarWeekdayHeader)];
    public static string CalendarDays => LocalizationService.Instance[nameof(CalendarDays)];

    /// <summary>Display title for a note the user never named. {0} = note number.</summary>
    public static string UntitledNoteFormat => LocalizationService.Instance[nameof(UntitledNoteFormat)];

    /// <summary>Win32 file-dialog filter for backup archives.</summary>
    public static string BackupZipFilter => LocalizationService.Instance[nameof(BackupZipFilter)];
}
