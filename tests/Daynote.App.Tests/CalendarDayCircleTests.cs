using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

/// <summary>
/// The calendar day cell: the ring that marks today and the selected date has to sit concentric with
/// the day number.
/// </summary>
/// <remarks>
/// This is measured rather than eyeballed because the failure mode is a fraction of a pixel that
/// looks like a design choice. WPF centres a TextBlock by its line box, which includes the font's
/// descent; digits have no descender, so centring the box leaves the digits low and the ring
/// looking raised. The style corrects for it, and the correction is exactly the kind of value that
/// silently rots when a font, a size, or a weight changes — hence a test that recomputes it from
/// the font instead of restating the number.
/// </remarks>
[TestClass]
public sealed class CalendarDayCircleTests
{
    /// <summary>Half a pixel is the most that can be off before the ring reads as off-centre.</summary>
    private const double Tolerance = 0.5;

    [STATestMethod]
    [DataRow("1")]
    [DataRow("8")]
    [DataRow("18")]
    [DataRow("30")]
    public void TheRingIsConcentricWithTheDayNumber(string dayText)
    {
        (Border circle, TextBlock number) = ComposeCell(dayText);

        double circleCentre = circle.ActualHeight / 2;
        double inkCentre = InkCentreY(number, circle);

        Assert.AreEqual(
            circleCentre,
            inkCentre,
            Tolerance,
            $"The day number's ink centre is {inkCentre:N2} but the ring's centre is {circleCentre:N2}. "
                + "Adjust the bottom margin on Daynote.Product.Style.Calendar.DayNumber by twice the "
                + "difference: a centred child moves by half its margin.");
    }

    [STATestMethod]
    public void TheCorrectionIsStillNeeded()
    {
        // Guards the comment as much as the code. If a future font centres its digits by itself,
        // this fails and the margin should be removed rather than left as cargo.
        (_, TextBlock number) = ComposeCell("18");
        var typeface = new Typeface(number.FontFamily, number.FontStyle, number.FontWeight, number.FontStretch);
        FormattedText text = Format("18", typeface, number.FontSize);
        Rect ink = text.BuildGeometry(new Point(0, 0)).Bounds;

        double boxCentre = text.Height / 2;
        double inkCentre = ink.Top + (ink.Height / 2);

        Assert.IsGreaterThan(
            0.25,
            Math.Abs(inkCentre - boxCentre),
            "The font now centres its digits within the line box, so the margin correction on "
                + "Daynote.Product.Style.Calendar.DayNumber is no longer doing anything and should go.");
    }

    [STATestMethod]
    public void TheHeatDotReservesItsSpaceOnDaysWithoutNotes()
    {
        // A day with no notes still has to lay out as tall as one with notes. If the dot collapses,
        // the cell's stack shrinks and the day number rides down relative to the days around it —
        // the whole row stops sharing a baseline, which is what the eye actually notices.
        double quiet = DayNumberTop(activityLevel: 0);
        double busy = DayNumberTop(activityLevel: 3);

        Assert.AreEqual(
            busy,
            quiet,
            Tolerance,
            "A day with no notes puts its number at a different height from a day with notes. The "
                + "heat dot must be Hidden rather than Collapsed in "
                + "Daynote.Product.Style.Calendar.HeatDot so its space survives.");
    }

    /// <summary>Where the day number sits inside the cell stack at a given heat level.</summary>
    private static double DayNumberTop(int activityLevel)
    {
        (Border circle, TextBlock number) = ComposeCell("18");
        var stack = new StackPanel { DataContext = new HeatLevel(activityLevel) };
        stack.Children.Add(circle);
        stack.Children.Add(new Ellipse
        {
            Style = (Style)Application.Current.Resources["Daynote.Product.Style.Calendar.HeatDot"],
        });

        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        stack.Arrange(new Rect(new Point(0, 0), new Size(40, 44)));
        stack.UpdateLayout();

        // Measured against the stack's own bottom, because that is what the surrounding grid centres.
        return stack.DesiredSize.Height - number.TransformToAncestor(stack).Transform(new Point(0, 0)).Y;
    }

    /// <summary>Builds the circle and its number with the shipping styles, laid out as in the grid.</summary>
    private static (Border Circle, TextBlock Number) ComposeCell(string dayText)
    {
        var application = Application.Current ?? new Application();
        foreach (string uri in new[]
        {
            "/Daynote.App;component/Themes/Daynote.Product.Light.xaml",
            "/Daynote.App;component/Themes/Daynote.Product.Styles.xaml",
        })
        {
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) });
        }

        var number = new TextBlock
        {
            Text = dayText,
            Style = (Style)application.Resources["Daynote.Product.Style.Calendar.DayNumber"],
        };
        var circle = new Border
        {
            Style = (Style)application.Resources["Daynote.Product.Style.Calendar.DayCircle"],
            Child = number,
        };

        circle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        circle.Arrange(new Rect(new Point(0, 0), circle.DesiredSize));
        circle.UpdateLayout();
        return (circle, number);
    }

    /// <summary>
    /// Where the digits actually are, in the circle's own coordinates: the TextBlock's arranged
    /// position plus where the glyphs sit inside it.
    /// </summary>
    private static double InkCentreY(TextBlock number, Border circle)
    {
        Point origin = number.TransformToAncestor(circle).Transform(new Point(0, 0));
        var typeface = new Typeface(number.FontFamily, number.FontStyle, number.FontWeight, number.FontStretch);
        Rect ink = Format(number.Text, typeface, number.FontSize).BuildGeometry(new Point(0, 0)).Bounds;
        return origin.Y + ink.Top + (ink.Height / 2);
    }

    private static FormattedText Format(string text, Typeface typeface, double fontSize) => new(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        typeface,
        fontSize,
        Brushes.Black,
        1.0);
}

/// <summary>
/// Stands in for the day-cell view model. Public because WPF bindings cannot see the properties of
/// an internal (or anonymous) type.
/// </summary>
public sealed record HeatLevel(int ActivityLevel);
