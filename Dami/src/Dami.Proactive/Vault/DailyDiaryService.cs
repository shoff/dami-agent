using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dami.Contracts.Calendar;
using Dami.Contracts.Domains;
using Dami.Contracts.Finance;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Vault;

/// <summary>Nightly: yesterday's decisions, open loops, mistakes and new facts, as a diary entry (B4).</summary>
/// <remarks>
/// OpenClaw's DREAMS.md, Letta's sleep-time agent and a Hermes user's 3 a.m. job all do this:
/// consolidate the day while nobody is using the agent, into something reviewable. Here the
/// local model writes it from what the day left behind — conversations, calendar, spending,
/// forwarded mail — and it goes both to the vault's Journal and into the corpus, where
/// reflection and recall find it. Weekly reflection is untouched. A quiet day gets no entry.
/// </remarks>
public sealed class DailyDiaryService : IProactiveService
{
    private const int CONVERSATIONS = 40;
    private const int KEPT_CHARS = 1500;

    private const string INSTRUCTIONS =
        "You keep Steve's diary. From the notes of his day below, write four short Markdown sections: "
        + "## Decisions, ## Open loops, ## What went wrong, ## New facts. Use only what the notes support; "
        + "write 'none' under an empty heading. No greeting, no advice, no invention.";

    private readonly IObservationCorpus corpus;
    private readonly IExpenseLedger expenses;
    private readonly ICalendarStore calendar;
    private readonly IDomainFactStore facts;
    private readonly IChatClient local;
    private readonly IVault vault;
    private readonly VaultOptions vaultOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<DailyDiaryService> logger;

    /// <summary>Creates the service.</summary>
    public DailyDiaryService(
        IObservationCorpus corpus, IExpenseLedger expenses, ICalendarStore calendar, IDomainFactStore facts,
        IChatClient local, IVault vault, IOptions<VaultOptions> vaultOptions, TimeProvider clock, ILogger<DailyDiaryService> logger)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(expenses);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(vaultOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        (this.corpus, this.expenses, this.calendar, this.facts) = (corpus, expenses, calendar, facts);
        (this.local, this.vault, this.vaultOptions, this.clock, this.logger) = (local, vault, vaultOptions.Value, clock, logger);
    }

    /// <inheritdoc />
    public string ServiceName => "daily-diary";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(this.vaultOptions.TimeZone);
        var today = TimeZoneInfo.ConvertTime(this.clock.GetUtcNow(), zone).Date;
        var yesterday = today.AddDays(-1);
        var from = new DateTimeOffset(yesterday, zone.GetUtcOffset(yesterday));
        var notes = await this.NotesAsync(from, from.AddDays(1), DateOnly.FromDateTime(yesterday), zone, cancellationToken).ConfigureAwait(false);
        if (notes.Length == 0)
        {
            return ProactiveResult.Did("a quiet day; no diary entry");
        }

        var entry = (await this.local.CompleteAsync($"{INSTRUCTIONS}\n\n{notes}", cancellationToken).ConfigureAwait(false)).Trim();
        var title = yesterday.ToString("dddd, MMMM d yyyy", CultureInfo.InvariantCulture);
        await this.vault.WriteAsync(
            Path.Combine("Journal", yesterday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md"),
            $"# {title}\n\n{entry}\n", cancellationToken).ConfigureAwait(false);
        await this.corpus.RecordAsync(
            new Observation(DiaryId(yesterday), from.AddHours(23), "diary", $"Diary, {title}: {Bound(entry)}"),
            cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Diary written for {Day:yyyy-MM-dd}", yesterday);
        return ProactiveResult.Did($"diary written for {yesterday:yyyy-MM-dd}");
    }

    private async Task<string> NotesAsync(
        DateTimeOffset from, DateTimeOffset to, DateOnly day, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var sections = new List<(string Heading, List<string> Lines)>
        {
            ("Conversations", await this.ConversationsAsync(from, to, cancellationToken).ConfigureAwait(false)),
            ("Calendar", [.. (await this.calendar.BetweenAsync(from, to, cancellationToken).ConfigureAwait(false))
                .Select(item => $"{(item.AllDay ? "all day" : TimeZoneInfo.ConvertTime(item.StartsAt, zone).ToString("h:mm tt", CultureInfo.InvariantCulture))} {item.Summary}")]),
            ("Spent", [.. (await this.expenses.BetweenAsync(day, day, cancellationToken).ConfigureAwait(false))
                .Select(item => $"{item.Merchant} {item.Total.ToString("C", CultureInfo.GetCultureInfo("en-US"))} ({item.Category})")]),
            ("Forwarded mail", await this.MailAsync(day, cancellationToken).ConfigureAwait(false)),
        };
        var notes = new StringBuilder();
        foreach (var (heading, lines) in sections.Where(section => section.Lines.Count > 0))
        {
            notes.Append(heading).Append(":\n").AppendJoin('\n', lines.Select(line => "- " + line)).Append("\n\n");
        }

        return notes.ToString().Trim();
    }

    private async Task<List<string>> ConversationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await foreach (var observation in this.corpus.BetweenAsync(from, to, cancellationToken).ConfigureAwait(false))
        {
            if (observation.Source is "chat" or "lesson" && lines.Count < CONVERSATIONS)
            {
                lines.Add(observation.Body.ReplaceLineEndings(" "));
            }
        }

        return lines;
    }

    private async Task<List<string>> MailAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await foreach (var fact in this.facts.BetweenAsync("mail", day, day, 20, cancellationToken).ConfigureAwait(false))
        {
            lines.Add(fact.Description);
        }

        return lines;
    }

    /// <summary>One id per day, so a rerun of the night does not add a second entry to the corpus.</summary>
    private static Guid DiaryId(DateTime day) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("diary:" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))[..16]);

    private static string Bound(string text) => text.Length <= KEPT_CHARS ? text : text[..KEPT_CHARS] + "…";
}
