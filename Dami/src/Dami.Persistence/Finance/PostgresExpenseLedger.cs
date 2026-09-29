using Dami.Contracts.Finance;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Finance;

/// <summary>PostgreSQL store for the expense ledger (migration 043).</summary>
public sealed class PostgresExpenseLedger : IExpenseLedger
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;

    /// <summary>Creates the ledger.</summary>
    public PostgresExpenseLedger(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.schema = options.Value.SchemaName;
    }

    /// <inheritdoc />
    public async Task<bool> RecordAsync(Expense expense, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expense);
        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.schema}.expenses
                (expense_id, spent_on, merchant, total, currency, category, source, recorded_at)
            values (@id, @spentOn, @merchant, @total, @currency, @category, @source, @recorded)
            on conflict (spent_on, merchant, total) do nothing;
            """);
        command.Parameters.AddWithValue("id", expense.ExpenseId);
        command.Parameters.AddWithValue("spentOn", expense.SpentOn);
        command.Parameters.AddWithValue("merchant", expense.Merchant);
        command.Parameters.AddWithValue("total", expense.Total);
        command.Parameters.AddWithValue("currency", expense.Currency);
        command.Parameters.AddWithValue("category", expense.Category);
        command.Parameters.AddWithValue("source", expense.Source);
        command.Parameters.AddWithValue("recorded", expense.RecordedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Expense>> BetweenAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            select expense_id, spent_on, merchant, total, currency, category, source, recorded_at
              from {this.schema}.expenses
             where spent_on between @from and @to
             order by spent_on desc, recorded_at desc;
            """);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        var expenses = new List<Expense>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            expenses.Add(new Expense(
                reader.GetGuid(0),
                await reader.GetFieldValueAsync<DateOnly>(1, cancellationToken).ConfigureAwait(false),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken).ConfigureAwait(false)));
        }

        return expenses;
    }
}
