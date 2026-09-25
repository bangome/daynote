using UIKit;

namespace Daynote.Mobile.iOS;

/// <summary>The iOS entry point. UIKit takes over from here and calls into <see cref="AppDelegate"/>.</summary>
public static class Application
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
