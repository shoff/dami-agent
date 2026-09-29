using System.Globalization;
using Dami.Contracts.Finance;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;

namespace Dami.Proactive.Vault;

/// <summary>Nightly: beliefs, lessons and this month's and last month's expenses as Markdown (B6).</summary>
public sealed class VaultExportService : IProactiveService
{
    private const int LESSONS = 200;
    private static readonly TimeSpan lessonLifetime = TimeSpan.FromDays(90);

    private readonly IConclusionLedger beliefs;
    private readonly IObservationCorpus corpus;
    private readonly IExpenseLedger expenses;
    private readonly IVault vault;
    private readonly TimeProvider clock;

    /// <summary>Creates the service.</summary>
    public VaultExportService(
        IConclusionLedger beliefs, IObservationCorpus corpus, IExpenseLedger expenses, IVault vault, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(beliefs);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(expenses);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(clock);
        this.beliefs = beliefs;
        this.corpus = corpus;
        this.expenses = expenses;
        this.vault = vault;
        this.clock = clock;
    }

    /// <inheritdoc />
    public string ServiceName => "vault-export";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = this.clock.GetUtcNow();
        var active = new List<Conclusion>();
        await foreach (var belief in this.beliefs.ActiveAsOfAsync(now, cancellationToken).ConfigureAwait(false))
        {
            active.Add(belief);
        }

        var lessons = new List<Observation>();
        await foreach (var lesson in this.corpus.FromSourceAsync("lesson", LESSONS, cancellationToken).ConfigureAwait(false))
        {
            if (now - lesson.OccurredAt <= lessonLifetime)
            {
                lessons.Add(lesson);
            }
        }

        await this.vault.WriteAsync("Beliefs.md", VaultPages.Beliefs(active, now), cancellationToken).ConfigureAwait(false);
        await this.vault.WriteAsync("Lessons.md", VaultPages.Lessons(lessons, now), cancellationToken).ConfigureAwait(false);
        var thisMonth = new DateOnly(now.Year, now.Month, 1);
        await this.MonthAsync(thisMonth.AddMonths(-1), cancellationToken).ConfigureAwait(false);
        await this.MonthAsync(thisMonth, cancellationToken).ConfigureAwait(false);
        return ProactiveResult.Did($"vault: {active.Count} belief(s), {lessons.Count} lesson(s)");
    }

    private async Task MonthAsync(DateOnly month, CancellationToken cancellationToken)
    {
        var spent = await this.expenses.BetweenAsync(month, month.AddMonths(1).AddDays(-1), cancellationToken).ConfigureAwait(false);
        if (spent.Count > 0)
        {
            await this.vault.WriteAsync(
                Path.Combine("Expenses", month.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".md"),
                VaultPages.Expenses(month, spent), cancellationToken).ConfigureAwait(false);
        }
    }
}
