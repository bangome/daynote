using CommunityToolkit.Mvvm.ComponentModel;

namespace Daynote.Mobile.ViewModels;

/// <summary>One month in the year-and-month picker.</summary>
/// <remarks>
/// The label comes from the active culture rather than the string catalog: every language the app
/// offers already names its months, and twelve more catalog entries per language would be twelve
/// more things to keep in step with the calendar header right above them.
/// </remarks>
public sealed partial class MonthOption(int number, string label) : ObservableObject
{
    public int Number { get; } = number;

    public string Label { get; } = label;

    /// <summary>Whether this is the month the calendar is currently showing.</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
