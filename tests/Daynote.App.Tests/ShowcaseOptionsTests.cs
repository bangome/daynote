using System.Reflection;
using System.Runtime.ExceptionServices;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
public sealed class ShowcaseOptionsTests
{
    private static readonly Type OptionsType = typeof(ShowcaseManifest).Assembly
        .GetType("Daynote.App.Showcase.ShowcaseOptions", throwOnError: true)!;

    private static readonly MethodInfo ParseMethod = OptionsType.GetMethod(
        "Parse", BindingFlags.Public | BindingFlags.Static)!;

    [TestMethod]
    public void Parse_WithoutArguments_UsesSafeDeterministicDefaults()
    {
        var options = Parse();

        Assert.IsFalse(Get<bool>(options, "Showcase"));
        Assert.IsFalse(Get<bool>(options, "CaptureAll"));
        Assert.IsFalse(Get<bool>(options, "Hold"));
        Assert.IsFalse(Get<bool>(options, "List"));
        Assert.IsFalse(Get<bool>(options, "ShowHelp"));
        Assert.IsNull(Get<string?>(options, "Page"));
        Assert.IsNull(Get<string?>(options, "Output"));
        Assert.IsNull(Get<string?>(options, "InteractionLog"));
        Assert.IsNull(Get<string?>(options, "InteractionSequence"));
        Assert.IsNull(Get<object?>(options, "InteractionModality"));
        Assert.IsNull(Get<double?>(options, "Width"));
        Assert.IsNull(Get<double?>(options, "Height"));
        Assert.AreEqual(1, Get<int>(options, "Scale"));
        Assert.AreEqual(ShowcasePalette.Standard, Get<ShowcasePalette>(options, "Palette"));
        Assert.AreEqual(ShowcaseMotion.Normal, Get<ShowcaseMotion>(options, "Motion"));
        Assert.AreEqual(ShowcaseStress.Default, Get<ShowcaseStress>(options, "Stress"));
        Assert.AreEqual(ShowcaseFrame.Settled, Get<ShowcaseFrame>(options, "Frame"));
    }

    [TestMethod]
    public void Parse_AllSupportedOptions_PreservesInvariantNumericAndEnumValues()
    {
        var options = Parse(
            "--showcase", "--page", "regular.search.focus", "--output", "captures",
            "--width", "1586.5", "--height", "992", "--scale", "2",
            "--palette", "high-contrast", "--motion", "reduced", "--stress", "cjk",
            "--frame", "midpoint",
            "--hold", "--list");

        Assert.IsTrue(Get<bool>(options, "Showcase"));
        Assert.IsTrue(Get<bool>(options, "Hold"));
        Assert.IsTrue(Get<bool>(options, "List"));
        Assert.AreEqual("regular.search.focus", Get<string>(options, "Page"));
        Assert.AreEqual("captures", Get<string>(options, "Output"));
        Assert.AreEqual(1586.5, Get<double?>(options, "Width"));
        Assert.AreEqual(992d, Get<double?>(options, "Height"));
        Assert.AreEqual(2, Get<int>(options, "Scale"));
        Assert.AreEqual(ShowcasePalette.HighContrast, Get<ShowcasePalette>(options, "Palette"));
        Assert.AreEqual(ShowcaseMotion.Reduced, Get<ShowcaseMotion>(options, "Motion"));
        Assert.AreEqual(ShowcaseStress.Cjk, Get<ShowcaseStress>(options, "Stress"));
        Assert.AreEqual(ShowcaseFrame.Midpoint, Get<ShowcaseFrame>(options, "Frame"));
    }

    [TestMethod]
    public void Parse_StateAliasAndShortHelp_AreAccepted()
    {
        var options = Parse("--state", "compact.calendar-day.active", "--hold", "-h");

        Assert.AreEqual("compact.calendar-day.active", Get<string>(options, "Page"));
        Assert.IsTrue(Get<bool>(options, "ShowHelp"));
    }

    [TestMethod]
    public void Parse_InteractionSequence_RequiresExactShowcaseFamilyModalityAndOutput()
    {
        var options = Parse(
            "--showcase", "--interaction-sequence", "date-header",
            "--interaction-modality", "pointer", "--output", "captures");

        Assert.AreEqual("date-header", Get<string>(options, "InteractionSequence"));
        Assert.AreEqual("Pointer", Get<object>(options, "InteractionModality").ToString());
        Assert.AreEqual("captures", Get<string>(options, "Output"));
    }

    [TestMethod]
    public void Parse_InteractionSequence_WithExplicitStress_IsRejectedInsteadOfSilentlyDefaulting()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => Parse(
            "--showcase", "--interaction-sequence", "app-shell",
            "--interaction-modality", "pointer", "--output", "x", "--stress", "cjk"));

        StringAssert.Contains(exception.Message, "cannot be combined");
    }

    [TestMethod]
    [DataRow(
        new[] { "--showcase", "--interaction-sequence", "app-shell", "--output", "captures" },
        "--interaction-modality")]
    [DataRow(
        new[] { "--showcase", "--interaction-sequence", "unknown", "--interaction-modality", "pointer", "--output", "captures" },
        "supported animated family")]
    [DataRow(
        new[] { "--showcase", "--interaction-sequence", "app-shell", "--interaction-modality", "gesture", "--output", "captures" },
        "pointer or keyboard")]
    [DataRow(
        new[] { "--interaction-sequence", "app-shell", "--interaction-modality", "pointer", "--output", "captures" },
        "showcase-only")]
    [DataRow(
        new[] { "--showcase", "--interaction-sequence", "app-shell", "--interaction-modality", "pointer", "--output", "captures", "--page", "wide.app-shell.default" },
        "cannot be combined")]
    [DataRow(
        new[] { "--showcase", "--interaction-sequence", "app-shell", "--interaction-modality", "pointer", "--output", "captures", "--motion", "normal" },
        "captures normal and reduced motion internally")]
    [DataRow(
        new[] { "--showcase", "--interaction-sequence", "app-shell", "--interaction-modality", "pointer", "--output", "captures", "--stress", "cjk" },
        "cannot be combined")]
    public void Parse_InteractionSequence_MalformedOrAmbiguousArgumentsAreRejected(
        string[] arguments,
        string expectedMessage)
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => Parse(arguments));

        StringAssert.Contains(exception.Message, expectedMessage);
    }

    [TestMethod]
    public void Parse_InteractionLog_WhenExactCjkEditorHoldFixtureSelected_IsAccepted()
    {
        var options = Parse(
            "--showcase", "--page", "wide.markdown-editor.focus", "--stress", "cjk",
            "--hold", "--interaction-log", "composition.jsonl");

        Assert.AreEqual("composition.jsonl", Get<string>(options, "InteractionLog"));
    }

    [TestMethod]
    [DataRow("compact.markdown-editor.focus", "cjk", true)]
    [DataRow("wide.markdown-editor.focus", "default", true)]
    [DataRow("wide.markdown-editor.focus", "cjk", false)]
    public void Parse_InteractionLog_OutsideExactEvidenceFixture_IsRejected(
        string page,
        string stress,
        bool hold)
    {
        var arguments = new List<string>
        {
            "--showcase", "--page", page, "--stress", stress,
            "--interaction-log", "composition.jsonl"
        };
        if (hold)
            arguments.Add("--hold");

        var exception = Assert.ThrowsExactly<ArgumentException>(() => Parse([.. arguments]));

        StringAssert.Contains(exception.Message, "deterministic CJK editor hold fixture");
    }

    [TestMethod]
    [DataRow("--width", "0", "positive finite")]
    [DataRow("--height", "NaN", "positive finite")]
    [DataRow("--scale", "3", "must be 1 or 2")]
    [DataRow("--palette", "sepia", "standard or high-contrast")]
    [DataRow("--motion", "sometimes", "normal or reduced")]
    [DataRow("--stress", "random", "default, cjk, long, or unbroken")]
    [DataRow("--frame", "between", "rest, midpoint, or settled")]
    public void Parse_InvalidValue_ExplainsTheAcceptedDomain(string option, string value, string message)
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => Parse(option, value));

        StringAssert.Contains(exception.Message, message);
    }

    [TestMethod]
    public void Parse_SelectionWithoutDestinationOrHold_IsRejected()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => Parse("--page", "wide.app-shell.default"));

        StringAssert.Contains(exception.Message, "--output, --hold, or --list");
    }

    [TestMethod]
    public void Parse_PageAndCaptureAll_AreMutuallyExclusive()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => Parse("--page", "wide.app-shell.default", "--capture-all", "--list"));

        StringAssert.Contains(exception.Message, "either --page or --capture-all");
    }

    [TestMethod]
    [DataRow("--page")]
    [DataRow("--output")]
    [DataRow("--width")]
    [DataRow("--interaction-log")]
    [DataRow("--interaction-sequence")]
    [DataRow("--interaction-modality")]
    public void Parse_MissingValue_NamesTheOption(string option)
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => Parse(option));

        StringAssert.Contains(exception.Message, $"{option} requires a value");
    }

    [TestMethod]
    public void Parse_UnknownOption_IsRejectedInsteadOfSilentlyIgnored()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => Parse("--unknown"));

        StringAssert.Contains(exception.Message, "Unknown argument: --unknown");
    }

    private static object Parse(params string[] arguments)
    {
        try
        {
            return ParseMethod.Invoke(null, [arguments])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static T Get<T>(object options, string propertyName) =>
        (T)OptionsType.GetProperty(propertyName)!.GetValue(options)!;
}
