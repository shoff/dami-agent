using System.Globalization;
using System.Text.RegularExpressions;

namespace Dami.Core.Anticoag;

/// <summary>A drug or supplement known to interact with warfarin, and how.</summary>
public sealed record Interactor(string Name, string Effect);

/// <summary>What in Steve's words is an INR reading, a dose change, or a warfarin interactor (ADR-0037).</summary>
/// <remarks>
/// Deliberately narrow: a question, a target range or an unrelated number must never become a
/// reading beside his real ones, and "took my 5 mg" is not a change. The interactor list is
/// short, well established (FDA warfarin labelling) and not exhaustive; a match is a prompt
/// to ask the prescriber, never a verdict.
/// </remarks>
public static partial class AnticoagCapture
{
    private static readonly (Regex Pattern, Interactor Interactor)[] interactors =
    [
        (Word("bactrim|septra|sulfamethoxazole|trimethoprim"), new("Bactrim", "can raise INR")),
        (Word("fluconazole|diflucan"), new("fluconazole", "can raise INR")),
        (Word("metronidazole|flagyl"), new("metronidazole", "can raise INR")),
        (Word("ciprofloxacin|cipro"), new("ciprofloxacin", "can raise INR")),
        (Word("levofloxacin|levaquin"), new("levofloxacin", "can raise INR")),
        (Word("clarithromycin|erythromycin"), new("clarithromycin/erythromycin", "can raise INR")),
        (Word("amiodarone"), new("amiodarone", "can raise INR")),
        (Word("rifampin|rifampicin"), new("rifampin", "can lower INR sharply")),
        (Word("carbamazepine|tegretol"), new("carbamazepine", "can lower INR")),
        (new Regex(@"\bst\.?\s*john'?s\s*wort\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), new("St John's wort", "can lower INR")),
        (Word("ibuprofen|advil|motrin"), new("ibuprofen", "adds bleeding risk")),
        (Word("naproxen|aleve"), new("naproxen", "adds bleeding risk")),
        (Word("aspirin"), new("aspirin", "adds bleeding risk")),
        (Word("clopidogrel|plavix"), new("clopidogrel", "adds bleeding risk")),
        (Word("ginkgo"), new("ginkgo", "adds bleeding risk")),
        (Word("antibiotics?"), new("an antibiotic", "many antibiotics change INR")),
    ];

    /// <summary>The reading and its day, or null when the words are not a reported reading.</summary>
    public static (decimal Inr, DateOnly OnDay)? Reading(string text, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(text);
        var said = text.Trim();
        if (said.EndsWith('?') || NotAReading().IsMatch(said) || ReadingPattern().Match(said) is not { Success: true } match)
        {
            return null;
        }

        var inr = decimal.Parse(match.Groups["v"].Value, CultureInfo.InvariantCulture);
        return inr is >= 0.5m and <= 10m ? (inr, Yesterday().IsMatch(said) ? today.AddDays(-1) : today) : null;
    }

    /// <summary>The words verbatim when they report a dose change; null otherwise.</summary>
    public static string? DoseChange(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var said = text.Trim();
        return !said.EndsWith('?') && Milligrams().IsMatch(said) && ChangeWord().IsMatch(said) && DoseContext().IsMatch(said)
            ? said
            : null;
    }

    /// <summary>The first known interactor named, or null.</summary>
    public static Interactor? Interactor(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return interactors.FirstOrDefault(entry => entry.Pattern.IsMatch(text)).Interactor;
    }

    private static Regex Word(string alternatives) =>
        new($@"\b(?:{alternatives})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\bINR\b(?:\s*(?:is|was|came back|reading|of|:|=))*\s*(?<v>\d(?:\.\d{1,2})?)\b(?!\s*(?:-|–|to)\s*\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReadingPattern();

    [GeneratedRegex(@"\b(?:target|range|should|normal|goal)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotAReading();

    [GeneratedRegex(@"\byesterday\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Yesterday();

    [GeneratedRegex(@"\d+(?:\.\d+)?\s*mg\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Milligrams();

    [GeneratedRegex(@"\b(?:changed?|new|lowered|raised|increased|decreased|switched|put me on|now on|dose is)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChangeWord();

    [GeneratedRegex(@"\b(?:warfarin|coumadin|jantoven|dose|clinic)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DoseContext();
}
