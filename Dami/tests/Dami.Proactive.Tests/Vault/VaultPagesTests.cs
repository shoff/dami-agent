using Dami.Contracts.Finance;
using Dami.Contracts.Memory;
using Dami.Proactive.Vault;
using Xunit;

namespace Dami.Proactive.Tests.Vault;

/// <summary>Dami's memory as plain Markdown Steve can read, grep and keep (docs/agent-landscape-2026-09.md B6).</summary>
public sealed class VaultPagesTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Beliefs_Should_Be_Grouped_By_Subject_With_Their_Correctable_Ids()
    {
        var id = Guid.Parse("6f1c2e3d-0000-0000-0000-000000000001");
        var page = VaultPages.Beliefs(
        [
            new Conclusion(id, null, "work", "Builds momentum by shipping vertical slices", 0.8, ConclusionSource.ReflectionPass, now.AddDays(-30)),
            new Conclusion(Guid.NewGuid(), null, "health", "Takes warfarin", 0.95, ConclusionSource.Conversation, now.AddDays(-60)),
        ], now);

        Assert.StartsWith("# What Dami believes", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("## health", StringComparison.Ordinal) < page.IndexOf("## work", StringComparison.Ordinal));
        Assert.Contains("- Builds momentum by shipping vertical slices — 80%, since 2026-08-30 (`6f1c2e3d`)", page, StringComparison.Ordinal);
        Assert.Contains("dami retract", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Lessons_Should_Show_What_Steve_Said_They_Came_From()
    {
        var page = VaultPages.Lessons(
        [
            new Observation(Guid.NewGuid(), now.AddDays(-2), "lesson", "Keep weather to one line.",
                new Dictionary<string, string> { ["quote"] = "one line for weather please" }),
        ], now);

        Assert.Contains("- Keep weather to one line. — from \"one line for weather please\", 2026-09-27", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Month_Of_Expenses_Should_Be_A_Table_With_Category_Totals()
    {
        var page = VaultPages.Expenses(new DateOnly(2026, 9, 1),
        [
            new Expense(Guid.NewGuid(), new DateOnly(2026, 9, 28), "Costco", 84.12m, "USD", "groceries", "receipt-photo", now),
            new Expense(Guid.NewGuid(), new DateOnly(2026, 9, 3), "Aldi", 31.07m, "USD", "groceries", "mail", now),
            new Expense(Guid.NewGuid(), new DateOnly(2026, 9, 10), "Shell", 40m, "USD", "fuel", "receipt-photo", now),
        ]);

        Assert.StartsWith("# Expenses — September 2026", page, StringComparison.Ordinal);
        Assert.Contains("| 2026-09-03 | Aldi | groceries | $31.07 | mail |", page, StringComparison.Ordinal);
        Assert.Contains("- groceries: $115.19", page, StringComparison.Ordinal);
        Assert.Contains("**Total: $155.19**", page, StringComparison.Ordinal);
    }
}
