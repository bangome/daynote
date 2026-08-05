using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
public sealed class ShowcaseInteractionLoggerTests
{
    [STATestMethod]
    public void RoutedCompositionStart_WritesTimestampedCaretAndValueSnapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daynote-composition-{Guid.NewGuid():N}.jsonl");
        try
        {
            var root = new Grid();
            var editor = new TextBox { Text = "stable", CaretIndex = 3 };
            AutomationProperties.SetName(editor, "Deterministic composition editor");
            root.Children.Add(editor);
            var logger = CreateLogger(root, path);

            var composition = new TextComposition(InputManager.Current, editor, "ㄱ");
            var eventArgs = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
            {
                RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent
            };
            editor.RaiseEvent(eventArgs);
            logger.Dispose();

            var lines = File.ReadAllLines(path);
            Assert.HasCount(1, lines);
            using var document = JsonDocument.Parse(lines[0]);
            var record = document.RootElement;
            Assert.AreEqual("preview-text-input-start", record.GetProperty("eventKind").GetString());
            Assert.AreEqual("Deterministic composition editor", record.GetProperty("targetName").GetString());
            Assert.AreEqual("stable", record.GetProperty("value").GetString());
            Assert.AreEqual(3, record.GetProperty("caretIndex").GetInt32());
            Assert.IsTrue(record.GetProperty("composing").GetBoolean());
            Assert.IsTrue(DateTimeOffset.TryParse(record.GetProperty("timestampUtc").GetString(), out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IDisposable CreateLogger(FrameworkElement root, string path)
    {
        var type = typeof(ShowcaseManifest).Assembly.GetType(
            "Daynote.App.Showcase.ShowcaseInteractionLogger", throwOnError: true)!;
        var constructor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(FrameworkElement), typeof(string)],
            modifiers: null)!;
        return (IDisposable)constructor.Invoke([root, path]);
    }
}
