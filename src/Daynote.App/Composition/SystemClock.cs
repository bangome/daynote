using Daynote.Core.Time;

namespace Daynote.App.Composition;

/// <summary>Production <see cref="IClock"/> reading the machine's current instant and offset.</summary>
public sealed class SystemClock : IClock
{
    public ClockSnapshot Read()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        return new ClockSnapshot(now.ToUniversalTime(), now.Offset);
    }
}
