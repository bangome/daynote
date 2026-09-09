using System.Globalization;

namespace Daynote.Core.Domain;

/// <summary>
/// A calendar date with no time and no zone.
/// </summary>
/// <remarks>
/// <see cref="IComparable{T}"/> is not decoration. A date without an order cannot be sorted, and
/// <c>OrderBy</c> on a type that implements neither interface throws at run time rather than at
/// compile time — which is how a search whose matches spanned two dates came back blank: the
/// exception surfaced inside a fire-and-forget query and was swallowed (SearchDropdownViewModel).
/// </remarks>
public readonly record struct LocalDate : ISpanFormattable, IComparable<LocalDate>, IComparable
{
    private const string IsoFormat = "yyyy-MM-dd";
    private readonly DateOnly value;

    private LocalDate(DateOnly value)
    {
        this.value = value;
    }

    public int Year => value.Year;

    public int Month => value.Month;

    public int Day => value.Day;

    public int CompareTo(LocalDate other) => value.CompareTo(other.value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        LocalDate other => CompareTo(other),
        _ => throw new ArgumentException($"Cannot compare a {nameof(LocalDate)} with {obj.GetType()}.", nameof(obj)),
    };

    public static bool operator <(LocalDate left, LocalDate right) => left.CompareTo(right) < 0;

    public static bool operator <=(LocalDate left, LocalDate right) => left.CompareTo(right) <= 0;

    public static bool operator >(LocalDate left, LocalDate right) => left.CompareTo(right) > 0;

    public static bool operator >=(LocalDate left, LocalDate right) => left.CompareTo(right) >= 0;

    public static DomainResult<LocalDate> Parse(string? text)
    {
        if (text is null ||
            text.Length != 10 ||
            !DateOnly.TryParseExact(
                text,
                IsoFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsed) ||
            !string.Equals(text, parsed.ToString(IsoFormat, CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return DomainResult<LocalDate>.Failure(
                DomainErrorCode.InvalidLocalDate,
                "The local date must be a valid canonical ISO date (yyyy-MM-dd).");
        }

        return DomainResult<LocalDate>.Success(new LocalDate(parsed));
    }

    internal static LocalDate FromDateOnly(DateOnly date) => new(date);

    public override string ToString() => value.ToString(IsoFormat, CultureInfo.InvariantCulture);

    public string ToString(string? format, IFormatProvider? formatProvider) =>
        value.ToString(format ?? IsoFormat, formatProvider ?? CultureInfo.InvariantCulture);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        value.TryFormat(destination, out charsWritten, format.IsEmpty ? IsoFormat : format, provider);
}
