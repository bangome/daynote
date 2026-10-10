using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Localization;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// Bindable view over the static <see cref="AppStrings"/> catalog, the phone's copy of the desktop
/// <c>AppStringsProxy</c>: Avalonia's compiled bindings want an instance path, and one
/// <c>PropertyChanged(string.Empty)</c> re-reads every label after a language switch.
/// </summary>
/// <remarks>
/// Deliberately not shared with the desktop proxy. That one is a fixed list of the labels the desktop
/// window happens to use; this one exposes the catalog by key, which is all the phone views need and
/// means a new label never has to be added in two places.
/// <para>
/// Labels only the phone uses live in <see cref="MobileCatalog"/> rather than the shared catalog,
/// and are looked up first, so a phone screen never waits on a change to the desktop's strings.
/// </para>
/// </remarks>
public sealed class MobileStrings : ObservableObject
{
    public static MobileStrings Instance { get; } = new();

    public MobileStrings() => LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);

    /// <summary>Any catalog key by name: <c>{Binding Strings[AccountSignInTitle]}</c>.</summary>
    public string this[string key] => Get(key);

    /// <summary>The phone's own labels first, then the shared catalog.</summary>
    public static string Get(string key) =>
        MobileCatalog.For(LocalizationService.Instance.Language).TryGetValue(key, out string? value)
            ? value
            : LocalizationService.Instance[key];

    /// <summary>A phone label with its placeholders filled in, in the active culture.</summary>
    public static string Format(string key, params object[] args) =>
        string.Format(LocalizationService.Instance.Culture, Get(key), args);

    /// <summary>
    /// The to-do panel's empty line, which the catalog stores in three pieces so the middle one can
    /// be the literal syntax the user types.
    /// </summary>
    public string TodoEmpty =>
        AppStrings.TodoEmptyPrefix + AppStrings.TodoEmptyCode + AppStrings.TodoEmptySuffix;
}

/// <summary>The labels only the phone uses, in both languages. Every key is in both tables.</summary>
public static class MobileCatalog
{
    public static IReadOnlyDictionary<string, string> For(AppLanguage language) =>
        language == AppLanguage.English ? English : Korean;

    public static readonly IReadOnlyDictionary<string, string> Korean = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MobileBackToToday"] = "오늘로",
        ["MobileDayNumberFormat"] = "{0}일",
        ["MobileNotesHeader"] = "노트",
        ["MobileDayTodosHeader"] = "이 날의 할 일",
        ["MobileDayEmptyTitle"] = "아직 기록이 없는 날",
        ["MobileDayEmptyHint"] = "눌러서 첫 노트를 시작하세요",
        ["MobileNotePreviewEmpty"] = "내용 없음",
        ["MobilePreviousWeek"] = "이전 주",
        ["MobileNextWeek"] = "다음 주",
        ["MobilePreviousYear"] = "이전 해",
        ["MobileNextYear"] = "다음 해",
        ["MobileYearFormat"] = "{0}년",
        ["MobileTitlePlaceholder"] = "제목",
        ["MobileBodyPlaceholder"] = "오늘을 기록하세요",
        ["MobileSearchPlaceholder"] = "노트, 태그, 날짜",
        ["MobileSearchClear"] = "지우기",
        ["MobileSearchRecent"] = "최근",
        ["MobileSearchByTag"] = "태그로 찾기",
        ["MobileSearchResultCountFormat"] = "결과 {0}개",
        ["MobileTodoOverdue"] = "지남",
        ["MobileTodoToday"] = "오늘",
        ["MobileTodoUpcoming"] = "예정",
        ["MobileTodoDone"] = "완료",
        ["MobileTodoAddShort"] = "할 일",
        ["MobileTodoAddRow"] = "+ 할 일 추가",
        ["MobileTodoAddedTo"] = "{0}에 추가됨",
        ["MobileTodoAddedDateFormat"] = "M/d",
        ["MobileTodoView"] = "보기",
        ["MobileTodoEdit"] = "편집",
        ["MobileTodoDelete"] = "삭제",
        ["MobileTodoEditTitle"] = "할 일 편집",
        ["MobileTodoSave"] = "저장",
        ["MobileTodoDeleted"] = "삭제됨",
        ["MobileTodoUndo"] = "실행 취소",
        ["MobileRepeatDeleteQuestion"] = "반복되는 할 일입니다",
        ["MobileRepeatDeleteThis"] = "이 항목만 삭제",
        ["MobileRepeatDeleteAll"] = "이후 모든 반복 삭제",
        ["MobileRepeatEditThis"] = "이 항목만",
        ["MobileRepeatEditAll"] = "모든 반복",
        ["MobileListAll"] = "전체",
        ["MobileListNew"] = "+ 리스트",
        ["MobileListNamePlaceholder"] = "리스트 이름",
        ["MobileListCreate"] = "만들기",
        ["MobileListDefault"] = "기본 리스트",
        ["MobileListDefaultHint"] = "날짜·리스트를 정하지 않은 할 일이 여기 들어옵니다.",
        // The wide layouts (Daynote Tablet, Mobile B Foldables): the rail, the sidebar, the day panel.
        ["MobileWideNewNote"] = "+ 새 노트",
        ["MobileWideSidebar"] = "사이드바",
        ["MobileWideNoNote"] = "왼쪽에서 노트를 고르면 여기에 열립니다",
        ["MobileWideTodoLeftFormat"] = "남은 {0}",
        ["MobileWideDoneFormat"] = "완료 {0}",
        ["MobileWideToday"] = "오늘",
        ["MobileWideNoTodos"] = "이 날의 할 일이 없습니다.",
        ["MobileSettingsDisplay"] = "화면",
        ["MobileSettingsTheme"] = "테마",
        ["MobileThemeLight"] = "라이트",
        ["MobileThemeDark"] = "다크",
        ["MobileSettingsLanguage"] = "표시 언어",
        ["MobileLanguageKorean"] = "한국어",
        ["MobileLanguageEnglish"] = "EN",
        ["MobileSettingsData"] = "데이터",
        ["MobileStorageLocation"] = "저장 위치",
        ["MobileStorageDevice"] = "이 기기",
        ["MobileStorageDeviceCloud"] = "이 기기 + 클라우드",
        ["MobileSyncNow"] = "지금 동기화",
        ["MobileSyncRun"] = "실행",
        ["MobileAccountTitle"] = "계정",
        ["MobileAccountSignInSubtitle"] = "로그인하면 다른 기기와 동기화됩니다",
        ["MobileAccountAttention"] = "확인이 필요합니다",
        ["MobileNoteCountFormat"] = "노트 {0}개",
        ["MobileVersionFormat"] = "데이노트 {0}",
        ["MobileEditorDateFormat"] = "M'월' d'일' dddd",
        ["MobileFilesHeader"] = "파일",
        ["MobileFilesAdd"] = "첨부",
        ["MobileFilesEmpty"] = "이 날 보관된 파일이 없습니다",
        ["MobileFilesLocalOnly"] = "로그인하지 않아 파일은 이 기기에만 저장됩니다.",
        ["MobileFilesNotSynced"] = "이 계정에서는 파일이 동기화되지 않습니다. 파일은 이 기기에 그대로 남습니다.",
        ["MobileFilesQuotaFull"] = "클라우드 저장 공간이 가득 차 새 파일은 이 기기에만 저장됩니다.",
        ["MobileFilesTooLargeFormat"] = "{0}MB보다 큰 파일은 첨부할 수 없습니다.",
        ["MobileFilesAddFailed"] = "파일을 읽지 못해 첨부하지 못했습니다.",
        ["MobileFilesStillDownloading"] = "다른 기기에서 아직 내려받는 중입니다.",
        ["MobileFilesOpenFailed"] = "이 기기에 파일이 없어 열 수 없습니다.",
        ["MobileFilesNoApp"] = "이 파일을 열 앱이 없어 사본으로 저장합니다.",
        ["MobileFileCopySaved"] = "사본을 저장했습니다",
        ["MobileFileUploadPending"] = "업로드 대기",
        ["MobileAttachTitle"] = "첨부하기",
        ["MobileAttachPhotos"] = "사진",
        ["MobileAttachPhotosHint"] = "사진 보관함에서 고르기",
        ["MobileAttachFiles"] = "파일",
        ["MobileAttachFilesHint"] = "기기나 클라우드 드라이브에서 고르기",
        ["MobileFileSaveCopy"] = "사본 저장",
        ["MobileFileDelete"] = "삭제",
        ["MobileFileDeleteQuestion"] = "이 파일을 삭제할까요?",
        ["MobileFileDeleteBody"] = "이 날에서 지워지고, 동기화된 다른 기기에서도 사라집니다.",
        ["MobileFileMore"] = "파일 메뉴",
        ["MobileCancel"] = "취소",
        ["MobileClose"] = "닫기",

        // The storefront. Phone-only: the desktop sells through Paddle in a browser, so none of
        // these labels has a desktop caller, and they sat in the shared catalogue with no
        // AppStrings accessor until StringCatalogTests caught them.
        ["StoreTitle"] = "요금제",
        ["StoreEntryTitle"] = "요금제 · 구독",
        ["StoreEntryFree"] = "무료 · Pro와 Premium 보기",
        ["StoreSignInTitle"] = "구독하려면 먼저 로그인하세요",
        ["StoreSignInBody"] = "구독은 Daynote 계정에 연결됩니다. 같은 계정으로 로그인한 모든 기기에서 이미지·파일이 동기화됩니다.",
        ["StoreSignIn"] = "로그인하기",
        ["StoreManagedTitle"] = "컴퓨터에서 구독 중",
        ["StoreManagedBody"] = "이 계정의 구독은 컴퓨터의 데이노트에서 관리됩니다. 같은 구독으로 이 iPhone에서도 이미지·파일이 동기화됩니다.",
        ["StoreAppleManaged"] = "App Store 구독",
        ["StoreFreeDetail"] = "노트·할 일 동기화",
        ["StoreProDetail"] = "이미지·파일 동기화 2GB",
        ["StorePremiumDetail"] = "이미지·파일 동기화 무제한(공정 사용)",
        ["StoreSubscribe"] = "구독하기",
        ["StoreUpgrade"] = "Premium으로 업그레이드",
        ["StoreSwitch"] = "이 플랜으로 변경",
        ["StoreLengthMonth"] = "1개월",
        ["StoreLengthYear"] = "1년",
        ["StoreTermFormat"] = "{0} · {1} · {2}",
        ["StorePriceLoading"] = "가격 불러오는 중…",
        ["StorePricesUnavailable"] = "지금은 App Store에서 가격을 불러올 수 없습니다. 잠시 후 다시 열어 주세요.",
        ["StoreCannotPay"] = "이 기기에서는 구입이 제한되어 있습니다(스크린 타임 설정).",
        ["StoreAutoRenew"] = "구입을 확인하면 Apple ID로 결제됩니다. 구독은 현재 기간이 끝나기 최소 24시간 전에 해지하지 않으면 같은 기간과 가격으로 자동 갱신되며, 갱신 요금은 기간이 끝나기 전 24시간 안에 청구됩니다. 설정 › Apple ID › 구독에서 언제든 관리하고 해지할 수 있습니다.",
        ["StoreTerms"] = "이용약관(EULA)",
        ["StorePrivacy"] = "개인정보처리방침",
        ["StoreRestore"] = "구매 복원",
        ["StoreManage"] = "구독 관리",
        ["StoreRestoreNone"] = "이 Apple ID로 복원할 구독이 없습니다.",
        ["StoreRestoreDone"] = "구독을 복원했습니다.",
        ["StoreDoneFormat"] = "{0} 구독이 시작되었습니다.",
        ["StoreChangedFormat"] = "{0} 플랜으로 변경되었습니다. App Store가 남은 기간을 정산합니다.",
        ["StorePending"] = "구입 승인을 기다리는 중입니다. 승인되면 자동으로 반영됩니다.",
        ["StoreFailed"] = "구입을 마치지 못했습니다. 다시 시도해 주세요.",
        ["StoreConfirming"] = "구매를 확인하는 중…",
        ["StoreConfirmLater"] = "구매가 완료되었습니다. 확인이 끝나는 대로 자동으로 반영됩니다.",
        ["StoreOtherAccount"] = "이 App Store 구독은 다른 Daynote 계정에 연결되어 있습니다. 그 계정으로 로그인하면 사용할 수 있습니다.",
        ["StoreDuplicateNote"] = "같은 계정에 구독이 두 개 결제되고 있습니다. 하나를 해지해 주세요. App Store 구독은 설정 › Apple ID › 구독에서 해지할 수 있고, 환불은 Apple에 요청할 수 있습니다.",
        ["StoreRenewsFormat"] = "다음 갱신일 {0}",
        ["StoreEndsFormat"] = "{0}까지 이용",
        ["StoreStorageFormat"] = "저장 공간 {0}",
        // The home-screen widgets (Android). Drawn outside the app, in the app's language.
        ["WidgetToday"] = "오늘",
        ["WidgetRemainingFormat"] = "남은 {0}",
        ["WidgetUpNext"] = "다음 일정",
        ["WidgetNoEvent"] = "다가오는 일정 없음",
        ["WidgetEmpty"] = "오늘 남은 할 일이 없어요",
        ["WidgetLocked"] = "잠겨 있습니다. 앱에서 잠금을 풀면 보입니다.",
        ["WidgetOutdated"] = "Daynote를 한 번 열면 다시 보입니다.",
        ["WidgetNewNote"] = "+ 노트",
        ["WidgetNewTodo"] = "+ 할 일",
        ["WidgetAddTodo"] = "할 일 추가",
        ["WidgetCompleteFormat"] = "{0} 완료",
        ["WidgetReopenFormat"] = "{0} 완료 취소",
        ["WidgetMoreFormat"] = "외 {0}개",
        ["WidgetTomorrow"] = "내일",
        ["WidgetAllDay"] = "종일",
    };

    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MobileBackToToday"] = "Today",
        ["MobileDayNumberFormat"] = "{0}",
        ["MobileNotesHeader"] = "Notes",
        ["MobileDayTodosHeader"] = "To-dos this day",
        ["MobileDayEmptyTitle"] = "Nothing written yet",
        ["MobileDayEmptyHint"] = "Tap to start the first note",
        ["MobileNotePreviewEmpty"] = "Empty",
        ["MobilePreviousWeek"] = "Previous week",
        ["MobileNextWeek"] = "Next week",
        ["MobilePreviousYear"] = "Previous year",
        ["MobileNextYear"] = "Next year",
        ["MobileYearFormat"] = "{0}",
        ["MobileTitlePlaceholder"] = "Title",
        ["MobileBodyPlaceholder"] = "Write down today",
        ["MobileSearchPlaceholder"] = "Notes, tags, dates",
        ["MobileSearchClear"] = "Clear",
        ["MobileSearchRecent"] = "Recent",
        ["MobileSearchByTag"] = "Find by tag",
        ["MobileSearchResultCountFormat"] = "{0} results",
        ["MobileTodoOverdue"] = "Overdue",
        ["MobileTodoToday"] = "Today",
        ["MobileTodoUpcoming"] = "Upcoming",
        ["MobileTodoDone"] = "Done",
        ["MobileTodoAddShort"] = "To-do",
        ["MobileTodoAddRow"] = "+ Add to-do",
        ["MobileTodoAddedTo"] = "Added to {0}",
        ["MobileTodoAddedDateFormat"] = "MMM d",
        ["MobileTodoView"] = "View",
        ["MobileTodoEdit"] = "Edit",
        ["MobileTodoDelete"] = "Delete",
        ["MobileTodoEditTitle"] = "Edit to-do",
        ["MobileTodoSave"] = "Save",
        ["MobileTodoDeleted"] = "Deleted",
        ["MobileTodoUndo"] = "Undo",
        ["MobileRepeatDeleteQuestion"] = "This is a repeating to-do",
        ["MobileRepeatDeleteThis"] = "Delete this one only",
        ["MobileRepeatDeleteAll"] = "Delete all repeats",
        ["MobileRepeatEditThis"] = "This one only",
        ["MobileRepeatEditAll"] = "All repeats",
        ["MobileListAll"] = "All",
        ["MobileListNew"] = "+ List",
        ["MobileListNamePlaceholder"] = "List name",
        ["MobileListCreate"] = "Create",
        ["MobileListDefault"] = "Default list",
        ["MobileListDefaultHint"] = "To-dos with no date and no list land here.",
        // The wide layouts (Daynote Tablet, Mobile B Foldables): the rail, the sidebar, the day panel.
        ["MobileWideNewNote"] = "+ New",
        ["MobileWideSidebar"] = "Sidebar",
        ["MobileWideNoNote"] = "Pick a note on the left to open it here",
        ["MobileWideTodoLeftFormat"] = "{0} left",
        ["MobileWideDoneFormat"] = "Done {0}",
        ["MobileWideToday"] = "Today",
        ["MobileWideNoTodos"] = "No to-dos on this day.",
        ["MobileSettingsDisplay"] = "Display",
        ["MobileSettingsTheme"] = "Theme",
        ["MobileThemeLight"] = "Light",
        ["MobileThemeDark"] = "Dark",
        ["MobileSettingsLanguage"] = "Language",
        ["MobileLanguageKorean"] = "한국어",
        ["MobileLanguageEnglish"] = "EN",
        ["MobileSettingsData"] = "Data",
        ["MobileStorageLocation"] = "Stored on",
        ["MobileStorageDevice"] = "This device",
        ["MobileStorageDeviceCloud"] = "This device + cloud",
        ["MobileSyncNow"] = "Sync now",
        ["MobileSyncRun"] = "Run",
        ["MobileAccountTitle"] = "Account",
        ["MobileAccountSignInSubtitle"] = "Sign in to sync with your other devices",
        ["MobileAccountAttention"] = "Needs your attention",
        ["MobileNoteCountFormat"] = "{0} notes",
        ["MobileVersionFormat"] = "Daynote {0}",
        ["MobileEditorDateFormat"] = "dddd, MMM d",
        ["MobileFilesHeader"] = "Files",
        ["MobileFilesAdd"] = "Attach",
        ["MobileFilesEmpty"] = "No files on this day",
        ["MobileFilesLocalOnly"] = "Not signed in, so files stay on this device.",
        ["MobileFilesNotSynced"] = "Files don't sync on this account. They stay on this device.",
        ["MobileFilesQuotaFull"] = "Cloud storage is full, so new files stay on this device.",
        ["MobileFilesTooLargeFormat"] = "Files larger than {0} MB can't be attached.",
        ["MobileFilesAddFailed"] = "Couldn't read the file, so it wasn't attached.",
        ["MobileFilesStillDownloading"] = "Still downloading from your other device.",
        ["MobileFilesOpenFailed"] = "This file isn't on this device, so it can't be opened.",
        ["MobileFilesNoApp"] = "No app can open this file, so save a copy instead.",
        ["MobileFileCopySaved"] = "Copy saved",
        ["MobileFileUploadPending"] = "Waiting to upload",
        ["MobileAttachTitle"] = "Attach",
        ["MobileAttachPhotos"] = "Photos",
        ["MobileAttachPhotosHint"] = "Choose from your photo library",
        ["MobileAttachFiles"] = "Files",
        ["MobileAttachFilesHint"] = "Choose from this device or a cloud drive",
        ["MobileFileSaveCopy"] = "Save a copy",
        ["MobileFileDelete"] = "Delete",
        ["MobileFileDeleteQuestion"] = "Delete this file?",
        ["MobileFileDeleteBody"] = "It is removed from this day and from your other synced devices.",
        ["MobileFileMore"] = "File options",
        ["MobileCancel"] = "Cancel",
        ["MobileClose"] = "Close",

        // The storefront. Phone-only: the desktop sells through Paddle in a browser, so none of
        // these labels has a desktop caller, and they sat in the shared catalogue with no
        // AppStrings accessor until StringCatalogTests caught them.
        ["StoreTitle"] = "Plans",
        ["StoreEntryTitle"] = "Plans & subscription",
        ["StoreEntryFree"] = "Free · see Pro and Premium",
        ["StoreSignInTitle"] = "Sign in to subscribe",
        ["StoreSignInBody"] = "A subscription belongs to your Daynote account. Images and files then sync on every device signed in to it.",
        ["StoreSignIn"] = "Sign in",
        ["StoreManagedTitle"] = "Subscribed on your computer",
        ["StoreManagedBody"] = "This account's subscription is managed in Daynote on your computer. The same subscription syncs images and files on this iPhone too.",
        ["StoreAppleManaged"] = "App Store subscription",
        ["StoreFreeDetail"] = "Notes and to-dos sync",
        ["StoreProDetail"] = "Image and file sync, 2 GB",
        ["StorePremiumDetail"] = "Unlimited image and file sync (fair use)",
        ["StoreSubscribe"] = "Subscribe",
        ["StoreUpgrade"] = "Upgrade to Premium",
        ["StoreSwitch"] = "Switch to this plan",
        ["StoreLengthMonth"] = "1 month",
        ["StoreLengthYear"] = "1 year",
        ["StoreTermFormat"] = "{0} · {1} · {2}",
        ["StorePriceLoading"] = "Loading price…",
        ["StorePricesUnavailable"] = "The App Store prices could not be loaded right now. Open this page again in a moment.",
        ["StoreCannotPay"] = "Purchases are restricted on this device (Screen Time).",
        ["StoreAutoRenew"] = "Payment is charged to your Apple ID when you confirm the purchase. The subscription renews automatically for the same length and price unless it is cancelled at least 24 hours before the current period ends, and the renewal is charged within the 24 hours before the period ends. Manage or cancel it at any time in Settings › Apple ID › Subscriptions.",
        ["StoreTerms"] = "Terms of Use (EULA)",
        ["StorePrivacy"] = "Privacy Policy",
        ["StoreRestore"] = "Restore Purchases",
        ["StoreManage"] = "Manage Subscription",
        ["StoreRestoreNone"] = "There is no subscription to restore for this Apple ID.",
        ["StoreRestoreDone"] = "Your subscription was restored.",
        ["StoreDoneFormat"] = "Your {0} subscription has started.",
        ["StoreChangedFormat"] = "Changed to {0}. The App Store settles the rest of the period.",
        ["StorePending"] = "Waiting for the purchase to be approved. It applies on its own once it is.",
        ["StoreFailed"] = "The purchase could not be completed. Try again.",
        ["StoreConfirming"] = "Confirming your purchase…",
        ["StoreConfirmLater"] = "Your purchase is complete. It will show here on its own as soon as it is confirmed.",
        ["StoreOtherAccount"] = "This App Store subscription belongs to a different Daynote account. Sign in with that account to use it.",
        ["StoreDuplicateNote"] = "Two subscriptions are being paid for this account. Cancel one: an App Store subscription is cancelled in Settings › Apple ID › Subscriptions, and refunds are requested from Apple.",
        ["StoreRenewsFormat"] = "Renews {0}",
        ["StoreEndsFormat"] = "Active until {0}",
        ["StoreStorageFormat"] = "Storage {0}",
        ["WidgetToday"] = "Today",
        ["WidgetRemainingFormat"] = "{0} left",
        ["WidgetUpNext"] = "Up next",
        ["WidgetNoEvent"] = "Nothing scheduled",
        ["WidgetEmpty"] = "Nothing left for today",
        ["WidgetLocked"] = "Locked. Unlock in the app to see your day.",
        ["WidgetOutdated"] = "Open Daynote once to bring this back.",
        ["WidgetNewNote"] = "+ Note",
        ["WidgetNewTodo"] = "+ To-do",
        ["WidgetAddTodo"] = "Add a to-do",
        ["WidgetCompleteFormat"] = "Complete {0}",
        ["WidgetReopenFormat"] = "Reopen {0}",
        ["WidgetMoreFormat"] = "+{0} more",
        ["WidgetTomorrow"] = "Tomorrow",
        ["WidgetAllDay"] = "All day",
    };

    /// <summary>"노트 3개" / "3 notes", and "1 note" rather than "1 notes".</summary>
    public static string NoteCount(int count) =>
        LocalizationService.Instance.Language == AppLanguage.English && count == 1
            ? "1 note"
            : MobileStrings.Format("MobileNoteCountFormat", count.ToString(CultureInfo.CurrentCulture));

    /// <summary>"결과 3개" / "3 results", and "1 result".</summary>
    public static string ResultCount(int count) =>
        LocalizationService.Instance.Language == AppLanguage.English && count == 1
            ? "1 result"
            : MobileStrings.Format("MobileSearchResultCountFormat", count.ToString(CultureInfo.CurrentCulture));
}
