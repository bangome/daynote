using System.Globalization;
using System.Windows;
using System.Windows.Data;
using WpfBinding = System.Windows.Data.Binding;

namespace Daynote.App.Shell;

/// <summary>Returns <see cref="Visibility.Visible"/> when the bound value equals the parameter.</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString()) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        WpfBinding.DoNothing;
}

/// <summary>Returns true when the bound value equals the parameter (for command/selection state).</summary>
public sealed class EqualsToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : WpfBinding.DoNothing;
}

/// <summary>Collapses an element when the bound value is null; visible otherwise.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool present = value switch
        {
            null => false,
            string text => text.Length > 0,
            _ => true,
        };
        return present != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        WpfBinding.DoNothing;
}

/// <summary>Inverts a boolean.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>Returns Collapsed when the boolean is true, Visible when false (redesign panel collapse).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        WpfBinding.DoNothing;
}
