using System.Globalization;
using System.Text.RegularExpressions;
using Dami.Contracts.Models;
using Dami.Contracts.Nutrition;
using Dami.Contracts.Privacy;
using Dami.Core.Nutrition;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>A photo Steve calls a meal becomes an estimate in the meal log, entirely on this host (D6).</summary>
/// <remarks>
/// Food-photo logging was one of the most repeated uses in the 2026-09-29 survey. The local
/// vision model estimates; the reply says so, and carries the day's running totals. Health
/// data: the frontier never sees it, and the confirmation is ProfileDerived — Steve's DM only.
/// </remarks>
public sealed partial class DiscordMealResponder
{
    private static readonly CultureInfo numbers = CultureInfo.GetCultureInfo("en-US");

    private readonly IVisionClient vision;
    private readonly IDiscordRest rest;
    private readonly IMealLog meals;
    private readonly IEgressChannel channel;
    private readonly DiscordOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordMealResponder> logger;

    /// <summary>Creates the responder.</summary>
    public DiscordMealResponder(
        IVisionClient vision, IDiscordRest rest, IMealLog meals, IEgressChannel channel, DiscordOptions options,
        TimeProvider clock, ILogger<DiscordMealResponder> logger)
    {
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(meals);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        (this.vision, this.rest, this.meals, this.channel) = (vision, rest, meals, channel);
        (this.options, this.clock, this.logger) = (options, clock, logger);
    }

    /// <summary>Logs the meal when the message is a photo Steve calls one; false otherwise.</summary>
    public async Task<bool> TryAnswerAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var photo = message.Attachments.FirstOrDefault(attachment => attachment.IsImage);
        if (photo is null || !MealWord().IsMatch(message.Text))
        {
            return false;
        }

        var estimate = MealReader.Read(await this.ReadAsync(photo, cancellationToken).ConfigureAwait(false) ?? string.Empty);
        var text = estimate is not { } meal
            ? "I couldn't make out a meal in that photo, so nothing is logged."
            : await this.LogAsync(meal, message.ReceivedAt, cancellationToken).ConfigureAwait(false);
        await this.channel.SendAsync(
            new OutboundContent(message.ConversationId, text, ContentProvenance.ProfileDerived, Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<string?> ReadAsync(InboundAttachment photo, CancellationToken cancellationToken)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(this.options.VisionTimeout * 2);
            var bytes = await this.rest.DownloadAsync(photo.Url, budget.Token).ConfigureAwait(false);
            return await this.vision.DescribeAsync(bytes, MealReader.PROMPT, budget.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            this.logger.LogWarning(exception, "Could not read meal photo {File}", photo.FileName);
            return null;
        }
    }

    private async Task<string> LogAsync(
        (string Food, int Calories, int Protein) meal, DateTimeOffset eatenAt, CancellationToken cancellationToken)
    {
        await this.meals.RecordAsync(
            new Meal(Guid.NewGuid(), eatenAt, meal.Food, meal.Calories, meal.Protein, this.clock.GetUtcNow()), cancellationToken)
            .ConfigureAwait(false);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(this.options.CheckInTimeZone);
        var day = TimeZoneInfo.ConvertTime(eatenAt, zone).Date;
        var midnight = new DateTimeOffset(day, zone.GetUtcOffset(day));
        var today = await this.meals.BetweenAsync(midnight, midnight.AddDays(1), cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Meal logged locally");
        return $"Logged about {meal.Calories.ToString("N0", numbers)} kcal and {meal.Protein} g protein ({meal.Food}), "
            + $"an estimate from the photo. Today so far: {today.Sum(item => item.Calories).ToString("N0", numbers)} kcal, "
            + $"{today.Sum(item => item.ProteinGrams)} g protein.";
    }

    [GeneratedRegex(@"\b(ate|eating|meal|breakfast|lunch|dinner|snack|food)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MealWord();
}
