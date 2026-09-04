using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Account;
using Daynote.App.Input;
using Daynote.App.Lifecycle;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The Calendar Notes product window: a custom <c>WindowChrome</c> shell hosting the calendar, notes, and
/// right panel. The caption buttons minimize/maximize/restore/close; close hides to the tray (the process
/// and clipboard listener stay alive) exactly as the legacy shell did.
/// </summary>
public partial class ProductWindow : Window, IWindowHost, IAccountHost
{
    private AppLifecycleCoordinator? _lifecycle;
    private IGlobalHotkeyService? _hotkeys;
    private ConfigurableShortcuts? _shortcuts;
    private readonly List<StickyNoteWindow> _stickyNoteWindows = [];

    public ProductWindow(ProductShellViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _openStickyCommand = new RelayCommand(() => OpenStickyNote());
        InitializeComponent();
        DataContext = viewModel;
        SettingsHost.CloseRequested += OnSettingsCloseRequested;
        viewModel.PanelUserToggled += OnPanelUserToggled;
    }

    // A collapsed panel disappears entirely (no strip), freeing its full width plus its 10-DIP gap
    // (see the XAML column definitions: left 290+10, right 300+10).
    private const double LeftPanelWidthDelta = 290 + 10;
    private const double RightPanelWidthDelta = 300 + 10;

    /// <summary>
    /// A user panel toggle resizes the WINDOW by the freed/needed width so the editor column stays the
    /// same size; only an explicit user window resize changes the editor. Skipped when maximized (the
    /// window cannot shed width there) and for width-driven auto-collapse (no event is raised for it).
    /// </summary>
    private void OnPanelUserToggled(bool isLeft, bool nowCollapsed)
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        double delta = isLeft ? LeftPanelWidthDelta : RightPanelWidthDelta;
        double desired = nowCollapsed ? Width - delta : Width + delta;
        double newWidth = Math.Max(MinWidth, desired);
        double appliedDelta = newWidth - Width;

        // The left panel sits to the LEFT of the editor, so shedding/adding its width from the window's
        // LEFT edge (moving the window) keeps the window's right edge — and thus the editor's screen
        // position — fixed. The right panel is to the editor's right, so a plain right-edge resize (Left
        // unchanged) already leaves the editor in place.
        if (isLeft)
        {
            Left -= appliedDelta;
        }

        Width = newWidth;
    }

    public ProductShellViewModel ViewModel { get; }

    /// <summary>Set true only for an explicit Quit so the close is a real close, not hide-to-tray.</summary>
    public bool IsClosingToShutdown { get; set; }

    public void AttachLifecycle(AppLifecycleCoordinator coordinator) => _lifecycle = coordinator;

    /// <summary>
    /// Binds the global summon hotkey service to this window's handle. Registered here because the
    /// handle survives hide-to-tray (the window is only <c>Hide()</c>n), so the chord keeps firing while
    /// the app is in the tray. Called before <c>Show()</c>; if the handle already exists, attaches now.
    /// </summary>
    public void AttachHotkeys(IGlobalHotkeyService hotkeys)
    {
        _hotkeys = hotkeys;
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle != 0)
        {
            _hotkeys.Attach(handle);
        }
    }

    /// <summary>
    /// Binds the configurable in-app shortcuts: builds the window's <see cref="System.Windows.Input.KeyBinding"/>s
    /// from the current gesture set and rebuilds them whenever the user reassigns one.
    /// </summary>
    public void AttachShortcuts(ConfigurableShortcuts shortcuts)
    {
        _shortcuts = shortcuts;
        _shortcuts.Changed += (_, _) => RebuildShortcutBindings();
        RebuildShortcutBindings();
    }

    private void RebuildShortcutBindings()
    {
        if (_shortcuts is null)
        {
            return;
        }

        InputBindings.Clear();
        foreach (AppShortcutAction action in _shortcuts.Actions)
        {
            if (CommandFor(action.Id) is not { } command)
            {
                continue;
            }

            Hotkey hotkey = _shortcuts.Get(action.Id);
            InputBindings.Add(new System.Windows.Input.KeyBinding(command, new System.Windows.Input.KeyGesture(hotkey.Key, hotkey.Modifiers)));
        }
    }

    private System.Windows.Input.ICommand? CommandFor(string actionId) => actionId switch
    {
        AppShortcuts.NewNote => ViewModel.NewNoteCommand,
        AppShortcuts.GoToday => ViewModel.GoToTodayCommand,
        AppShortcuts.Settings => ViewModel.ToggleSettingsCommand,
        AppShortcuts.ToggleTheme => ViewModel.ToggleThemeCommand,
        AppShortcuts.ToggleLeft => ViewModel.ToggleLeftCommand,
        AppShortcuts.ToggleRight => ViewModel.ToggleRightCommand,
        AppShortcuts.OpenSticky => _openStickyCommand,
        _ => null,
    };

    // Window-owned (not a VM command) because opening a sticky creates a window; mirrors the editor's
    // post-it button, so the configurable "포스트잇으로 띄우기" shortcut pops the current note out.
    private readonly System.Windows.Input.ICommand _openStickyCommand;

    void IWindowHost.HideToTray() => Hide();

    void IWindowHost.ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
    }

    void IWindowHost.ShowSettings()
    {
        ((IWindowHost)this).ShowAndActivate();
        ViewModel.OpenSettings();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // WindowStyle=None drops the OS corner rounding; ask DWM for the Windows 11 rounded
        // corner preference. On Windows 10 the attribute is unsupported and the call is a no-op.
        nint hwnd = new WindowInteropHelper(this).Handle;
        int preference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeCornerPreference, ref preference, sizeof(int));

        // The handle now exists and outlives hide-to-tray, so register the summon hotkey against it.
        _hotkeys?.Attach(hwnd);

        // WindowStyle=None also means the OS maximizes us over the taskbar (ProductWindow.Maximize.cs).
        AttachMaximizeFix(hwnd);
    }

    private const int DwmWindowAttributeCornerPreference = 33;
    private const int DwmWindowCornerPreferenceRound = 2;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (IsClosingToShutdown)
        {
            CloseStickyNoteWindows();
            return;
        }

        if (_lifecycle is not null && !IsClosingToShutdown)
        {
            e.Cancel = true;
            _lifecycle.HideToTray();
        }
    }

    private void OnSettingsCloseRequested(object? sender, EventArgs e) => ViewModel.CloseSettings();

    /// <summary>
    /// Shows the account window, or brings the open one forward. One instance: it binds the shell's
    /// single <see cref="AccountViewModel"/>, and a second copy would show the same checkout button
    /// twice. Opened from two places — the titlebar avatar and the row in settings.
    /// </summary>
    public void ShowAccountWindow()
    {
        // Null in a build with no sync endpoint configured: the feature is absent, not disabled.
        if (ViewModel.Account is null)
        {
            return;
        }

        if (_accountWindow is { IsLoaded: true })
        {
            _accountWindow.Activate();
            return;
        }

        _accountWindow = new AccountWindow(ViewModel.Account) { Owner = this };
        _accountWindow.Closed += (_, _) => _accountWindow = null;
        _accountWindow.Show();
    }

    public void ShowSettingsPanel() => ViewModel.OpenSettings();

    private AccountWindow? _accountWindow;

    private void OnStickyNoteRequested(object? sender, EventArgs e) => OpenStickyNote();

    private StickyNoteWindow OpenStickyNote()
    {
        var stickyNote = new StickyNoteWindow(ViewModel);
        _stickyNoteWindows.Add(stickyNote);
        stickyNote.Closed += OnStickyNoteClosed;
        stickyNote.Show();
        return stickyNote;
    }

    /// <summary>
    /// The Alt+` quick-note flow: jump to today, create a note there, and open it as a post-it — the
    /// post-it is an independent window, so this works while the main window is hidden to the tray.
    /// </summary>
    public async Task OpenQuickStickyNoteAsync()
    {
        await ViewModel.GoToTodayCommand.ExecuteAsync(null);
        await ViewModel.NewNoteCommand.ExecuteAsync(null);
        StickyNoteWindow stickyNote = OpenStickyNote();
        stickyNote.Activate();
    }

    private void OnStickyNoteClosed(object? sender, EventArgs e)
    {
        if (sender is StickyNoteWindow stickyNote)
        {
            stickyNote.Closed -= OnStickyNoteClosed;
            _stickyNoteWindows.Remove(stickyNote);
        }
    }

    private void CloseStickyNoteWindows()
    {
        while (_stickyNoteWindows.Count > 0)
        {
            StickyNoteWindow stickyNote = _stickyNoteWindows[^1];
            _stickyNoteWindows.RemoveAt(_stickyNoteWindows.Count - 1);
            stickyNote.Closed -= OnStickyNoteClosed;
            stickyNote.Close();
        }
    }

    // ── 파일-tab card → editor drag: start a drag once the pointer moves past the OS drag threshold,
    //    so plain clicks (e.g. the card's Delete button) never turn into accidental drags. ──

    private System.Windows.Point _fileCardDragOrigin;

    private void OnFileCardMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _fileCardDragOrigin = e.GetPosition(null);

    private void OnFileCardMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        Vector delta = e.GetPosition(null) - _fileCardDragOrigin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: FileItemViewModel item } element)
        {
            _ = DragDrop.DoDragDrop(
                element,
                new System.Windows.DataObject(FileLinkSyntax.DragFormat, item.Name),
                System.Windows.DragDropEffects.Copy);
        }
    }

    // ── Explorer → 파일-tab drop: importing here stores the files WITHOUT inserting body links
    //    (dropping on the editor body is the link-inserting path). ──

    private void OnFilesPanelDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnFilesPanelDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            && e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
        {
            e.Handled = true;
            _ = ImportDroppedFilesAsync(paths);
        }
    }

    private async Task ImportDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                await using var stream = System.IO.File.OpenRead(path);
                _ = await ViewModel.Files.AddFromStreamAsync(System.IO.Path.GetFileName(path), stream);
            }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
            {
                // Skip an unreadable file; the rest still import (same policy as the picker path).
            }
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => ViewModel.UpdateWidth(e.NewSize.Width);

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
