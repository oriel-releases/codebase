namespace Oriel.Core.Services;

public interface IClock
{
    DateTimeOffset Now { get; }
    DateOnly Today => DateOnly.FromDateTime(Now.LocalDateTime);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
