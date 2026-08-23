using Dami.Persistence.Events;
using Xunit;

namespace Dami.Persistence.Tests.Events;

/// <summary>The SQL projections, asserted without a database.</summary>
/// <remarks>
/// Standards §10 requires SQL be exposed as pure static builders precisely so this is
/// possible. These assertions are about shape, not about text: they check the properties
/// the store depends on rather than pinning a string nobody may reformat.
/// </remarks>
public sealed class ExecutionEventStoreSqlTests
{
    private const string TABLE = "dami_test.execution_events";

    [Fact]
    public void BuildAppendSql_Should_Qualify_The_Table_From_The_Configured_Schema()
    {
        var sql = PostgresExecutionEventStore.BuildAppendSql(TABLE);

        Assert.Contains(TABLE, sql, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAppendSql_Should_Be_Idempotent_On_Event_Id()
    {
        var sql = PostgresExecutionEventStore.BuildAppendSql(TABLE);

        Assert.Contains("on conflict (event_id) do nothing", sql, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAppendSql_Should_Return_A_Sequence_Even_When_The_Insert_Conflicts()
    {
        var sql = PostgresExecutionEventStore.BuildAppendSql(TABLE);

        Assert.Contains("union all", sql, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAppendSql_Should_Use_Parameters_Rather_Than_Interpolated_Values()
    {
        var sql = PostgresExecutionEventStore.BuildAppendSql(TABLE);

        Assert.Contains("@event_id", sql, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReplaySql_Should_Order_By_Sequence()
    {
        var sql = PostgresExecutionEventStore.BuildReplaySql(TABLE);

        Assert.Contains("order by sequence", sql, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReadSinceSql_Should_Bound_The_Result_Set()
    {
        var sql = PostgresExecutionEventStore.BuildReadSinceSql(TABLE);

        Assert.Contains("limit @limit", sql, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BuildAppendSql")]
    [InlineData("BuildReplaySql")]
    [InlineData("BuildReadSinceSql")]
    public void Builders_Should_Reject_A_Null_Table(string builder)
    {
        var method = typeof(PostgresExecutionEventStore).GetMethod(builder)!;

        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method.Invoke(null, [null]));

        Assert.IsType<System.ArgumentNullException>(thrown.InnerException);
    }
}
