using System.Text.Json;

namespace Dami.Core.Nutrition;

/// <summary>The local vision model's estimate of a meal from a photo, or nothing.</summary>
public static class MealReader
{
    /// <summary>What the vision model is asked.</summary>
    public const string PROMPT =
        "This is a photo of food Steve is eating. Reply with only one JSON object: {\"food\": a short plain "
        + "description, \"calories\": your best estimate of the total kcal as an integer, \"protein\": your best "
        + "estimate of grams of protein as an integer, \"vitamin_k\": low, moderate or high — high for leafy greens such as "
        + "kale, spinach, collards, chard, lettuce, broccoli or brussels sprouts}. If there is no food, reply {}.";

    /// <summary>The estimate, or null when it names no food or is implausible.</summary>
    public static (string Food, int Calories, int Protein, string? VitaminK)? Read(string reply)
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
            var food = root.TryGetProperty("food", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString()!.Trim() : string.Empty;
            var calories = Whole(root, "calories");
            var protein = Whole(root, "protein");
            return food.Length > 0 && calories is > 0 and <= 5000 && protein is >= 0 and <= 400
                ? (food, calories.Value, protein.Value, VitaminK(root))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>One of low, moderate, high (ADR-0037 slice 2); anything else is unknown.</summary>
    private static string? VitaminK(JsonElement root)
    {
        var said = root.TryGetProperty("vitamin_k", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim().ToLowerInvariant()
            : null;
        return said is "low" or "moderate" or "high" ? said : null;
    }

    private static int? Whole(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? (int)Math.Round(number)
            : null;
}
