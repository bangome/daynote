using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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
        AttachHighlight();
        AttachHeaderWrap();
        AttachFileDrop();
        // Tunnelling, because the text box takes Up and Down for its caret before a bubbling handler sees them.
        SearchBox.AddHandler(KeyDownEvent, OnPaletteKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (_shell is not null)
            {
                _shell.StickyNoteRequested -= OnStickyNoteRequested;
            }

            _shell = DataContext as DesktopShellViewModel;
            if (_shell is not null)
            {
                _shell.StickyNoteRequested += OnStickyNoteRequested;
                // "이름 변경" from a row's menu opens the editor through the view model; the caret has
                // to follow, the same as it does after a double-click on the heading.
                _shell.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(DesktopShellViewModel.IsRenamingTitle) && _shell.IsRenamingTitle)
                    {
                        FocusTitleEditor();
                    }
                    else if (args.PropertyName == nameof(DesktopShellViewModel.IsAddTodoOpen) && _shell.IsAddTodoOpen)
                    {
                        // The card's title takes the keys as it opens, as the phone sheet's does.
                        Dispatcher.UIThread.Post(() => AddTodoTitle.Focus(), DispatcherPriority.Loaded);
                    }
                };
            }

            AttachTutorialStickyDemo(_shell);
            AttachMotion(_shell);
            AttachPalette(_shell);
            AttachChrome(_shell);

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
        if (_shell is null)
        {
            return;
        }

        // The palette's chord is fixed, like the design's: ⌘K on the Mac, Ctrl+K elsewhere. Both are
        // bound everywhere so a PC keyboard on a Mac (and the reverse) still finds it.
        KeyBindings.Add(new KeyBinding { Command = _shell.OpenPaletteCommand, Gesture = new KeyGesture(Key.K, KeyModifiers.Meta) });
        KeyBindings.Add(new KeyBinding { Command = _shell.OpenPaletteCommand, Gesture = new KeyGesture(Key.K, KeyModifiers.Control) });

        if (_shortcuts is null)
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
            DismissOverlayOnEscape(e);
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

    /// <summary>
    /// Escape closes whatever is on top: the palette, then the add-to-do card, then the checkout, then
    /// the account card, then settings — the order the design checks them in. The palette's own query box handles it first when focused.
    /// </summary>
    private void DismissOverlayOnEscape(KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _shell is not { } shell)
        {
            return;
        }

        if (shell.IsPaletteOpen)
        {
            shell.ClosePaletteCommand.Execute(null);
        }
        else if (shell.IsAddTodoOpen)
        {
            shell.CloseAddTodoCommand.Execute(null);
        }
        else if (shell.Account is { IsCheckoutOpen: true } account)
        {
            account.CloseCheckoutCommand.Execute(null);
        }
        else if (shell.IsAccountOpen)
        {
            shell.CloseAccountCommand.Execute(null);
        }
        else if (shell.IsSettingsOpen)
        {
            shell.CloseSettingsCommand.Execute(null);
        }
        else
        {
            return;
        }

        e.Handled = true;
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyPlatformChrome();
        UpdateMaximizeGlyph();
        PlacePalette();
    }

    protected override void OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PlacePalette();
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizeGlyph();
        }
    }

    private void OnMinimize(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        foreach (StickyNoteWindow sticky in _stickyNotes.ToArray())
        {
            sticky.Close();
        }

        base.OnClosed(e);
    }

    /// <summary>The heading reads as the field it is in the design: one click puts the caret in it.</summary>
    private void OnTitleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => OnTitleDoubleTapped(sender, e);

    private void OnTitleDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is DesktopShellViewModel shell && shell.HasOpenNote)
        {
            shell.BeginRenameTitle();
        }
    }

    /// <summary>
    /// Puts the caret in the title editor once it is visible. The IsVisible binding lands on the
    /// next layout pass, so focusing synchronously would hit a collapsed control and do nothing.
    /// </summary>
    internal void FocusTitleEditor()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            TitleEditor.Focus();
            TitleEditor.SelectAll();
        });
    }

    private void OnTitleEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DesktopShellViewModel shell)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            shell.CommitRenameTitleCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            shell.CancelRenameTitleCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnTitleEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is DesktopShellViewModel shell)
        {
            shell.CommitRenameTitleCommand.Execute(null);
        }
    }

    /// <summary>
    /// A file card asks to be saved. The command lives on the card's own view model, which is the
    /// DataContext of the Border the gesture landed on.
    /// </summary>
    private void OnFileCardDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control { DataContext: Daynote.App.Shell.Product.FileItemViewModel file })
        {
            file.SaveCommand.Execute(null);
            e.Handled = true;
        }
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
