using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Daynote.App.Lifecycle;
using WpfInputElement = System.Windows.IInputElement;
using WpfKey = System.Windows.Input.Key;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfKeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;

namespace Daynote.App.Shell;

public partial class MainWindow : Window, IWindowHost
{
    private WpfInputElement? _searchInvoker;
    private AppLifecycleCoordinator? _lifecycle;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Search.PropertyChanged += OnSearchPropertyChanged;
        SettingsHost.CloseRequested += OnSettingsCloseRequested;
        Loaded += OnLoaded;
    }

    public MainWindowViewModel ViewModel { get; }

    /// <summary>Set true only for an explicit Quit so the close is a real close, not hide-to-tray.</summary>
    public bool IsClosingToShutdown { get; set; }

    /// <summary>Attaches the lifecycle coordinator so the window close hides to the tray.</summary>
    public void AttachLifecycle(AppLifecycleCoordinator coordinator) => _lifecycle = coordinator;

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

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_lifecycle is not null && !IsClosingToShutdown)
        {
            // X hides to the tray; the process and clipboard listener stay alive (DESIGN Section 1).
            e.Cancel = true;
            _lifecycle.HideToTray();
        }
    }

    private void OnSettingsCloseRequested(object? sender, EventArgs e) => ViewModel.CloseSettings();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySidebarWidth();
        ViewModel.UpdateEffectiveWidth(ActualWidth);
    }

    private void ApplySidebarWidth()
    {
        if (TryFindResource("Daynote.Size.Sidebar.Default") is double width)
        {
            SidebarColumn.Width = new GridLength(width);
        }

        if (TryFindResource("Daynote.Size.Sidebar.Min") is double min)
        {
            SidebarColumn.MinWidth = min;
        }

        if (TryFindResource("Daynote.Size.Sidebar.Max") is double max)
        {
            SidebarColumn.MaxWidth = max;
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) =>
        ViewModel.UpdateEffectiveWidth(e.NewSize.Width);

    protected override void OnPreviewKeyDown(WpfKeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == WpfKey.F && (WpfKeyboard.Modifiers & WpfModifierKeys.Control) == WpfModifierKeys.Control)
        {
            _searchInvoker = WpfKeyboard.FocusedElement;
            ViewModel.Search.Open();
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnSearchBoxFocused(object sender, WpfKeyboardFocusChangedEventArgs e)
    {
        if (e.OldFocus is not null && !ReferenceEquals(e.OldFocus, SearchBox))
        {
            _searchInvoker = e.OldFocus;
        }

        ViewModel.Search.Open();
    }

    private void OnSearchBoxKeyDown(object sender, WpfKeyEventArgs e)
    {
        switch (e.Key)
        {
            case WpfKey.Escape:
                if (ViewModel.Search.Query.Length > 0)
                {
                    ViewModel.Search.ClearQuery();
                }
                else
                {
                    ViewModel.Search.Close();
                }

                e.Handled = true;
                break;
            case WpfKey.Down when ViewModel.Search.HasResults:
                SearchOverlayHost.FocusFirstResult();
                e.Handled = true;
                break;
        }
    }

    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.Search.IsOpen) && !ViewModel.Search.IsOpen)
        {
            (_searchInvoker ?? this).Focus();
            _searchInvoker = null;
        }
    }
}
