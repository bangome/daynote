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
        ["MobileInsertTime"] = "시간",
        ["MobileSearchPlaceholder"] = "노트, 태그, 날짜",
        ["MobileSearchClear"] = "지우기",
        ["MobileSearchRecent"] = "최근",
        ["MobileSearchByTag"] = "태그로 찾기",
        ["MobileSearchResultCountFormat"] = "결과 {0}개",
        ["MobileTodoOverdue"] = "지남",
        ["MobileTodoToday"] = "오늘",
        ["MobileTodoUpcoming"] = "예정",
        ["MobileTodoNoDate"] = "날짜 없음",
        ["MobileTodoDone"] = "완료",
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
        ["MobileInsertTime"] = "Time",
        ["MobileSearchPlaceholder"] = "Notes, tags, dates",
        ["MobileSearchClear"] = "Clear",
        ["MobileSearchRecent"] = "Recent",
        ["MobileSearchByTag"] = "Find by tag",
        ["MobileSearchResultCountFormat"] = "{0} results",
        ["MobileTodoOverdue"] = "Overdue",
        ["MobileTodoToday"] = "Today",
        ["MobileTodoUpcoming"] = "Upcoming",
        ["MobileTodoNoDate"] = "No date",
        ["MobileTodoDone"] = "Done",
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
