using System.Globalization;
using System.Text.Json;

namespace Dami.Proactive.Recalls;

/// <summary>One recall notice, whatever agency it came from.</summary>
public sealed record RecallNotice(
    string Source,
    string Classification,
    string Product,
    string Reason,
    DateOnly? Date,
    string Reference);

/// <summary>Reads the recall wire formats. Pure.</summary>
public static class RecallFeeds
{
    private const int PRODUCT_LENGTH = 200;
    private const int REASON_LENGTH = 160;

    /// <summary>The recalls in an openFDA enforcement response.</summary>
    public static IReadOnlyList<RecallNotice> ParseOpenFda(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var notices = new List<RecallNotice>();
            foreach (var result in results.EnumerateArray())
            {
                notices.Add(new RecallNotice(
                    source,
                    Text(result, "classification"),
                    Clip(Text(result, "product_description"), PRODUCT_LENGTH),
                    Clip(Text(result, "reason_for_recall"), REASON_LENGTH),
                    FdaDate(Text(result, "recall_initiation_date")),
                    Text(result, "recall_number")));
            }

            return notices;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The recalls in a CPSC REST response.</summary>
    public static IReadOnlyList<RecallNotice> ParseCpsc(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var notices = new List<RecallNotice>();
            foreach (var recall in document.RootElement.EnumerateArray())
            {
                notices.Add(new RecallNotice(
                    "cpsc",
                    string.Empty,
                    Clip(Text(recall, "Title") + " — " + Products(recall), PRODUCT_LENGTH),
                    Clip(Text(recall, "Description"), REASON_LENGTH),
                    DateTimeOffset.TryParse(Text(recall, "RecallDate"), out var date)
                        ? DateOnly.FromDateTime(date.UtcDateTime)
                        : null,
                    Text(recall, "URL")));
            }

            return notices;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Products(JsonElement recall)
    {
        if (!recall.TryGetProperty("Products", out var products)
            || products.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (var product in products.EnumerateArray())
        {
            var name = Text(product, "Name");
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return string.Join("; ", names);
    }

    private static DateOnly? FdaDate(string value) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string Clip(string text, int length) =>
        text.Length <= length ? text : text[..length];
}
