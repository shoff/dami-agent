namespace Dami.Proactive.Recalls;

/// <summary>Turns medication notes into matchable drug names, and matches them. Pure.</summary>
public static class RecallTerms
{
    /// <summary>Dosing and narration words that appear beside every drug name.</summary>
    private static readonly HashSet<string> noise = new(StringComparer.Ordinal)
    {
        "started", "starting", "stopped", "stopping", "discontinued", "daily", "twice",
        "nightly", "weekly", "morning", "evening", "tablet", "tablets", "capsule",
        "capsules", "dosage", "doses", "taking", "takes", "prescribed", "increased",
        "decreased", "medication", "medications", "treatment", "after", "before",
        "surgery", "every", "other",
    };

    /// <summary>Candidate drug names from medication descriptions.</summary>
    public static HashSet<string> FromMedications(IEnumerable<string> descriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptions);

        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var description in descriptions)
        {
            foreach (var raw in description.Split(' ', ',', ';', '(', ')', '/'))
            {
                var token = raw.Trim().ToLowerInvariant();
                if (token.Length >= 5 && IsAllLetters(token) && !noise.Contains(token))
                {
                    terms.Add(token);
                }
            }
        }

        return terms;
    }

    /// <summary>The first term the text mentions, or null.</summary>
    public static string? Mentions(string text, IEnumerable<string> terms)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(terms);

        foreach (var term in terms)
        {
            if (term.Length > 0 && text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return term;
            }
        }

        return null;
    }

    private static bool IsAllLetters(string token)
    {
        foreach (var character in token)
        {
            if (!char.IsAsciiLetter(character))
            {
                return false;
            }
        }

        return true;
    }
}
