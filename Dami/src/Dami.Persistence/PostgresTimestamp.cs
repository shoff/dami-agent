namespace Dami.Persistence;

/// <summary>Normalizes instants to PostgreSQL's microsecond timestamp precision.</summary>
internal static class PostgresTimestamp
{
    public static DateTimeOffset Normalize(DateTimeOffset value)
    {
        long utcTicks = value.UtcTicks;
        long normalizedTicks = utcTicks - (utcTicks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(normalizedTicks, TimeSpan.Zero);
    }
}
