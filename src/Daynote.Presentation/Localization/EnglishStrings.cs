namespace Daynote.App.Localization;

/// <summary>
/// The English string catalog. Mirrors <see cref="KoreanStrings"/> key-for-key — a parity test
/// fails if either catalog gains a key the other lacks, or if a format string's <c>{0}</c>
/// placeholders do not match across the two. Format strings use <c>{0}</c> placeholders.
/// </summary>
internal static class EnglishStrings
{
    internal static IReadOnlyDictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Command region
        ["SearchHint"] = "Search notes",
        ["SearchAutomation"] = "Search notes",
        ["Settings"] = "Settings",
        ["OpenSettings"] = "Open settings",

        // Capture state (command-row chip, tray, settings)

        // Sidebar
        ["Today"] = "Today",
        ["GoToToday"] = "Go to today",
        ["AddNote"] = "Add note",
        ["PreviousMonth"] = "Previous month",
        ["NextMonth"] = "Next month",
        ["CalendarMonth"] = "Calendar month",
        ["SidebarNoteList"] = "Sidebar note list",
        ["MiniCalendar"] = "Mini month calendar",
        ["MoveNoteUpFormat"] = "Move {0} up",
        ["MoveNoteDownFormat"] = "Move {0} down",

        // Note region
        ["DateHeadingAutomation"] = "Selected date heading",
        ["OrderedNoteTabs"] = "Note tabs",
        ["MarkdownEditor"] = "Markdown editor",
        ["CloseNoteFormat"] = "Close {0}",
        ["EditorEmptyHint"] = "Start today's first entry",

        // Editor toolbar
        ["EditorToolbar"] = "Editor toolbar",
        ["SaveStatusAutomation"] = "Save status",
        ["FormattingCommands"] = "Markdown formatting commands",
        ["Bold"] = "Bold",
        ["Italic"] = "Italic",
        ["BulletedList"] = "Bulleted list",
        ["NumberedList"] = "Numbered list",
        ["InlineCode"] = "Inline code",

        // Save status text
        ["SaveDirty"] = "Unsaved",
        ["SaveSaving"] = "Saving",
        ["SaveSaved"] = "Saved",
        ["SaveFailed"] = "Save failed",
        ["Retry"] = "Retry",
        ["RetrySave"] = "Retry save",

        // Clipboard drawer
        ["Delete"] = "Delete",

        // Search
        ["SearchResultsOverlay"] = "Search results overlay",
        ["SearchSummary"] = "Search summary",
        ["SearchQuery"] = "Search query",
        ["SearchResultCount"] = "Search result count",
        ["SearchScope"] = "Search scope",
        ["SearchScopeValue"] = "Notes and files",
        ["SearchLoadingAutomation"] = "Searching",
        ["SearchLoading"] = "Searching…",
        ["SearchEmptyAutomation"] = "No search results",
        ["SearchEmpty"] = "No notes match your search.",
        ["SearchErrorAutomation"] = "Search error",
        ["SearchError"] = "Search is unavailable. Try again once storage is available.",
        ["SearchResults"] = "Search results",
        ["SearchResultSnippet"] = "Result preview",
        ["ResultDate"] = "Result date",
        ["NoteSource"] = "Note item",
        ["ClipboardSourceLabel"] = "File item",
        ["SearchKindNote"] = "Note",
        ["SearchKindClipboard"] = "File",
        ["StaleSearchResult"] = "That result is no longer available. The search results have been refreshed.",
        ["StaleSearchResultAutomation"] = "Stale search result",
        ["SearchNoResults"] = "No results",
        ["SearchUnavailableShort"] = "Search unavailable",
        ["SearchResultsShownFormat"] = "Showing {0} results",
        ["SearchResultsCountFormat"] = "{0} results",

        // Settings
        ["SettingsTitle"] = "Settings",
        ["CloseSettings"] = "Close settings",
        ["SettingsStartupRow"] = "Run at Windows startup",
        ["SettingsStartupLabel"] = "Start Daynote when I sign in",
        ["SettingsStartupAutomation"] = "Start Daynote at sign-in",
        ["SettingsStorageRow"] = "Storage location",
        ["SettingsStorageLabel"] = "Storage location",
        ["SettingsPrivacyRow"] = "Privacy",
        ["SettingsPrivacyLabel"] = "Privacy",
        ["SettingsStartupEnabledText"] = "Daynote starts automatically when you sign in.",
        ["SettingsStartupDisabledText"] = "Daynote does not start automatically.",
        ["SettingsStartupEnabledByPolicyText"] = "Set by administrator policy and cannot be changed here.",
        ["SettingsStartupDisabledByPolicyText"] = "Disabled by administrator policy and cannot be changed here.",
        ["SettingsStartupDisabledByUserText"] = "Turned off in Windows startup app settings. Re-enable it there.",
        ["SettingsStartupUnavailableText"] = "Startup app settings are unavailable on this device.",
        ["SettingsPrivacyText"] =
            "Notes are stored in a plain-text SQLite database on this PC, and attachments as local " +
            "files. Daynote itself has no analytics and no telemetry.",
        ["SettingsPrivacyMcp"] =
            "If you register the AI integration (MCP), the AI client you connect can read and write " +
            "your notes, and that client may send their content to its own service.",
        ["SettingsPrivacySync"] =
            "Cloud sync is off by default. When you sign in, notes are encrypted on this PC before they are uploaded.",

        // AI integration / MCP (settings)
        ["SettingsMcpRow"] = "AI integration",
        ["SettingsMcpLabel"] = "AI integration (MCP)",
        ["SettingsMcpDesc"] =
            "Let AI tools like Claude read and write your Daynote notes directly. The MCP server is " +
            "installed alongside Daynote, so one button registers it - nothing to build or download.",
        ["SettingsMcpRegister"] = "Register with Claude Desktop",
        ["SettingsMcpRegistered"] = "Registered. Restart Claude Desktop and the daynote tools appear.",
        ["SettingsMcpAlreadyRegistered"] = "Already registered.",
        ["SettingsMcpFailedFormat"] = "Registration failed. Please check the config file yourself: {0}",
        ["SettingsMcpUnavailable"] =
            "This build has no MCP server to register. The feature ships with Daynote installed from the Microsoft Store.",
        ["SettingsMcpCodeHint"] = "For Claude Code, run this one line in a terminal instead.",
        ["SettingsMcpFooter"] =
            "Tools provided: search, by-date, recent list, create, update, delete. See docs/MCP.md for details.",
        ["SettingsMcpCopy"] = "Copy",
        ["SettingsMcpCopied"] = "Copied!",

        // Shortcuts (settings)
        ["SettingsShortcutsRow"] = "Shortcuts",
        ["SettingsSummonHotkeyLabel"] = "Global summon shortcut",
        ["SettingsSummonHotkeyDesc"] =
            "Press this combination from anywhere to bring the window straight up, even while Daynote is hidden in the tray.",
        ["HotkeyChange"] = "Change",
        ["HotkeyCapturing"] = "Press a key combination…",
        ["HotkeyConflict"] = "Another app is already using that combination. Pick a different one.",
        ["HotkeyInvalid"] = "Press a modifier (Ctrl/Alt/Shift/Win) together with a regular key.",
        ["HotkeyReset"] = "Default",
        ["SettingsInAppShortcutsLabel"] = "In-app shortcuts",

        // File links (body paste/drop)
        ["PastedImagePrefix"] = "Image",

        // Backup / restore (settings)
        ["SettingsBackupRow"] = "Backup and restore",
        ["SettingsBackupLabel"] = "Back up and restore your data",
        ["SettingsBackupDesc"] =
            "Back up every note, attachment, and setting to a single zip file, or restore from a backup file.",
        ["BackupButton"] = "Back up",
        ["RestoreButton"] = "Restore",
        ["BackupInProgress"] = "Backing up…",
        ["BackupSucceeded"] = "Backup complete.",
        ["BackupFailed"] = "Backup failed. Check the destination location.",
        ["BackupFlushBlocked"] = "There are unsaved changes, so the backup cannot run. Resolve the save problem first.",
        ["RestoreStagedRestarting"] =
            "The restore is staged. It will be applied the next time the app starts. (Your current data is kept in the pre-restore-backup folder.)",
        ["RestoreIncompatible"] = "This backup was made by a newer version and cannot be restored.",
        ["RestoreInvalid"] = "That is not a valid Daynote backup file.",
        ["RestoreFailed"] = "Staging the restore failed.",

        // In-app shortcut action labels
        ["ShortcutNewNote"] = "New note",
        ["ShortcutGoToday"] = "Go to today",
        ["ShortcutSettings"] = "Open/close settings",
        ["ShortcutToggleTheme"] = "Toggle theme",
        ["ShortcutToggleLeft"] = "Collapse/expand left panel",
        ["ShortcutToggleRight"] = "Collapse/expand right panel",
        ["ShortcutOpenSticky"] = "Pop out as a sticky note",
        ["ShortcutQuickSticky"] = "New sticky note for today (global)",

        // Onboarding tutorial
        ["TutorialBack"] = "Back",
        ["TutorialNext"] = "Next",
        ["TutorialSkip"] = "Skip",
        ["TutorialFinish"] = "Get started",
        ["TutorialProgressFormat"] = "{0} / {1}",

        ["TutorialWelcomeTitle"] = "Welcome to Daynote",
        ["TutorialWelcomeBody"] = "A desktop app for writing and organizing notes by date. Here's a quick tour.",

        ["TutorialNotesTitle"] = "Notes and the calendar",
        ["TutorialNotesBody"] = "Pick a date in the calendar on the left and write in the middle. The + button adds a note, and everything saves as you type.",

        ["TutorialTodoTitle"] = "To-do checkboxes",
        ["TutorialTodoBody"] = "Type -[] in the body to make a checkbox, and add a due time like (7/25 14:00). They gather by date in the To-do tab on the right.",

        ["TutorialFilesTitle"] = "Attachments and body links",
        ["TutorialFilesBody"] = "Drop files onto the Files tab, or paste an image or file into the body — it is stored as a file and only a link stays in the text. Click the link to open it in the Files tab.",

        ["TutorialStickyTitle"] = "Sticky notes",
        ["TutorialStickyBody"] = "Pop a note out into a sticky window and pin it above everything else. The sticky note and the body edit together in real time.",

        ["TutorialSearchTitle"] = "Unified search",
        ["TutorialSearchBody"] = "Search titles, bodies, and files at once. Results highlight the part your keyword matched.",

        ["TutorialShortcutsTitle"] = "Shortcuts",
        ["TutorialShortcutsBody"] = "Reach what you use most with a keystroke. Global shortcuts work even while the app sits in the tray, and in-app shortcuts can be changed in settings.",

        ["TutorialWrapTitle"] = "Backup and settings",
        ["TutorialWrapBody"] = "Open settings from the account button at the right of the title bar, the tray menu, or Ctrl+,. Manage data backup and restore, shortcuts, and startup options there.",

        ["TutorialClosingTitle"] = "Ready when you are",
        ["TutorialClosingBody"] =
            "We left a sample note on today's date. Feel free to change it or delete it as you find your way around. " +
            "You can reopen this tour any time from Settings → Help. Enjoy Daynote!",

        // Sample note seeded on first run (dated to-dos use today's month/day via {0}/{1}).
        ["SampleNoteTitle"] = "Today's work notes (sample)",
        ["SampleNoteBodyFormat"] =
            "Here is one way to use Daynote. This note is only an example — delete it whenever you like.\n\n" +
            "-[] Write up the morning team standup ({0}/{1} 10:00)\n" +
            "-[x] Review last week's status report\n" +
            "-[] Send the reply to the client ({0}/{1} 15:00)\n\n" +
            "[Meeting notes]\n" +
            "- Shared the release schedule for the new feature\n" +
            "- Re-prioritized three QA issues\n\n" +
            "Tip: pasting a file into the body stores it in the Files tab and leaves a link behind. " +
            "Use the search box at the top to find anything across all your notes.",

        ["TutorialShortcutSummon"] = "Summon Daynote from anywhere (global)",
        ["TutorialShortcutQuickSticky"] = "New sticky note for today (global)",

        ["SettingsTutorialRow"] = "Help",
        ["SettingsTutorialLabel"] = "How-to tour",
        ["SettingsTutorialButton"] = "Replay the tour",

        // About / author (settings)
        ["SettingsAboutRow"] = "Author",
        ["SettingsAboutLabel"] = "Author",
        ["AuthorName"] = "Bread Jinhwa Jeong",
        ["AuthorEmail"] = "aracube@gmail.com",

        // Tray menu
        ["TrayAppName"] = "Daynote",
        ["TrayShow"] = "Show Daynote",
        ["TraySettings"] = "Settings",
        ["TrayQuit"] = "Quit",

        // Calendar automation suffix
        ["TodaySuffix"] = ", today",

        // ── Redesign (calendar-notes.dc.html, DESIGN.md Revision 2026-07-21) ──
        ["AppTitle"] = "Calendar Notes",
        ["UnifiedSearchHint"] = "Search everything — notes, files, dates",
        ["ClearSearch"] = "Clear search",
        ["ThemeToggle"] = "Toggle theme",
        ["SidebarsExpand"] = "Expand sidebars",
        ["SidebarsCollapse"] = "Collapse sidebars",
        ["TabTodoName"] = "To-do tab",
        ["TabFilesName"] = "Files tab",
        ["WindowMinimize"] = "Minimize",
        ["WindowMaximize"] = "Maximize",
        ["WindowRestore"] = "Restore down",
        ["WindowClose"] = "Close",

        // Timeline view mode
        ["TimelineToggle"] = "Timeline view",
        ["TimelineExpand"] = "Expand",
        ["TimelineCollapse"] = "Collapse",
        ["TimelineEmpty"] = "No notes to show",

        // Note list
        ["NewNote"] = "New note",
        ["NoteCountFormat"] = "{0} notes",
        ["DayNotesEmpty"] = "No notes on this date",

        // Editor
        ["TitlePlaceholder"] = "Title",
        ["Favorite"] = "Favorite",
        ["OpenStickyNote"] = "Open as a sticky note",
        ["StickyNoteWindow"] = "Sticky note",
        ["PinStickyNote"] = "Keep on top",
        ["UnpinStickyNote"] = "Stop keeping on top",
        ["CloseStickyNote"] = "Close sticky note",
        ["StickyNoteBody"] = "Sticky note body",
        ["DeleteNoteTip"] = "Delete note",
        ["TagAddPlaceholder"] = "+ tag",
        ["NoNoteSelected"] = "Pick a note from the list on the left, or create a new one",
        ["NoteMetaFormat"] = "{0} chars · {1} lines",
        ["NoteUpdatedFormat"] = "Edited {0}",
        ["EditorBodyPlaceholder"] = "Write your note.  Lines like '-[] task (07/25 14:00)' are added to the To-do panel automatically.",

        // Right panel tabs
        ["TabTodo"] = "To-do",
        ["TabTodoFormat"] = "To-do ({0})",
        ["TabTags"] = "Tags",
        ["TabTagsFormat"] = "Tags ({0})",
        ["TabTagsName"] = "Tags tab",
        ["TabFiles"] = "Files",
        ["TabFavoritesName"] = "Favorites tab",

        // Tags tab
        ["TagPanelEmpty"] = "Type #tags in your notes and they'll gather here.",

        // Favorites tab
        ["FavoritesPanelEmpty"] = "Star a note and it will be collected here.",
        ["FavoritesPreviewEmpty"] = "No content",

        // Todo tab
        ["TodoEmptyText"] = "(empty)",
        ["TodoEmptyPrefix"] = "Type ",
        ["TodoEmptyCode"] = "-[] task",
        ["TodoEmptySuffix"] = " in a note and it shows up here automatically",

        // Clipboard tab

        // Files tab
        ["AddFile"] = "Add files or images",
        ["FileTabEmpty"] = "No files kept on this date",

        // Search dropdown result kinds
        ["SearchKindDate"] = "Date",
        ["SearchKindFile"] = "File",
        ["SearchNoResultsRow"] = "No results found",
        ["SearchDateNoteCountFormat"] = "{0} notes",

        // Language (settings)
        ["SettingsLanguageRow"] = "Language",
        ["SettingsLanguageLabel"] = "Display language",
        ["SettingsLanguageDesc"] = "Your choice applies across the app right away — no restart needed.",
        ["LanguageKorean"] = "한국어",
        ["LanguageEnglish"] = "English",

        // Date patterns. These are .NET custom format strings, not prose: literal text inside a
        // pattern must stay single-quoted, and the field letters (yyyy/M/d/ddd) must survive
        // translation or the date renders wrong.
        ["DateFormatLong"] = "dddd, MMMM d, yyyy",
        ["DateFormatMonth"] = "MMMM yyyy",
        ["DateFormatDayHeading"] = "ddd, MMM d",

        // Mini-calendar weekday header, Sunday first
        ["WeekdaySun"] = "Su",
        ["WeekdayMon"] = "Mo",
        ["WeekdayTue"] = "Tu",
        ["WeekdayWed"] = "We",
        ["WeekdayThu"] = "Th",
        ["WeekdayFri"] = "Fr",
        ["WeekdaySat"] = "Sa",

        // Compact shell (narrow-window layout)
        ["CommandRegion"] = "Command region",
        ["StatusRegion"] = "Status region",
        ["CompactWorkspaceSwitch"] = "Compact workspace switcher",
        ["CompactNavigate"] = "Browse",
        ["CompactNavigateAutomation"] = "Browse view",
        ["CompactNotes"] = "Notes",
        ["CompactNotesAutomation"] = "Notes view",
        ["CalendarWeekdayHeader"] = "Calendar weekday header",
        ["CalendarDays"] = "Calendar days",

        // Default display title for a note the user never named ({0} = note number)
        ["UntitledNoteFormat"] = "Note {0}",

        // Cloud sync: account section and the command-row status chip
        ["CloudSyncTitle"] = "Cloud sync",
        ["CloudSyncBlurb"] = "Optional. Sign in with Google and your notes sync to your other devices. By default the server holds the key that opens them, so whoever runs the service can read what is stored — turn on the lock below and it cannot.",
        ["CloudSyncPrivacyNote"] = "The server stores your note bodies, titles, tags, and dates encrypted, and holds the key to that encryption. Your Google account id and email address are stored to identify the account.",
        ["CloudSyncLocalNote"] = "The database on this PC stays unencrypted. Sync is not a substitute for a backup, and attachments are not synced yet.",
        ["AccountSignInWithGoogle"] = "Sign in with Google",
        ["AccountSignInBrowserHint"] = "Your browser will open so you can sign in with Google. Daynote never sees or stores your password.",
        ["AccountKeyMissingBlurb"] = "This PC has no key to open your notes. You are still signed in, so it only needs fetching again.",
        ["AccountRestoreKey"] = "Fetch the key again",
        ["AccountErrorSignInCancelled"] = "The sign-in was not completed. Try again when you are ready.",
        ["AccountErrorUnverifiedIdentity"] = "This Google account has no verified email address, so it cannot be used to sign in.",
        ["AccountLockTitle"] = "Lock your notes (end-to-end encryption)",
        ["AccountLockBlurb"] = "Turning the lock on encrypts your notes with a passphrase, so nobody running the service can read them. In exchange, you enter that passphrase once on every new PC.",
        ["AccountLockOn"] = "On — the notes in this account cannot be opened on the server.",
        ["AccountLockOff"] = "Off — whoever runs the service can read your note content.",
        ["AccountLockEnable"] = "Turn the lock on",
        ["AccountLockDisable"] = "Turn the lock off",
        ["AccountLockConfirm"] = "Lock",
        ["AccountLockCancel"] = "Cancel",
        ["AccountPassphrase"] = "Lock passphrase",
        ["AccountPassphraseHint"] = "Use at least {0} characters. If you forget it, your recovery key is the only way back in.",
        ["AccountUnlockTitle"] = "Unlock your notes",
        ["AccountUnlockBlurb"] = "This account is locked. Enter its passphrase to open your notes on this PC. The notes already on this PC are untouched.",
        ["AccountUnlock"] = "Unlock",
        ["AccountUseRecoveryKey"] = "Use the recovery key instead",
        ["AccountUsePassphrase"] = "Use the passphrase instead",
        ["AccountRecoveryKeyLabel"] = "Recovery key",
        ["AccountLockedTitle"] = "Your notes are locked",
        ["RecoveryKeyTitle"] = "Recovery key",
        ["RecoveryKeyBlurb"] = "Write this down somewhere safe. It is the only way to recover the notes in your cloud copy if you forget the lock passphrase, and it cannot be shown again.",
        ["RecoveryKeyCopy"] = "Copy",
        ["RecoveryKeyCopied"] = "Copied",
        ["RecoveryKeySaveToFile"] = "Save to file",
        ["RecoveryKeyConfirm"] = "I have saved my recovery key",
        ["RecoveryKeyDone"] = "Done",
        ["RecoveryKeyFileFilter"] = "Text file (*.txt)|*.txt",
        ["AccountErrorInvalidPassphrase"] = "That lock passphrase is incorrect.",
        ["AccountErrorInvalidRecoveryKey"] = "That is not this account’s recovery key.",
        ["AccountErrorLockedOut"] = "This PC cannot open your notes. Enter the lock passphrase or your recovery key.",
        ["AccountErrorWeakPassphrase"] = "Use at least {0} characters.",
        ["AccountErrorUnsupportedVersion"] = "This account was locked by a newer version of Daynote. Update the app.",
        ["BillingTitle"] = "Subscription",
        ["BillingBlurb"] = "Cloud sync is a subscription feature. Daynote itself, and the notes on this PC, keep working either way.",
        ["BillingTrialFormat"] = "{0} days left in your free trial.",
        ["BillingActive"] = "Subscribed.",
        ["BillingActiveFormat"] = "Subscribed. Renews on {0}.",
        ["BillingGraceFormat"] = "We could not take your payment. Please check your payment method within {0} days.",
        ["BillingTrialOver"] = "Your trial has ended. Subscribe to start syncing again.",
        ["BillingExpired"] = "Your subscription ended, so syncing has stopped. The notes you already uploaded are kept.",
        ["BillingSubscribe"] = "Subscribe",
        ["BillingRenew"] = "Renew subscription",
        ["BillingSubscribeAnnual"] = "Subscribe yearly · $19.99 / year",
        ["BillingSubscribeMonthly"] = "Monthly · $2.49 / month",
        ["BillingManage"] = "Manage subscription",
        ["BillingLapseNote"] = "Nothing is deleted when syncing stops. The notes on this PC are untouched, and your cloud copy is kept — resubscribe and it picks up where it left off.",
        ["BillingBrowserHint"] = "Payment happens in your browser. Card details are handled by the payment provider; Daynote never stores them.",
        ["SyncChipUnpaid"] = "Subscription needed",
        ["AccountSignOut"] = "Sign out",
        ["AccountSyncNow"] = "Sync now",
        ["AccountBusy"] = "Working…",
        // {0} = the signed-in email address.
        ["AccountSignedInAsFormat"] = "Signed in as {0}",
        // {0} = a formatted local time.
        ["AccountLastSyncFormat"] = "Last synced {0}",
        ["AccountNeverSynced"] = "Not synced yet",
        // {0} = number of notes replaced by a newer version from another device.
        ["AccountConflictsFormat"] = "{0} notes were replaced by a newer version from another device. The earlier text was kept as a copy.",
        ["AccountOpenConflicts"] = "Open the copies folder",
        ["SyncChipSynced"] = "Synced",
        ["SyncChipSyncing"] = "Syncing",
        ["SyncChipPending"] = "Pending",
        ["SyncChipOffline"] = "Offline",
        ["SyncChipLocked"] = "Locked",
        ["SyncChipError"] = "Sync problem",
        ["SyncStatusAutomation"] = "Cloud sync status",
        ["AccountErrorInvalidCredentials"] = "That sign-in is no longer valid. Sign in again.",
        ["AccountErrorOffline"] = "Daynote could not reach the sync service. Check your connection and try again.",
        ["AccountErrorServer"] = "The sync service reported a problem. Try again in a moment.",


        // Account window (docs/design-renewal/Daynote Account.dc.html)
        ["AccountWindowTitle"] = "Daynote account",
        ["AccountMenuAutomation"] = "Account",
        ["AccountManage"] = "Account & subscription",
        ["AccountAvatarTooltipFormat"] = "{0} · {1} · {2}",
        ["AccountSignInTitle"] = "Sign in to Daynote",
        ["AccountSignInLead"] = "Your notes stay on this PC either way. Signing in is only for seeing the same notes on more than one device.",
        ["AccountSignInTerms"] = "Continuing means you accept the terms of service and the privacy policy.",
        ["AccountTerms"] = "Terms of service",
        ["AccountPrivacy"] = "Privacy policy",
        ["AccountSkipSignIn"] = "Keep using Daynote without an account",
        ["AccountPerkSyncTitle"] = "Sync across devices",
        ["AccountPerkSyncDesc"] = "The same notes on your laptop and desktop",
        ["AccountPerkLockTitle"] = "Note lock",
        ["AccountPerkLockDesc"] = "Turn it on and the server cannot read them either",
        ["AccountPerkExportTitle"] = "Export anytime",
        ["AccountPerkExportDesc"] = "Save everything as a backup file",
        ["AccountPlanFree"] = "Free",
        ["AccountPlanTrial"] = "Trial",
        ["AccountPlanPro"] = "Pro",
        ["AccountPlanGrace"] = "Payment pending",
        ["BillingTrialBannerTitleFormat"] = "{0} days left in your Pro trial",
        ["BillingTrialBannerBody"] = "Subscribe before the trial ends and syncing carries on without a break. If it does stop, your notes stay where they are.",
        ["BillingGraceBannerTitle"] = "A payment did not go through",
        ["BillingGraceBannerBodyFormat"] = "Please check your payment method within {0} days. Syncing keeps working until then.",
        ["BillingExpiredBannerTitle"] = "Syncing has stopped",
        ["BillingUpgradeTitle"] = "Upgrade to Pro",
        ["BillingPlanMonthly"] = "Monthly",
        ["BillingPlanAnnual"] = "Yearly",
        ["BillingPriceMonthly"] = "$2.49",
        ["BillingPriceAnnual"] = "$19.99",
        ["BillingPriceUnitMonthly"] = "/ month",
        ["BillingPriceUnitAnnual"] = "/ year",
        ["BillingPriceSubMonthly"] = "Billed monthly · cancel anytime",
        ["BillingPriceSubAnnual"] = "About $1.67 a month · billed once a year",
        ["BillingAnnualSaving"] = "Save 33%",
        ["BillingFeatureSync"] = "Cloud sync across all your devices",
        ["BillingFeatureDevices"] = "No limit on connected devices",
        ["BillingFeatureLock"] = "Note lock (encryption the server cannot open)",
        ["BillingFeatureQuota"] = "No note storage limit",
        ["BillingCheckoutFormat"] = "Subscribe to Pro · {0}",
        ["BillingProviderNote"] = "Paddle handles payment and tax as the merchant of record · cancel anytime",
        ["BillingDetailTitle"] = "Subscription",
        ["BillingRowPlan"] = "Plan",
        ["BillingRowState"] = "Status",
        ["BillingRowRenews"] = "Next payment",
        ["BillingRowEnds"] = "Access until",
        ["BillingStateActive"] = "Active",
        ["BillingStateTrial"] = "Trial",
        ["BillingStateGrace"] = "Payment failed · retrying",
        ["BillingPortalNote"] = "Changing your card, switching between monthly and yearly, cancelling, and finding receipts all happen on the Paddle portal.",
        ["AccountVersionFormat"] = "Daynote {0}",
        ["AccountSyncState"] = "Cloud sync",

        // Site paths (joined onto the sync service origin; language-specific)
        ["AccountTermsUrl"] = "/en/terms/",
        ["AccountPrivacyUrl"] = "/privacy",

        // File-dialog filter (pipe-delimited Win32 syntax; only the label is translated)
        ["BackupZipFilter"] = "Daynote backup (*.zip)|*.zip",
    };
}
