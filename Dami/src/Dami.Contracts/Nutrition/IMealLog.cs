namespace Dami.Contracts.Nutrition;

/// <summary>One meal, estimated from a photo by the local vision model.</summary>
public sealed record Meal(Guid MealId, DateTimeOffset EatenAt, string Description, int Calories, int ProteinGrams, DateTimeOffset RecordedAt)
{
    /// <summary>low, moderate or high, when the estimate said (ADR-0037 slice 2).</summary>
    public string? VitaminK { get; init; }
}

/// <summary>What Steve ate. Health data: local only, never sent to a frontier model.</summary>
public interface IMealLog
{
    /// <summary>Records a meal.</summary>
    Task RecordAsync(Meal meal, CancellationToken cancellationToken);

    /// <summary>Meals eaten in [<paramref name="from"/>, <paramref name="to"/>), earliest first.</summary>
    Task<IReadOnlyList<Meal>> BetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
