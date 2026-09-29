namespace Dami.Contracts.Finance;

/// <summary>One thing Steve paid for.</summary>
/// <param name="ExpenseId">Identity.</param>
/// <param name="SpentOn">The day it was paid, as printed on the receipt when it says.</param>
/// <param name="Merchant">Who was paid.</param>
/// <param name="Total">What was paid, in <paramref name="Currency"/>.</param>
/// <param name="Currency">ISO code, "USD" unless the receipt says otherwise.</param>
/// <param name="Category">A small fixed vocabulary: groceries, dining, fuel, household, health, hobby, travel, other.</param>
/// <param name="Source">Where it came from: "receipt-photo" today.</param>
/// <param name="RecordedAt">When Dami wrote it down.</param>
public sealed record Expense(
    Guid ExpenseId,
    DateOnly SpentOn,
    string Merchant,
    decimal Total,
    string Currency,
    string Category,
    string Source,
    DateTimeOffset RecordedAt);

/// <summary>What Steve spent. Local only: nothing here is sent to a frontier model.</summary>
public interface IExpenseLedger
{
    /// <summary>Records an expense; false when the same merchant, total and day is already there.</summary>
    Task<bool> RecordAsync(Expense expense, CancellationToken cancellationToken);

    /// <summary>Expenses from <paramref name="from"/> to <paramref name="to"/> inclusive, newest first.</summary>
    Task<IReadOnlyList<Expense>> BetweenAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
