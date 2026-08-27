using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Daynote.Core.Files;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Editor card: a transparent-foreground <see cref="TextBox"/> over a synchronized highlight
/// <see cref="TextBlock"/> that renders <c>-[]</c>/<c>-[x]</c> markers and <c>(M/D H:mm)</c> due stamps in
/// accent semibold (calendar-notes.dc.html highlightBody). Both layers share font, size, padding, and wrap
/// so the caret sits over its glyph; scrolling is mirrored. Title and tag edits commit through the shell.
/// </summary>
public partial class EditorCardView : System.Windows.Controls.UserControl
{
    private static readonly Regex HighlightPattern = new(
        @"(-\s?\[(?: |x|X)?\])|(\(\d{1,2}/\d{1,2}(?:\s+\d{1,2}:\d{2})?\))|(\[\[file:[^\]\r\n]+\]\])|("
            + UrlLinkSyntax.PatternText + @")|((?<![\p{L}\p{N}_])#[\p{L}\p{N}_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public EditorCardView()
    {
        InitializeComponent();
        BodyBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnBodyScroll));
        HighlightScroll.ScrollChanged += OnHighlightScroll;

        // File-link plumbing: paste stores images/files as day files and inserts a [[file:…]] marker;
        // dropping a 파일-tab card (or Explorer files) does the same; clicking a marker reveals the file.
        System.Windows.DataObject.AddPastingHandler(BodyBox, OnBodyPaste);
        BodyBox.AllowDrop = true;
        BodyBox.PreviewDragOver += OnBodyDragOver;
        BodyBox.PreviewDrop += OnBodyDrop;
        BodyBox.PreviewMouseLeftButtonUp += OnBodyMouseUp;

        // Tag-panel jumps ask the shell to select a body span; follow the DataContext so the editor
        // stays subscribed to the live shell and never leaks a handler.
        DataContextChanged += OnShellDataContextChanged;
        Unloaded += OnEditorUnloaded;
    }

    private ProductShellViewModel? _subscribedShell;

    /// <summary>Raised when the user opens the post-it; the shell supplies the live buffer, so no snapshot travels with the event.</summary>
    public event EventHandler? StickyNoteRequested;

    private ProductShellViewModel? Shell => DataContext as ProductShellViewModel;

    private void OnBodyTextChanged(object sender, TextChangedEventArgs e)
    {
        string text = BodyBox.Text;
        RebuildHighlight(text);
        MetaLeft.Text = string.Format(
            CultureInfo.CurrentCulture,
            Localization.AppStrings.NoteMetaFormat,
            text.Length,
            text.Length == 0 ? 1 : text.Count(c => c == '\n') + 1);
    }

    private void RebuildHighlight(string text)
    {
        Highlight.Inlines.Clear();
        var accent = (System.Windows.Media.Brush?)TryFindResource("Daynote.Product.Brush.Accent")
            ?? System.Windows.Media.Brushes.RoyalBlue;
        int last = 0;
        foreach (Match match in HighlightPattern.Matches(text))
        {
            if (match.Index > last)
            {
                Highlight.Inlines.Add(new Run(text[last..match.Index]));
            }

            var run = new Run(match.Value) { Foreground = accent, FontWeight = FontWeights.SemiBold };
            if (match.Value.StartsWith('#'))
            {
                // Inline tags read as chips: keep the accent text but add a soft chip background.
                if (TryFindResource("Daynote.Product.Brush.AccentSoft") is System.Windows.Media.Brush chip)
                {
                    run.Background = chip;
                }
            }
            else if (match.Value.StartsWith("[[file:", StringComparison.Ordinal)
                || match.Value.StartsWith("http", StringComparison.Ordinal))
            {
                run.TextDecorations = TextDecorations.Underline;
            }

            Highlight.Inlines.Add(run);
            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            Highlight.Inlines.Add(new Run(text[last..]));
        }

        // Trailing run keeps the highlight height in step with the editor's final empty line.
        Highlight.Inlines.Add(new Run("​"));
    }

    private void OnShellDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedShell is not null)
        {
            _subscribedShell.EditorSelectRequested -= OnEditorSelectRequested;
        }

        _subscribedShell = e.NewValue as ProductShellViewModel;
        if (_subscribedShell is not null)
        {
            _subscribedShell.EditorSelectRequested += OnEditorSelectRequested;
        }
    }

    private void OnEditorUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribedShell is not null)
        {
            _subscribedShell.EditorSelectRequested -= OnEditorSelectRequested;
            _subscribedShell = null;
        }
    }

    /// <summary>Selects and scrolls to a body span; deferred to Background so freshly-loaded text has propagated.</summary>
    private void OnEditorSelectRequested(int start, int length)
    {
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            int s = Math.Clamp(start, 0, BodyBox.Text.Length);
            int len = Math.Clamp(length, 0, BodyBox.Text.Length - s);
            BodyBox.Focus();
            BodyBox.Select(s, len);
            BodyBox.ScrollToLine(BodyBox.GetLineIndexFromCharacterIndex(s));
        });
    }

    private void OnBodyScroll(object sender, ScrollChangedEventArgs e)
    {
        _ = e;
        SyncHighlightOffset();
    }

    /// <summary>
    /// Re-mirrors the offset after the highlight layer's own extent changes.
    /// </summary>
    /// <remarks>
    /// <c>ScrollToVerticalOffset</c> clamps to the <c>ScrollableHeight</c> the target has at that
    /// instant, and says nothing when it does. The highlight layer's content is replaced on the same
    /// TextChanged that precedes the editor's ScrollChanged, so a mirror can land while its extent is
    /// still the shorter, pre-edit one: the offset is silently clamped, no further ScrollChanged
    /// arrives to correct it, and the glyphs stay parked above the caret. Typing then lands a line or
    /// more away from where the caret is drawn. Reissuing whenever the extent changes closes that
    /// window; a plain offset change carries no extent delta, so this cannot recurse.
    /// </remarks>
    private void OnHighlightScroll(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 || e.ExtentWidthChange != 0)
        {
            SyncHighlightOffset();
        }
    }

    /// <summary>Aligns the glyph layer with the editor, reading the editor's live offset rather than a cached one.</summary>
    private void SyncHighlightOffset()
    {
        HighlightScroll.ScrollToVerticalOffset(BodyBox.VerticalOffset);
        HighlightScroll.ScrollToHorizontalOffset(BodyBox.HorizontalOffset);
    }

    private void OnTitleLostFocus(object sender, RoutedEventArgs e)
    {
        if (Shell?.Notes.SelectedTab is { } tab)
        {
            _ = Shell.Notes.RenameAsync(tab, TitleBox.Text);
        }
    }

    private void OnOpenStickyNote(object sender, RoutedEventArgs e)
    {
        if (Shell?.Notes.SelectedTab is not { IsProjection: false })
        {
            return;
        }

        StickyNoteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnTagKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfKey.Enter && Shell is { } shell && shell.CommitTagCommand.CanExecute(null))
        {
            shell.CommitTagCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── File links: paste / drop store into the 파일 tab and leave only a [[file:…]] marker ──

    private void OnBodyPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (Shell is not { } shell)
        {
            return;
        }

        // A copied Explorer file carries FileDrop (often alongside a bitmap preview) — prefer the file.
        if (e.DataObject.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var paths = (string[])e.DataObject.GetData(System.Windows.DataFormats.FileDrop);
            e.CancelCommand();
            _ = StoreFilesAndInsertLinksAsync(shell, paths, BodyBox.SelectionStart);
            return;
        }

        // Office apps (PowerPoint/Excel/Word) put a bitmap RENDERING on the clipboard even for a plain
        // text copy. Any text representation wins: only a text-less clipboard (screenshot, copied
        // picture) is treated as an image.
        bool hasText = e.DataObject.GetDataPresent(System.Windows.DataFormats.UnicodeText)
            || e.DataObject.GetDataPresent(System.Windows.DataFormats.Text);
        if (!hasText
            && e.DataObject.GetDataPresent(System.Windows.DataFormats.Bitmap)
            && e.DataObject.GetData(System.Windows.DataFormats.Bitmap, autoConvert: true) is BitmapSource bitmap)
        {
            e.CancelCommand();
            _ = StoreImageAndInsertLinkAsync(shell, bitmap, BodyBox.SelectionStart);
        }
    }

    private void OnBodyDragOver(object sender, WpfDragEventArgs e)
    {
        if (e.Data.GetDataPresent(FileLinkSyntax.DragFormat) || e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnBodyDrop(object sender, WpfDragEventArgs e)
    {
        if (Shell is not { } shell)
        {
            return;
        }

        int at = BodyBox.GetCharacterIndexFromPoint(e.GetPosition(BodyBox), snapToText: true);
        if (at < 0)
        {
            at = BodyBox.Text.Length;
        }

        if (e.Data.GetDataPresent(FileLinkSyntax.DragFormat) && e.Data.GetData(FileLinkSyntax.DragFormat) is string name)
        {
            e.Handled = true;
            InsertText(FileLinkSyntax.BuildMarker(name), at);
        }
        else if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Handled = true;
            _ = StoreFilesAndInsertLinksAsync(shell, (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop), at);
        }
    }

    /// <summary>
    /// A plain click (no selection drag) on a [[file:…]] marker reveals that file in the 파일 tab; a click
    /// on a bare <c>http(s)://…</c> URL opens it in the default browser.
    /// </summary>
    private void OnBodyMouseUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (Shell is not { } shell || BodyBox.SelectionLength > 0)
        {
            return;
        }

        int index = BodyBox.GetCharacterIndexFromPoint(e.GetPosition(BodyBox), snapToText: false);
        if (index < 0)
        {
            return;
        }

        if (FileLinkSyntax.TryGetLinkAt(BodyBox.Text, index, out string name))
        {
            shell.RevealFile(name);
        }
        else if (UrlLinkSyntax.TryGetUrlAt(BodyBox.Text, index, out string url))
        {
            OpenInBrowser(url);
        }
    }

    /// <summary>Opens a URL through the shell so the OS launches the user's default browser.</summary>
    private static void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // Best-effort: a malformed URL or a machine with no registered browser just does nothing.
        }
    }

    private async Task StoreImageAndInsertLinkAsync(ProductShellViewModel shell, BitmapSource bitmap, int insertAt)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        memory.Position = 0;

        string name = string.Create(
            CultureInfo.InvariantCulture,
            $"{Localization.AppStrings.PastedImagePrefix} {DateTime.Now:yyyy-MM-dd HHmmss}.png");
        DayFile? stored = await shell.Files.AddFromStreamAsync(name, memory);
        if (stored is not null)
        {
            InsertText(FileLinkSyntax.BuildMarker(stored.DisplayName), insertAt);
        }
    }

    private async Task StoreFilesAndInsertLinksAsync(ProductShellViewModel shell, IReadOnlyList<string> paths, int insertAt)
    {
        var markers = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                await using FileStream stream = File.OpenRead(path);
                DayFile? stored = await shell.Files.AddFromStreamAsync(Path.GetFileName(path), stream);
                if (stored is not null)
                {
                    markers.Add(FileLinkSyntax.BuildMarker(stored.DisplayName));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Skip an unreadable file; the rest still import (same policy as the picker path).
            }
        }

        if (markers.Count > 0)
        {
            InsertText(string.Join("\n", markers), insertAt);
        }
    }

    /// <summary>Inserts at the captured index (clamped — the buffer may have changed while storing).</summary>
    private void InsertText(string text, int at)
    {
        int index = Math.Clamp(at, 0, BodyBox.Text.Length);
        BodyBox.Select(index, 0);
        BodyBox.SelectedText = text;
        BodyBox.Select(index + text.Length, 0);
        BodyBox.Focus();
    }
}
