using System.Windows.Input;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Daynote.App.Settings;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the close button or Escape is used, so the shell can dismiss the overlay.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// While the shortcuts row is capturing, swallow every key here and forward the first real chord
    /// (a modifier plus a non-modifier key) to the view model; Escape cancels capture instead of closing.
    /// </summary>
    protected override void OnPreviewKeyDown(WpfKeyEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.IsCapturing)
        {
            e.Handled = true;
            WpfKey key = e.Key == WpfKey.System ? e.SystemKey : e.Key;
            if (key == WpfKey.Escape)
            {
                vm.CancelCapture();
            }
            else if (!IsModifierKey(key))
            {
                _ = vm.HandleCapturedChordAsync(Keyboard.Modifiers, key);
            }

            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnKeyDown(WpfKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == WpfKey.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private static bool IsModifierKey(WpfKey key) => key is
        WpfKey.LeftCtrl or WpfKey.RightCtrl or
        WpfKey.LeftAlt or WpfKey.RightAlt or
        WpfKey.LeftShift or WpfKey.RightShift or
        WpfKey.LWin or WpfKey.RWin or
        WpfKey.System or WpfKey.None;
}
