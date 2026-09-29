using System.Globalization;
using System.Text;
using Dami.Contracts.Finance;
using Dami.Contracts.Memory;

namespace Dami.Proactive.Vault;

/// <summary>Dami's memory rendered as plain Markdown pages (docs/agent-landscape-2026-09.md B6).</summary>
/// <remarks>
/// The memory users trust most in the 2026-09-29 survey is the one they can open: an
/// Obsidian vault of Markdown files, readable without the agent and portable across models.
/// These pages are a mirror, rebuilt nightly; the ledgers stay the record, and each belief
/// carries the id that corrects it there.
/// </remarks>
public static class VaultPages
{
    private static readonly CultureInfo money = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Active beliefs, grouped by subject.</summary>
    public static string Beliefs(IReadOnlyList<Conclusion> beliefs, DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(beliefs);
        var page = new StringBuilder("# What Dami believes about Steve\n\n")
            .Append(CultureInfo.InvariantCulture, $"_Exported {Day(exportedAt)}. A mirror: correct a belief with `dami correct <id>` or `dami retract <id> <reason>`._\n");
        foreach (var subject in beliefs.GroupBy(belief => belief.Subject).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            page.Append(CultureInfo.InvariantCulture, $"\n## {subject.Key}\n\n");
            foreach (var belief in subject.OrderByDescending(item => item.Confidence))
            {
                page.Append(CultureInfo.InvariantCulture,
                    $"- {belief.Statement} — {Math.Round(belief.Confidence * 100)}%, since {Day(belief.ConcludedAt)} (`{belief.ConclusionId.ToString("N")[..8]}`)\n");
            }
        }

        return page.ToString();
    }

    /// <summary>The standing lessons, newest first, with the words they came from.</summary>
    public static string Lessons(IReadOnlyList<Observation> lessons, DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(lessons);
        var page = new StringBuilder("# Lessons Dami follows\n\n")
            .Append(CultureInfo.InvariantCulture, $"_Exported {Day(exportedAt)}. A lesson lapses after 90 days unless Steve says it again._\n\n");
        foreach (var lesson in lessons.OrderByDescending(item => item.OccurredAt))
        {
            var quote = lesson.Metadata is { } metadata && metadata.TryGetValue("quote", out var said) ? $" — from \"{said}\"," : " —";
            page.Append(CultureInfo.InvariantCulture, $"- {lesson.Body}{quote} {Day(lesson.OccurredAt)}\n");
        }

        return page.ToString();
    }

    /// <summary>One month of expenses as a table, with totals by category.</summary>
    public static string Expenses(DateOnly month, IReadOnlyList<Expense> expenses)
    {
        ArgumentNullException.ThrowIfNull(expenses);
        var page = new StringBuilder("# Expenses — " + month.ToString("MMMM yyyy", CultureInfo.InvariantCulture) + "\n\n")
            .Append("| Day | Merchant | Category | Total | From |\n|---|---|---|---|---|\n");
        foreach (var expense in expenses.OrderBy(item => item.SpentOn).ThenBy(item => item.Merchant, StringComparer.Ordinal))
        {
            page.Append(CultureInfo.InvariantCulture,
                $"| {expense.SpentOn:yyyy-MM-dd} | {expense.Merchant} | {expense.Category} | {expense.Total.ToString("C", money)} | {expense.Source} |\n");
        }

        page.Append("\n## By category\n\n");
        foreach (var category in expenses.GroupBy(item => item.Category).OrderByDescending(group => group.Sum(item => item.Total)))
        {
            page.Append(CultureInfo.InvariantCulture, $"- {category.Key}: {category.Sum(item => item.Total).ToString("C", money)}\n");
        }

        return page.Append(CultureInfo.InvariantCulture, $"\n**Total: {expenses.Sum(item => item.Total).ToString("C", money)}**\n").ToString();
    }

    private static string Day(DateTimeOffset at) => at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
