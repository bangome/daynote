namespace Daynote.Core.Time;

public interface IClock
{
    ClockSnapshot Read();
}

public readonly record struct ClockSnapshot(DateTimeOffset UtcInstant, TimeSpan LocalUtcOffset);
