using System.Text.Json;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class TodayDigestTests
{
    [Fact]
    public void BoardQuestions_Should_Keep_Only_Blocked_Tasks_That_Name_Steve()
    {
        using var tasks = JsonDocument.Parse("""
            [{"taskId":"11111111-0000-4000-8000-000000000001","title":"Epic","status":"Open","description":"epic","subTasks":[
              {"taskId":"22222222-0000-4000-8000-000000000002","title":"A7 ADR-0001","status":"Blocked","description":"- [ ] A7 `[STEVE]`","subTasks":[]},
              {"taskId":"33333333-0000-4000-8000-000000000003","title":"E3 UDP","status":"Blocked","description":"`[BLOCKED: L-phase]`","subTasks":[]}]}]
            """);

        var questions = TodayDigest.BoardQuestions(tasks.RootElement);

        var only = Assert.Single(questions);
        Assert.Equal(("22222222", "YOURS · A7 ADR-0001"), (only.Id, only.Headline));
    }

    [Fact]
    public void CivicWeek_And_NetworkProblems_Should_Keep_Only_What_Matters()
    {
        using var civic = JsonDocument.Parse("""
            [{"factId":"44444444-0000-4000-8000-000000000004","asOf":"2026-08-26","category":"meeting","description":"Finance Committee Meeting — https://x/1","source":"lakeville-calendar"},
             {"factId":"55555555-0000-4000-8000-000000000005","asOf":"2026-08-26","category":"notice","description":"Family Flicks — https://x/2","source":"lakeville-news"},
             {"factId":"66666666-0000-4000-8000-000000000006","asOf":"2026-09-20","category":"meeting","description":"Far away — https://x/3","source":"lakeville-calendar"}]
            """);
        using var network = JsonDocument.Parse("""
            [{"factId":"77777777-0000-4000-8000-000000000007","asOf":"2026-08-25","category":"service","description":"ollama on 127.0.0.1:11434 is not listening","source":"network-collector"},
             {"factId":"88888888-0000-4000-8000-000000000008","asOf":"2026-08-25","category":"service","description":"postgresql on 127.0.0.1:5432 is listening","source":"network-collector"},
             {"factId":"99999999-0000-4000-8000-000000000009","asOf":"2026-08-24","category":"service","description":"dami-stt on 127.0.0.1:8090 is not listening","source":"network-collector"}]
            """);

        var week = TodayDigest.CivicWeek(civic.RootElement, new DateOnly(2026, 8, 25));
        var problems = TodayDigest.NetworkProblems(network.RootElement);

        Assert.Equal("CIVIC · Wed 08-26 · Finance Committee Meeting", Assert.Single(week).Headline);
        Assert.Equal("NETWORK · ollama on 127.0.0.1:11434 is not listening", Assert.Single(problems).Headline);
    }
}
