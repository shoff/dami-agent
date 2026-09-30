using System.Text.Json;

namespace Dami.Core.Nutrition;

/// <summary>The local vision model's estimate of a meal from a photo, or nothing.</summary>
public static class MealReader
{
    /// <summary>What the vision model is asked.</summary>
    public const string PROMPT =
        "This is a photo of food Steve is eating. Reply with only one JSON object: {\"food\": a short plain "
        + "description, \"calories\": your best estimate of the total kcal as an integer, \"protein\": your best "
        + "estimate of grams of protein as an integer}. If there is no food, reply {}.";

    /// <summary>The estimate, or null when it names no food or is implausible.</summary>
    public static (string Food, int Calories, int Protein)? Read(string reply)
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
                ? (food, calories.Value, protein.Value)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? Whole(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? (int)Math.Round(number)
            : null;
}
