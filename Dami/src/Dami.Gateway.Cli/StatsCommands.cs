using Npgsql;

namespace Dami.Gateway.Cli;

/// <summary>One screen of what the system has been doing — including D-011's number.</summary>
public sealed class StatsCommands
{
    private readonly NpgsqlDataSource dataSource;

    /// <summary>Creates the commands.</summary>
    public StatsCommands(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        this.dataSource = dataSource;
    }

    private static readonly IReadOnlyList<(string Title, string Sql)> sections =
    [
        ("corpus",
         "select source || ': ' || count(*) from dami.observations group by source order by count(*) desc"),
        ("beliefs",
         """
         select 'active: ' || count(*) filter (where retracted_at is null)
             || ' · retracted: ' || count(*) filter (where retracted_at is not null)
             || ' · corrections: ' || count(*) filter (where supersedes_id is not null)
         from dami.conclusions
         """),
        ("surfacings",
         """
         select status || ': ' || count(*)
             || coalesce(' (' || count(*) filter (where feedback is not null) || ' with feedback)', '')
         from dami.surfacings group by status order by status
         """),
        ("passes, last 7 days",
         """
         select service_name || ': ' || count(*) || ' run(s), ' ||
             count(*) filter (where status = 'Failed') || ' failed'
         from dami.proactive_runs
         where ran_at > now() - interval '7 days'
         group by service_name order by service_name
         """),
        ("pushback rate, this quarter (D-011: a falling number is the alarm)",
         """
         select 'challenges: ' || count(*)
             || ' · accepted: ' || count(*) filter (where outcome = 'Accepted')
         from dami.pushbacks where occurred_at > now() - interval '91 days'
         """),
        ("egress, last 7 days (everything that left, or tried to)",
         """
         select type || ': ' || count(*) from dami.execution_events
         where type in ('EgressRequested','EgressCompleted','EgressRefused')
           and occurred_at > now() - interval '7 days'
         group by type order by type
         """),
    ];

    /// <summary>Prints the system's vital signs.</summary>
    public async Task<int> ShowAsync(CancellationToken cancellationToken)
    {
        foreach (var (title, sql) in sections)
        {
            await this.SectionAsync(title, sql, cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    private async Task SectionAsync(string title, string sql, CancellationToken cancellationToken)
    {
        Console.WriteLine(title);

        await using var command = this.dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var any = false;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            any = true;
            Console.WriteLine($"  {reader.GetString(0)}");
        }

        if (!any)
        {
            Console.WriteLine("  (none)");
        }

        Console.WriteLine();
    }
}
