using Daynote.Core.Settings;

namespace Daynote.App.Localization;

/// <summary>
/// Resolves the UI language during startup, before any window is created.
/// </summary>
/// <remarks>
/// Order matters here: view model constructors read <see cref="AppStrings"/> and XAML resolves its
/// <c>{loc:Tr}</c> bindings as each window loads, so the language has to be settled before the shell
/// is built. Doing it later would leave the first frame in the wrong language.
/// </remarks>
public static class LanguageStartup
{
    /// <summary>
    /// The language this profile should open in: the persisted choice if the user has made one,
    /// otherwise the Windows display language.
    /// </summary>
    public static async ValueTask<AppLanguage> ResolveAsync(
        ISettingsStore settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? tag = await settings.GetAsync(UiSettings.LanguageKey, cancellationToken).ConfigureAwait(false);
        return AppLanguages.FromTag(tag) ?? AppLanguages.FromSystem();
    }

    /// <summary>Resolves and applies the startup language to <see cref="LocalizationService"/>.</summary>
    public static async ValueTask ApplyAsync(
        ISettingsStore settings,
        CancellationToken cancellationToken = default)
    {
        AppLanguage language = await ResolveAsync(settings, cancellationToken).ConfigureAwait(false);
        LocalizationService.Instance.SetLanguage(language);

        // SetLanguage is a no-op when the resolved language already matches the initial Korean
        // default, so apply the culture unconditionally — otherwise a Korean profile would keep
        // whatever culture the OS handed the thread.
        LocalizationService.Instance.ApplyCulture();
    }
}
