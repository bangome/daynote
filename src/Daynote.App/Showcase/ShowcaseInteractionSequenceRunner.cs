using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyEventHandler = System.Windows.Input.KeyEventHandler;
using WpfSize = System.Windows.Size;

namespace Daynote.App.Showcase;

internal static partial class ShowcaseInteractionSequenceRunner
{
    private static ShowcaseSequenceTransition RunTransition(
        FrameworkElement surface,
        ShowcaseWindow window,
        ShowcaseInteractionDefinition definition,
        ShowcaseInputModality modality,
        ShowcaseMotion motion,
        string output,
        double width,
        double height,
        int scale,
        IntPtr hwnd)
    {
        var transitionId = Guid.NewGuid().ToString("N");
        var initiatorAutomationName = InitiatorName(definition, modality);
        var initiator = FindExact(surface, initiatorAutomationName, definition.InitiatorControlType);
        var motionTargetControlType = definition.MotionTargetAutomationName == definition.InitiatorAutomationName
            ? definition.InitiatorControlType
            : null;
        var motionTarget = FindExact(
            surface, definition.MotionTargetAutomationName, motionTargetControlType);
        var scrollOwner = definition.ScrollOwnerAutomationName == "none"
            ? null
            : FindExact(surface, definition.ScrollOwnerAutomationName, null);
        if (scrollOwner is not null && !IsActualScrollProvider(scrollOwner))
            throw new InvalidOperationException(
                $"Declared scroll owner '{definition.ScrollOwnerAutomationName}' is not an actual scroll provider.");
        ResetSemanticState(surface, motionTarget, definition, motion);
        var semanticBefore = ObserveSemanticState(surface, definition);
        if (semanticBefore != definition.StateBefore)
            throw new InvalidOperationException(
                $"Expected semantic before value '{definition.StateBefore}', observed '{semanticBefore}'.");
        var expectedFocusBefore = modality == ShowcaseInputModality.Keyboard
            ? initiatorAutomationName
            : definition.FocusBefore;
        var expectedFocusBeforeControlType = expectedFocusBefore == initiatorAutomationName ||
                                             expectedFocusBefore == definition.InitiatorAutomationName
            ? definition.InitiatorControlType
            : definition.FamilyId == "note-tab" ? "TabItem" : null;
        Focus(window, FindExact(surface, expectedFocusBefore, expectedFocusBeforeControlType));
        var focusBefore = FocusName();
        var frames = new List<ShowcaseSequenceFrame>();
        var events = new List<ShowcaseSequenceInputEvent>();
        ShowcaseSequenceActionReceipt? handlerReceipt = null;
        frames.Add(Capture(
            surface, motionTarget, scrollOwner, definition, transitionId, motion,
            motion == ShowcaseMotion.Normal ? "Rest0" : "ReducedRest",
            output, width, height, scale, 0));

        var triggered = false;
        var inputManager = InputManager.Current;
        var stagedInputs = new HashSet<InputEventArgs>();
        PreProcessInputEventHandler preProcess = (_, args) =>
            ObserveStaged(args.StagingItem.Input, "input-manager-pre-process");
        ProcessInputEventHandler postProcess = (_, args) =>
            ObserveStaged(args.StagingItem.Input, "input-manager-post-process");
        MouseButtonEventHandler mouseHandler = (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, initiator))
                return;
            Observe(args, "routed-preview");
            if (args.RoutedEvent == Mouse.PreviewMouseUpEvent)
                BeginTransition();
        };
        WpfKeyEventHandler keyHandler = (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, initiator))
                return;
            Observe(args, "routed-preview");
            if (args.RoutedEvent == Keyboard.PreviewKeyDownEvent &&
                KeyName(args.Key) == definition.KeyboardKey)
                BeginTransition();
        };

        void Add(InputEventArgs args, string stage)
        {
            var original = args.OriginalSource as DependencyObject;
            events.Add(new ShowcaseSequenceInputEvent(
                transitionId,
                Environment.ProcessId,
                Environment.CurrentManagedThreadId,
                stage,
                args.RoutedEvent?.Name ?? "none",
                Name(original),
                initiatorAutomationName,
                args is WpfKeyEventArgs key ? KeyName(key.Key) : null,
                args is MouseButtonEventArgs mouse ? mouse.ChangedButton.ToString() : null,
                DateTimeOffset.UtcNow));
        }

        void ObserveStaged(InputEventArgs args, string stage)
        {
            if (stagedInputs.Contains(args))
                Add(args, stage);
        }

        void Observe(InputEventArgs args, string stage) => Add(args, stage);

        void BeginTransition()
        {
            if (triggered)
                return;
            triggered = true;
        }

        inputManager.PreProcessInput += preProcess;
        inputManager.PostProcessInput += postProcess;
        surface.AddHandler(Mouse.PreviewMouseDownEvent, mouseHandler, true);
        surface.AddHandler(Mouse.PreviewMouseUpEvent, mouseHandler, true);
        surface.AddHandler(Keyboard.PreviewKeyDownEvent, keyHandler, true);
        surface.AddHandler(Keyboard.PreviewKeyUpEvent, keyHandler, true);
        try
        {
            if (modality == ShowcaseInputModality.Pointer)
                ProcessPointer(initiator, stagedInputs);
            else
                ProcessKeyboard(initiator, definition.KeyboardKey, stagedInputs);
            if (!triggered)
                throw new InvalidOperationException(
                    $"The {definition.SemanticAction} input did not reach the WPF routed-event observer.");

            Pump(surface.Dispatcher, TimeSpan.FromMilliseconds(1));
            handlerReceipt = ShowcaseInteractionBehavior.GetLastReceipt(initiator);
            if (handlerReceipt is null || handlerReceipt.SemanticAction != definition.SemanticAction)
                throw new InvalidOperationException(
                    $"The {definition.SemanticAction} input produced no matching fixture action-handler receipt.");

            if (motion == ShowcaseMotion.Normal)
            {
                var midpoint = DaynoteMotionPolicy.ForShowcase(reducedMotion: false).EvidenceMidpoint;
                ApplyMotionSample(motionTarget, definition, ShowcaseMotion.Normal, ShowcaseFrame.Midpoint);
                frames.Add(Capture(
                    surface, motionTarget, scrollOwner, definition, transitionId, motion,
                    "Midpoint100", output, width, height, scale, midpoint.TotalMilliseconds));
                var duration = Duration(definition);
                ApplyMotionSample(motionTarget, definition, ShowcaseMotion.Normal, ShowcaseFrame.Settled);
                frames.Add(Capture(
                    surface, motionTarget, scrollOwner, definition, transitionId, motion,
                    "Settled", output, width, height, scale, duration.TotalMilliseconds));
            }
            else
            {
                ApplyMotionSample(motionTarget, definition, ShowcaseMotion.Reduced, ShowcaseFrame.Settled);
                frames.Add(Capture(
                    surface, motionTarget, scrollOwner, definition, transitionId, motion,
                    "InstantSettled", output, width, height, scale, 0));
            }
        }
        finally
        {
            inputManager.PreProcessInput -= preProcess;
            inputManager.PostProcessInput -= postProcess;
            surface.RemoveHandler(Mouse.PreviewMouseDownEvent, mouseHandler);
            surface.RemoveHandler(Mouse.PreviewMouseUpEvent, mouseHandler);
            surface.RemoveHandler(Keyboard.PreviewKeyDownEvent, keyHandler);
            surface.RemoveHandler(Keyboard.PreviewKeyUpEvent, keyHandler);
        }

        var finalState = ObserveSemanticState(surface, definition);
        if (finalState != definition.StateAfter)
            throw new InvalidOperationException(
                $"Expected final state '{definition.StateAfter}', observed '{finalState}'.");
        var focusAfter = FocusName();
        return new ShowcaseSequenceTransition(
            transitionId,
            Environment.ProcessId,
            Environment.CurrentManagedThreadId,
            Hwnd(hwnd),
            motion,
            definition.SemanticAction,
            initiatorAutomationName,
            definition.InitiatorControlType,
            definition.StateBefore,
            definition.StateAfter,
            finalState,
            semanticBefore,
            finalState,
            focusBefore,
            focusAfter,
            definition.ScrollOwner,
            scrollOwner is null ? "none" : Name(scrollOwner),
            motion == ShowcaseMotion.Reduced ? 0 : Duration(definition).TotalMilliseconds,
            motion == ShowcaseMotion.Normal,
            handlerReceipt,
            events,
            frames);
    }

}
