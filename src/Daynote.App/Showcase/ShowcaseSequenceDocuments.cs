using System.Windows;

namespace Daynote.App.Showcase;

public sealed record ShowcaseSequenceInputEvent(
    string TransitionId,
    int ProcessId,
    int DispatcherThreadId,
    string Stage,
    string RoutedEvent,
    string OriginalSourceAutomationName,
    string TargetAutomationName,
    string? Key,
    string? MouseButton,
    DateTimeOffset ObservedUtc);

public sealed record ShowcaseSequenceActionReceipt(
    string SemanticAction,
    string ControlEvent,
    string SourceAutomationName,
    long Sequence,
    DateTimeOffset ObservedUtc);

public sealed record ShowcaseSequenceFrame(
    string TransitionId,
    int ProcessId,
    string Semantic,
    string Png,
    string PngSha256,
    int PixelWidth,
    int PixelHeight,
    double ElapsedMilliseconds,
    double Opacity,
    double TranslateY,
    string StateObserved,
    string FocusObserved,
    string ScrollOwnerObserved,
    DateTimeOffset CapturedUtc);

public sealed record ShowcaseSequenceTransition(
    string TransitionId,
    int ProcessId,
    int DispatcherThreadId,
    string WindowHwnd,
    ShowcaseMotion Motion,
    string SemanticAction,
    string InitiatorAutomationName,
    string InitiatorControlType,
    string StateBefore,
    string StateAfterExpected,
    string FinalStateObserved,
    string SemanticValueBeforeObserved,
    string SemanticValueAfterObserved,
    string FocusBeforeObserved,
    string FocusAfterObserved,
    string ScrollOwner,
    string ScrollOwnerObserved,
    double DurationMilliseconds,
    bool IntermediateFrameObserved,
    ShowcaseSequenceActionReceipt? HandlerReceipt,
    IReadOnlyList<ShowcaseSequenceInputEvent> InputEvents,
    IReadOnlyList<ShowcaseSequenceFrame> Frames);

public sealed record ShowcaseInteractionSequenceDocument(
    string Schema,
    string RunId,
    DateTimeOffset CapturedUtc,
    DateTimeOffset ProcessStartUtc,
    string BuildIdentity,
    DateTimeOffset BuildModifiedUtc,
    DateTimeOffset SourceModifiedUtc,
    string ExecutablePath,
    string ExecutableSha256,
    int ProcessId,
    int DispatcherThreadId,
    string WindowHwnd,
    string FamilyId,
    string PageId,
    ShowcaseInputModality Modality,
    string SemanticAction,
    string InitiatorAutomationName,
    string KeyboardInitiatorAutomationName,
    string InitiatorControlType,
    string MotionTargetAutomationName,
    string ScrollOwner,
    string ScrollOwnerAutomationName,
    string PageRootAutomationName,
    IReadOnlyList<string> InitiatorAncestorAutomationNames,
    IReadOnlyList<ShowcaseSequenceTransition> Transitions);

public static class ShowcaseInteractionState
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(string), typeof(ShowcaseInteractionState), new PropertyMetadata(string.Empty));

    public static void SetValue(DependencyObject target, string value) => target.SetValue(ValueProperty, value);
    public static string GetValue(DependencyObject target) => (string)target.GetValue(ValueProperty);
}
