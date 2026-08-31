using System.Globalization;

namespace Dami.Gui;

/// <summary>Turns whatever was right-clicked into words a model can reason about.</summary>
/// <remarks>
/// The useful context is not the control — a <c>TextBlock</c> tells a model nothing — it is
/// the view model behind it. Every row in this application is already backed by something
/// meaningful: a pass, a service, a surfacing, a board task. Describing that is the whole
/// feature; the popup is just where the answer lands.
///
/// Pure and static so the descriptions can be read and argued with in tests. What goes
/// into a prompt is the part worth being deliberate about, and a description that quietly
/// omits the alert count produces an answer that confidently misses the point.
/// </remarks>
public static class AskContext
{
    /// <summary>What the clicked element is, in one paragraph, or empty if it is nothing.</summary>
    public static string Describe(object? model, string visibleText)
    {
        ArgumentNullException.ThrowIfNull(visibleText);

        return model switch
        {
            WorkerRow service => Service(service),
            WorkerRun run => Run(run),
            PassEvent moment => Moment(moment),
            SidebarItem item => Item(item),
            ActivitySeries series => Series(series),
            FitnessInsight insight => Insight(insight),
            FitnessSeries fitness => Fitness(fitness),
            FitnessSessionRow session => $"a recent session on the health dashboard: "
                + $"{session.When}, {session.Title}, {session.Detail}.",
            Message message => $"a line in the conversation, from {message.Who}: {message.Body}",
            _ => visibleText.Trim(),
        };
    }

    /// <summary>
    /// The request sent to the runtime: what is on screen, then what is being asked about
    /// it, then how to answer.
    /// </summary>
    /// <remarks>
    /// The instruction is last because it is the part a model most reliably obeys, and it
    /// says "say so" rather than "guess": a confident answer about a pass the model cannot
    /// see is worse than no answer, and this whole application is a machine for not doing
    /// that.
    /// </remarks>
    public static string Prompt(string context, string question)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        return $"""
            Steve is looking at Dami's own desktop client and has asked about one element
            on screen.

            The element:
            {(context.Length == 0 ? "(nothing identifiable was under the pointer)" : context)}

            His question:
            {question.Trim()}

            Answer just that, briefly, using the element above. If it does not contain
            enough to answer, say what is missing rather than guessing.
            """;
    }

    private static string Service(WorkerRow service) =>
        $"a proactive service in the workers view: {service.ServiceName}. "
        + $"Last run {service.LastStatus.ToLowerInvariant()}, {service.Age}, over {service.Runs} run(s). "
        + $"Schedule: {service.Schedule}. Totals: {service.Totals}."
        + (service.HasAlerts
            ? " Some of its passes were refused by a server while still reporting as completed."
            : string.Empty);

    private static string Run(WorkerRun run) =>
        $"one pass of a proactive service, run at {run.When}, {run.Outcome}, "
        + $"trace {run.Trace}. It produced {run.Produced} item(s), made {run.Egress} "
        + $"outbound call(s) and raised {run.Alerts} alert(s), taking {run.Elapsed}.";

    private static string Moment(PassEvent moment) =>
        $"one event inside a replayed pass: {moment.Type} at {moment.Offset} "
        + $"(status {moment.Status}), labelled: {moment.Label}."
        + (moment.IsAlert ? " It is flagged as wanting a look." : string.Empty);

    private static string Item(SidebarItem item) =>
        $"an item in Dami's attention list ({item.Kind}): {item.Headline}. "
        + $"Provenance: {item.Detail}."
        + (item.Body.Length > 0 ? $" Its body or link: {item.Body}" : string.Empty);

    private static string Series(ActivitySeries series) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"a series on the runtime activity chart: {series.Name}, currently {series.Now} "
            + $"per interval, peaking at {series.Peak} over the window shown.");

    private static string Insight(FitnessInsight insight) =>
        $"a suggestion on the health dashboard ({insight.Kind}), computed from the "
        + $"fitness log, not by a model: {insight.Text}. Basis: {insight.Detail}.";

    private static string Fitness(FitnessSeries series) =>
        $"a chart on the health dashboard: {series.Name}, latest {series.Now}, "
        + $"plotted between {series.Floor} and {series.Ceiling}.";
}
