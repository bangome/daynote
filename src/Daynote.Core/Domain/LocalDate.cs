using System.Globalization;

namespace Daynote.Core.Domain;

public readonly record struct LocalDate : ISpanFormattable
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
