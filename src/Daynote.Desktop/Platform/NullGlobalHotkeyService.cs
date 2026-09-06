using Daynote.App.Input;

namespace Daynote.Desktop.Platform;

/// <summary>For operating systems without a global-hotkey integration yet: accepts nothing, never fires.</summary>
public sealed class NullGlobalHotkeyService : IGlobalHotkeyService
{
    public event EventHandler? Pressed
    {
        add { }
        remove { }
    }

    public event EventHandler? QuickNotePressed
    {
        add { }
        remove { }
    }

    public Hotkey? Current => null;

    public void Attach(nint hwnd)
    {
    }

    public HotkeySetResult TrySet(Hotkey hotkey) => HotkeySetResult.Invalid;

    public void Dispose()
    {
    }
}
