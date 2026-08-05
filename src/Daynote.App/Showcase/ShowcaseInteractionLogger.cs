using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyEventHandler = System.Windows.Input.KeyEventHandler;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Daynote.App.Showcase;

internal sealed class ShowcaseInteractionLogger : IDisposable
{
    private const string EvidencePage = "wide.markdown-editor.focus";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly FrameworkElement _root;
    private readonly StreamWriter _writer;
    private readonly TextCompositionEventHandler _startHandler;
    private readonly TextCompositionEventHandler _updateHandler;
    private readonly TextCompositionEventHandler _inputHandler;
    private readonly WpfKeyEventHandler _keyHandler;
    private bool _composing;
    private bool _disposed;
    private int _sequence;

    internal ShowcaseInteractionLogger(FrameworkElement root, string path)
    {
        _root = root;
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _writer = new StreamWriter(
            new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
        _startHandler = (_, eventArgs) => OnComposition("preview-text-input-start", eventArgs);
        _updateHandler = (_, eventArgs) => OnComposition("preview-text-input-update", eventArgs);
        _inputHandler = (_, eventArgs) => OnComposition("preview-text-input", eventArgs);
        _keyHandler = OnPreviewKeyDown;
        root.AddHandler(TextCompositionManager.PreviewTextInputStartEvent, _startHandler, handledEventsToo: true);
        root.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent, _updateHandler, handledEventsToo: true);
        root.AddHandler(TextCompositionManager.PreviewTextInputEvent, _inputHandler, handledEventsToo: true);
        root.AddHandler(Keyboard.PreviewKeyDownEvent, _keyHandler, handledEventsToo: true);
    }

    internal static ShowcaseInteractionLogger? TryAttachFromProcessArguments(FrameworkElement root)
    {
        var arguments = Environment.GetCommandLineArgs();
        var path = ValueAfter(arguments, "--interaction-log");
        if (path is null)
            return null;
        if (!arguments.Contains("--showcase", StringComparer.Ordinal) ||
            !arguments.Contains("--hold", StringComparer.Ordinal) ||
            arguments.Contains("--capture-all", StringComparer.Ordinal) ||
            arguments.Contains("--list", StringComparer.Ordinal) ||
            ValueAfterEither(arguments, "--page", "--state") != EvidencePage ||
            ValueAfter(arguments, "--stress") != "cjk")
            throw new InvalidOperationException("Interaction logging is restricted to the exact held CJK editor fixture.");
        return new ShowcaseInteractionLogger(root, path);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _root.RemoveHandler(TextCompositionManager.PreviewTextInputStartEvent, _startHandler);
        _root.RemoveHandler(TextCompositionManager.PreviewTextInputUpdateEvent, _updateHandler);
        _root.RemoveHandler(TextCompositionManager.PreviewTextInputEvent, _inputHandler);
        _root.RemoveHandler(Keyboard.PreviewKeyDownEvent, _keyHandler);
        _writer.Dispose();
    }

    private void OnComposition(string eventKind, TextCompositionEventArgs eventArgs)
    {
        if (eventKind == "preview-text-input-start")
            _composing = true;
        Write(eventKind, "route", eventArgs.OriginalSource, eventArgs.TextComposition, null);
        _root.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (eventKind == "preview-text-input")
                _composing = false;
            Write(eventKind, "post-route", eventArgs.OriginalSource, eventArgs.TextComposition, null);
        });
    }

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs eventArgs)
    {
        Write("preview-key-down", "route", eventArgs.OriginalSource, null, eventArgs.Key.ToString());
        _root.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (eventArgs.Key == Key.Escape)
                _composing = false;
            Write("preview-key-down", "post-route", eventArgs.OriginalSource, null, eventArgs.Key.ToString());
        });
    }

    private void Write(
        string eventKind,
        string stage,
        object originalSource,
        TextComposition? composition,
        string? key)
    {
        if (_disposed)
            return;
        var editor = FindEditor(originalSource);
        var record = new InteractionRecord(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            eventKind,
            stage,
            key,
            composition?.CompositionText ?? string.Empty,
            composition?.Text ?? string.Empty,
            editor is null ? string.Empty : AutomationProperties.GetName(editor),
            editor?.Text ?? string.Empty,
            editor?.CaretIndex ?? -1,
            editor?.SelectionStart ?? -1,
            editor?.SelectionLength ?? -1,
            _composing,
            editor?.IsKeyboardFocused ?? false,
            InputLanguageManager.Current.CurrentInputLanguage.Name);
        _writer.WriteLine(JsonSerializer.Serialize(record, JsonOptions));
    }

    private WpfTextBox? FindEditor(object originalSource)
    {
        if (originalSource is DependencyObject source)
        {
            for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is WpfTextBox textBox)
                    return textBox;
            }
        }
        return Keyboard.FocusedElement as WpfTextBox ??
               ShowcaseWindow.Descendants(_root).OfType<WpfTextBox>().FirstOrDefault();
    }

    private static string? ValueAfter(IReadOnlyList<string> arguments, string option)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == option)
                return arguments[index + 1];
        }
        return null;
    }

    private static string? ValueAfterEither(IReadOnlyList<string> arguments, string first, string second) =>
        ValueAfter(arguments, first) ?? ValueAfter(arguments, second);

    private sealed record InteractionRecord(
        int Sequence,
        string TimestampUtc,
        string EventKind,
        string Stage,
        string? Key,
        string CompositionText,
        string ResultText,
        string TargetName,
        string Value,
        int CaretIndex,
        int SelectionStart,
        int SelectionLength,
        bool Composing,
        bool Focused,
        string InputLanguage);
}
