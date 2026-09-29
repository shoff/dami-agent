using System.Globalization;
using System.Text.Json;
using Dami.Contracts.Finance;

namespace Dami.Core.Finance;

/// <summary>Turns the local vision model's reading of a receipt into one expense, or nothing.</summary>
/// <remarks>
/// The model is asked for a JSON object and does not always manage one, or a plausible
/// date; a receipt it cannot read is better unrecorded than recorded wrong, and a date
/// in the future or years back is the model misreading, so the day it was sent stands.
/// </remarks>
public static class ReceiptReader
{
    /// <summary>What the vision model is asked.</summary>
    public const string PROMPT =
        "This is a photo of a receipt. Reply with only one JSON object and nothing else: "
        + "{\"merchant\": the store or business name, \"date\": the purchase date as YYYY-MM-DD or null, "
        + "\"total\": the final amount paid as a number, \"currency\": the ISO code, \"category\": one of "
        + "groceries, dining, fuel, household, health, hobby, travel, other}. If there is no receipt "
        + "or no total, reply {}.";

    /// <summary>The categories a receipt may carry; anything else is "other".</summary>
    public static IReadOnlyList<string> Categories { get; } =
        ["groceries", "dining", "fuel", "household", "health", "hobby", "travel", "other"];

    private const string SOURCE = "receipt-photo";

    /// <summary>The expense in <paramref name="reply"/>, or null when it names no merchant and positive total.</summary>
    public static Expense? Read(string reply, DateOnly sentOn, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var start = reply.IndexOf('{', StringComparison.Ordinal);
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(reply[start..(end + 1)]);
            var root = document.RootElement;
            var merchant = Text(root, "merchant")?.Trim();
            var total = Amount(root);
            if (string.IsNullOrEmpty(merchant) || total is not > 0)
            {
                return null;
            }

            return new Expense(
                Guid.NewGuid(), Day(Text(root, "date"), sentOn), merchant, total.Value,
                Currency(Text(root, "currency")), Category(Text(root, "category")), SOURCE, now);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? Amount(JsonElement root)
    {
        if (!root.TryGetProperty("total", out var total))
        {
            return null;
        }

        if (total.ValueKind == JsonValueKind.Number && total.TryGetDecimal(out var number))
        {
            return decimal.Round(number, 2);
        }

        var written = total.ValueKind == JsonValueKind.String
            ? total.GetString()!.Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim()
            : string.Empty;
        return decimal.TryParse(written, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? decimal.Round(parsed, 2)
            : null;
    }

    private static DateOnly Day(string? written, DateOnly sentOn) =>
        DateOnly.TryParseExact(written, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
        && day <= sentOn.AddDays(1) && day >= sentOn.AddYears(-1)
            ? day
            : sentOn;

    private static string Currency(string? written) =>
        written is { Length: 3 } code && code.All(char.IsLetter) ? code.ToUpperInvariant() : "USD";

    private static string Category(string? written) =>
        Categories.FirstOrDefault(category => string.Equals(category, written?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? "other";
}
