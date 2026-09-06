using Avalonia.Controls;
using Avalonia.Input;
using Daynote.App.Input;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly List<StickyNoteWindow> _stickyNotes = [];
    private ConfigurableShortcuts? _shortcuts;
    private DesktopShellViewModel? _shell;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_shell is not null)
            {
                _shell.EditorSelectRequested -= OnEditorSelectRequested;
                _shell.StickyNoteRequested -= OnStickyNoteRequested;
            }

            _shell = DataContext as DesktopShellViewModel;
            if (_shell is not null)
            {
                _shell.EditorSelectRequested += OnEditorSelectRequested;
                _shell.StickyNoteRequested += OnStickyNoteRequested;
            }

            RebuildShortcutBindings();
        };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Binds the configurable in-app shortcuts and rebuilds them whenever one is reassigned.</summary>
    public void AttachShortcuts(ConfigurableShortcuts shortcuts)
    {
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _shortcuts.Changed += (_, _) => RebuildShortcutBindings();
        RebuildShortcutBindings();
    }

    private void RebuildShortcutBindings()
    {
        KeyBindings.Clear();
        if (_shortcuts is null || _shell is null)
        {
            return;
        }

        foreach (AppShortcutAction action in _shortcuts.Actions)
        {
            if (CommandFor(action.Id, _shell) is not { } command)
            {
                continue;
            }

            Hotkey hotkey = _shortcuts.Get(action.Id);
            KeyBindings.Add(new KeyBinding
            {
                Command = command,
                Gesture = new KeyGesture((Key)hotkey.Key, (KeyModifiers)hotkey.Modifiers),
            });
        }
    }

    private static System.Windows.Input.ICommand? CommandFor(string actionId, DesktopShellViewModel shell) => actionId switch
    {
        AppShortcuts.NewNote => shell.NewNoteCommand,
        AppShortcuts.GoToday => shell.GoToTodayCommand,
        AppShortcuts.Settings => shell.ToggleSettingsCommand,
        AppShortcuts.ToggleTheme => shell.ToggleThemeCommand,
        AppShortcuts.ToggleLeft => shell.ToggleLeftCommand,
        AppShortcuts.ToggleRight => shell.ToggleRightCommand,
        AppShortcuts.OpenSticky => shell.OpenStickyCommand,
        _ => null,
    };

    /// <summary>
    /// While the settings panel is capturing a chord, the next real key press becomes the chord instead
    /// of reaching the focused control; Escape cancels. Bare modifiers are ignored until a key joins them.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell?.SettingsViewModel is not { IsCapturing: true } settings)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            settings.CancelCapture();
            return;
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None)
        {
            return;
        }

        _ = settings.HandleCapturedChordAsync((HotkeyModifiers)e.KeyModifiers, (HotkeyKey)e.Key);
    }

    private void OnEditorSelectRequested(int start, int length)
    {
        Editor.Focus();
        Editor.SelectionStart = start;
        Editor.SelectionEnd = start + length;
    }

    private void OnStickyNoteRequested(object? sender, EventArgs e)
    {
        var sticky = new StickyNoteWindow { DataContext = _shell };
        sticky.Closed += (s, _) =>
        {
            if (s is StickyNoteWindow closed)
            {
                _stickyNotes.Remove(closed);
            }
        };
        _stickyNotes.Add(sticky);
        sticky.Show();
        sticky.Activate();
        sticky.FocusBody();
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (StickyNoteWindow sticky in _stickyNotes.ToArray())
        {
            sticky.Close();
        }

        base.OnClosed(e);
    }

    private void OnTagBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is DesktopShellViewModel shell)
        {
            shell.CommitTagCommand.Execute(null);
            e.Handled = true;
        }
    }
}
