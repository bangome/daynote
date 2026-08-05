using System.Globalization;

namespace Daynote.App.Showcase;

public enum ShowcasePalette { Standard, HighContrast }
public enum ShowcaseMotion { Normal, Reduced }
public enum ShowcaseStress { Default, Cjk, Long, Unbroken }
public enum ShowcaseFrame { Rest, Midpoint, Settled }

internal sealed record ShowcaseOptions(
    bool Showcase,
    bool CaptureAll,
    bool Hold,
    bool List,
    bool ShowHelp,
    string? Page,
    string? Output,
    string? InteractionLog,
    string? InteractionSequence,
    ShowcaseInputModality? InteractionModality,
    double? Width,
    double? Height,
    int Scale,
    ShowcasePalette Palette,
    ShowcaseMotion Motion,
    ShowcaseStress Stress,
    ShowcaseFrame Frame)
{
    public const string Usage = """
        Daynote.App --showcase [--page <id> | --capture-all] [options]

          --page, --state <id>       Select one manifest page (aliases)
          --capture-all              Capture every manifest page
          --output <directory>       Write PNG files, metadata, and manifest JSON
          --interaction-log <path>  Log routed IME evidence for the exact held CJK editor fixture
          --interaction-sequence <family>
                                    Capture one correlated held-WPF normal/reduced sequence
          --interaction-modality <pointer|keyboard>
                                    Select the semantic input path for the sequence
          --width <dip>              Capture surface width in WPF DIPs
          --height <dip>             Capture surface height in WPF DIPs
          --scale <1|2>              Render at 96 or 192 DPI
          --palette <standard|high-contrast>
          --motion <normal|reduced>
          --frame <rest|midpoint|settled>
          --stress <default|cjk|long|unbroken>
          --hold                     Keep the selected WPF window visible for UI automation
          --list                     Print the deterministic manifest JSON
          --help

        Exact reference capture:
          Daynote.App --showcase --page wide.app-shell.default --output <dir> --width 1586 --height 992 --scale 1
        """;

    public static ShowcaseOptions Parse(IReadOnlyList<string> args)
    {
        var values = new MutableOptions();
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--showcase": values.Showcase = true; break;
                case "--capture-all": values.CaptureAll = true; break;
                case "--hold": values.Hold = true; break;
                case "--list": values.List = true; break;
                case "--help" or "-h": values.ShowHelp = true; break;
                case "--page" or "--state": values.Page = Next(args, ref index, argument); break;
                case "--output": values.Output = Next(args, ref index, argument); break;
                case "--interaction-log": values.InteractionLog = Next(args, ref index, argument); break;
                case "--interaction-sequence": values.InteractionSequence = Next(args, ref index, argument); break;
                case "--interaction-modality": values.InteractionModality = ParseModality(Next(args, ref index, argument)); break;
                case "--width": values.Width = Positive(Next(args, ref index, argument), argument); break;
                case "--height": values.Height = Positive(Next(args, ref index, argument), argument); break;
                case "--scale": values.Scale = ParseScale(Next(args, ref index, argument)); break;
                case "--palette": values.Palette = ParsePalette(Next(args, ref index, argument)); break;
                case "--motion": values.Motion = ParseMotion(Next(args, ref index, argument)); values.MotionSpecified = true; break;
                case "--stress":
                    values.Stress = ParseStress(Next(args, ref index, argument));
                    values.StressSpecified = true;
                    break;
                case "--frame": values.Frame = ParseFrame(Next(args, ref index, argument)); values.FrameSpecified = true; break;
                default: throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        if (values.CaptureAll && values.Page is not null)
            throw new ArgumentException("Use either --page or --capture-all, not both.");
        if (values.InteractionLog is not null &&
            (!values.Showcase || !values.Hold || values.CaptureAll || values.List ||
             values.Page != "wide.markdown-editor.focus" || values.Stress != ShowcaseStress.Cjk))
            throw new ArgumentException(
                "--interaction-log requires the deterministic CJK editor hold fixture: " +
                "--showcase --page wide.markdown-editor.focus --stress cjk --hold.");
        var sequenceRequested = values.InteractionSequence is not null || values.InteractionModality is not null;
        if (sequenceRequested)
        {
            if (!values.Showcase)
                throw new ArgumentException("--interaction-sequence is showcase-only and requires --showcase.");
            if (values.InteractionSequence is null)
                throw new ArgumentException("--interaction-sequence requires a supported animated family.");
            if (values.InteractionModality is null)
                throw new ArgumentException("--interaction-modality is required for --interaction-sequence.");
            _ = ShowcaseInteractionCatalog.Find(values.InteractionSequence);
            if (values.Output is null)
                throw new ArgumentException("--interaction-sequence requires --output.");
            if (values.Page is not null || values.CaptureAll || values.Hold || values.List || values.InteractionLog is not null)
                throw new ArgumentException(
                    "--interaction-sequence cannot be combined with --page, --capture-all, --hold, --list, or --interaction-log.");
            if (values.MotionSpecified || values.FrameSpecified)
                throw new ArgumentException(
                    "--interaction-sequence captures normal and reduced motion internally; do not pass --motion or --frame.");
            if (values.StressSpecified)
                throw new ArgumentException(
                    "--interaction-sequence cannot be combined with explicit --stress; sequence fixtures use default stress.");
        }
        if ((values.CaptureAll || values.Page is not null) && !values.Hold && values.Output is null && !values.List)
            throw new ArgumentException("Capture selection requires --output, --hold, or --list.");

        return values.ToImmutable();
    }

    private static string Next(IReadOnlyList<string> args, ref int index, string argument)
    {
        if (++index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{argument} requires a value.");
        return args[index];
    }

    private static double Positive(string value, string argument)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed) || parsed <= 0)
            throw new ArgumentException($"{argument} requires a positive finite number.");
        return parsed;
    }

    private static int ParseScale(string value) => value switch
    {
        "1" => 1,
        "2" => 2,
        _ => throw new ArgumentException("--scale must be 1 or 2.")
    };

    private static ShowcasePalette ParsePalette(string value) => value switch
    {
        "standard" => ShowcasePalette.Standard,
        "high-contrast" => ShowcasePalette.HighContrast,
        _ => throw new ArgumentException("--palette must be standard or high-contrast.")
    };

    private static ShowcaseMotion ParseMotion(string value) => value switch
    {
        "normal" => ShowcaseMotion.Normal,
        "reduced" => ShowcaseMotion.Reduced,
        _ => throw new ArgumentException("--motion must be normal or reduced.")
    };

    private static ShowcaseInputModality ParseModality(string value) => value switch
    {
        "pointer" => ShowcaseInputModality.Pointer,
        "keyboard" => ShowcaseInputModality.Keyboard,
        _ => throw new ArgumentException("--interaction-modality must be pointer or keyboard.")
    };

    private static ShowcaseStress ParseStress(string value) => value switch
    {
        "default" => ShowcaseStress.Default,
        "cjk" => ShowcaseStress.Cjk,
        "long" => ShowcaseStress.Long,
        "unbroken" => ShowcaseStress.Unbroken,
        _ => throw new ArgumentException("--stress must be default, cjk, long, or unbroken.")
    };

    private static ShowcaseFrame ParseFrame(string value) => value switch
    {
        "rest" => ShowcaseFrame.Rest,
        "midpoint" => ShowcaseFrame.Midpoint,
        "settled" => ShowcaseFrame.Settled,
        _ => throw new ArgumentException("--frame must be rest, midpoint, or settled.")
    };

    private sealed class MutableOptions
    {
        public bool Showcase { get; set; }
        public bool CaptureAll { get; set; }
        public bool Hold { get; set; }
        public bool List { get; set; }
        public bool ShowHelp { get; set; }
        public string? Page { get; set; }
        public string? Output { get; set; }
        public string? InteractionLog { get; set; }
        public string? InteractionSequence { get; set; }
        public ShowcaseInputModality? InteractionModality { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public int Scale { get; set; } = 1;
        public ShowcasePalette Palette { get; set; } = ShowcasePalette.Standard;
        public ShowcaseMotion Motion { get; set; } = ShowcaseMotion.Normal;
        public bool MotionSpecified { get; set; }
        public ShowcaseStress Stress { get; set; } = ShowcaseStress.Default;
        public bool StressSpecified { get; set; }
        public ShowcaseFrame Frame { get; set; } = ShowcaseFrame.Settled;
        public bool FrameSpecified { get; set; }

        public ShowcaseOptions ToImmutable() => new(
            Showcase, CaptureAll, Hold, List, ShowHelp, Page, Output, InteractionLog,
            InteractionSequence, InteractionModality, Width, Height,
            Scale, Palette, Motion, Stress, Frame);
    }
}
