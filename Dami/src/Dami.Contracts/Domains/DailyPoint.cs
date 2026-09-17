namespace Dami.Contracts.Domains;

/// <summary>One day's value in a daily series.</summary>
/// <param name="Day">The calendar day, in the owner's time zone.</param>
/// <param name="Value">The day's value; a day with no data has no point rather than a zero.</param>
public sealed record DailyPoint(DateOnly Day, double Value);
