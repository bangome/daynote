using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using Daynote.Mobile.Android.Platform;

namespace Daynote.Mobile.Android;

/// <summary>
/// The Android <see cref="global::Android.App.Application"/>, which is where Avalonia 12 builds the app.
/// </summary>
/// <remarks>
/// Avalonia 11 put <c>CustomizeAppBuilder</c> on a generic activity; 12 moved it here, to a type
/// that outlives every activity. That suits this app: the platform services are built once, and the
/// two that need a live activity (the browser tab for sign-in, the top level for pickers) resolve
/// it through <see cref="MainActivity.Current"/> each time instead of capturing one.
/// </remarks>
[Application(Label = "Daynote", Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher_round",
             Theme = "@style/MyTheme.NoActionBar", AllowBackup = true, SupportsRtl = true)]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<App>(handle, ownership)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.Platform = AndroidPlatformServices.Create(this, () => MainActivity.Current);
        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .With(new FontManagerOptions { FontFallbacks = [KoreanFallback] });
    }

    /// <summary>
    /// A bundled Hangul face, tried before the system's.
    /// </summary>
    /// <remarks>
    /// Inter has no Hangul, so every Korean string falls back. On recent Android the system fallback
    /// is NotoSansCJK-Regular.ttc, a variable collection, and Skia draws its regular weight as boxes
    /// while the bold synthesis comes out fine — a Korean screen showed its headings and nothing
    /// else. Pretendard is static, pairs well with Inter, and makes the result independent of
    /// whatever the device ships.
    /// </remarks>
    private static readonly FontFallback KoreanFallback = new()
    {
        FontFamily = new Avalonia.Media.FontFamily("avares://Daynote.Mobile/Assets/Fonts#Pretendard"),
    };
}
