using Dami.Contracts.Code;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Providers.Tests;

public sealed class CodexCodeWorkerTests
{
    private const string REPOSITORY = "/repo/dami-agent";
    private const string WORKTREES = "/var/dami/code";

    /// <summary>Records every command and answers each by its first argument.</summary>
    private sealed class RecordingCommands : ICommandRunner
    {
        public List<(string Directory, string Executable, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        /// <summary>What <c>git status --porcelain</c> reports in the worktree.</summary>
        public string Status { get; set; } = " M Dami/src/Dami.Gui/AboutWindow.cs";

        public int BuildExitCode { get; set; }

        public string BranchList { get; set; } = string.Empty;

        public Task<CommandResult> RunAsync(
            string workingDirectory, string executable, IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken cancellationToken)
        {
            this.Calls.Add((workingDirectory, executable, arguments));
            var result = (executable, arguments[0]) switch
            {
                ("git", "status") => new CommandResult(0, this.Status),
                ("git", "diff") => new CommandResult(0, " 1 file changed, 2 insertions(+), 1 deletion(-)"),
                ("git", "for-each-ref") => new CommandResult(0, this.BranchList),
                ("dotnet", _) => new CommandResult(this.BuildExitCode, this.BuildExitCode == 0 ? "ok" : "error CS0001: bad"),
                _ => new CommandResult(0, string.Empty),
            };
            return Task.FromResult(result);
        }
    }

    private readonly ICodexProcess codex = Substitute.For<ICodexProcess>();
    private readonly RecordingCommands commands = new();
    private readonly IEgressBudget budget = Substitute.For<IEgressBudget>();
    private readonly IExecutionEventStore events = Substitute.For<IExecutionEventStore>();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 9, 16, 18, 30, 0, TimeSpan.Zero));
    private readonly CodeWorkOptions options = new()
    {
        Enabled = true,
        Repository = REPOSITORY,
        WorktreeRoot = WORKTREES,
    };

    public CodexCodeWorkerTests()
    {
        this.codex.RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns("Fixed the typo.");
        this.budget.FindRefusalAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
    }

    private CodexCodeWorker Create() => new(
        this.codex, this.commands, Options.Create(new CodexOptions { Enabled = true, BinaryPath = "/bin/codex" }),
        Options.Create(this.options), this.budget, this.events, this.clock, NullLogger<CodexCodeWorker>.Instance);

    private static CodeChangeRequest Request(string task = "Fix the typo in the About window") =>
        new(task, Guid.NewGuid(), ExecutionOrigin.UserTurn);

    private IReadOnlyList<string> CodexArguments() =>
        (IReadOnlyList<string>)this.codex.ReceivedCalls().Single().GetArguments()[1]!;

    private static string After(IReadOnlyList<string> arguments, string flag) =>
        arguments[arguments.ToList().IndexOf(flag) + 1];

    [Fact]
    public void Should_Reject_A_Null_Process()
    {
        Assert.Throws<ArgumentNullException>(() => new CodexCodeWorker(
            null!, this.commands, Options.Create(new CodexOptions()), Options.Create(this.options),
            this.budget, this.events, this.clock, NullLogger<CodexCodeWorker>.Instance));
    }

    [Fact]
    public void Enabled_Should_Follow_The_Option()
    {
        this.options.Enabled = false;

        Assert.False(this.Create().Enabled);
    }

    [Fact]
    public async Task Should_Refuse_When_Code_Work_Is_Off()
    {
        this.options.Enabled = false;

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => this.Create().ChangeAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Refuse_When_The_Subscription_Is_Off()
    {
        var worker = new CodexCodeWorker(
            this.codex, this.commands, Options.Create(new CodexOptions { Enabled = false }),
            Options.Create(this.options), this.budget, this.events, this.clock, NullLogger<CodexCodeWorker>.Instance);

        await Assert.ThrowsAsync<EgressRefusedException>(() => worker.ChangeAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Refuse_When_The_Budget_Is_Spent()
    {
        this.budget.FindRefusalAsync(Arg.Any<CancellationToken>()).Returns("egress budget exhausted");

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => this.Create().ChangeAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Touch_Nothing_When_Refused()
    {
        this.options.Enabled = false;

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => this.Create().ChangeAsync(Request(), CancellationToken.None));

        Assert.Empty(this.commands.Calls);
        Assert.Empty(this.codex.ReceivedCalls());
    }

    [Fact]
    public async Task Should_Add_A_Worktree_On_A_Stamped_Branch_From_The_Base()
    {
        await this.Create().ChangeAsync(Request(), CancellationToken.None);

        var add = this.commands.Calls.First(call => call.Executable == "git" && call.Arguments[0] == "worktree");
        Assert.Equal(REPOSITORY, add.Directory);
        Assert.Equal(
            ["worktree", "add", "-b", "dami/20260916-1830-fix-the-typo-in-the-about-window",
                $"{WORKTREES}/20260916-1830-fix-the-typo-in-the-about-window", "main"],
            add.Arguments);
    }

    [Fact]
    public async Task Should_Run_Codex_In_The_Worktree_With_Workspace_Write()
    {
        await this.Create().ChangeAsync(Request(), CancellationToken.None);

        var arguments = this.CodexArguments();
        Assert.Equal("workspace-write", After(arguments, "--sandbox"));
        Assert.Equal($"{WORKTREES}/20260916-1830-fix-the-typo-in-the-about-window", After(arguments, "--cd"));
    }

    [Fact]
    public async Task Should_Never_Run_Codex_In_The_Main_Working_Tree()
    {
        // Two agents already share the main tree (runbook §7); a third writing into it
        // unannounced is the collision the worktree exists to prevent.
        await this.Create().ChangeAsync(Request(), CancellationToken.None);

        Assert.NotEqual(REPOSITORY, After(this.CodexArguments(), "--cd"));
    }

    [Fact]
    public async Task Should_Put_The_Task_In_The_Prompt()
    {
        await this.Create().ChangeAsync(Request(), CancellationToken.None);

        Assert.Contains(this.CodexArguments(), argument => argument.Contains("Fix the typo in the About window", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_Tell_The_Agent_Not_To_Commit_Push_Or_Add_Packages()
    {
        // The commit is this host's, after its own build; packages are AGENTS.md's rule.
        await this.Create().ChangeAsync(Request(), CancellationToken.None);

        var prompt = this.CodexArguments()[^1];
        Assert.Contains("Do not commit", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not push", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not add packages", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Report_No_Change_When_The_Tree_Is_Clean()
    {
        this.commands.Status = string.Empty;

        var change = await this.Create().ChangeAsync(Request(), CancellationToken.None);

        Assert.False(change.Changed);
        Assert.False(change.Committed);
    }

    [Fact]
    public async Task Should_Not_Build_Or_Commit_When_The_Tree_Is_Clean()
    {
        this.commands.Status = string.Empty;

        await this.Create().ChangeAsync(Request(), CancellationToken.None);

        Assert.DoesNotContain(this.commands.Calls, call => call.Executable == "dotnet");
        Assert.DoesNotContain(this.commands.Calls, call => call.Arguments[0] == "commit");
    }

    [Fact]
    public async Task Should_Build_In_The_Worktree_When_Files_Changed()
    {
        await this.Create().ChangeAsync(Request(), CancellationToken.None);

        var build = this.commands.Calls.Single(call => call.Executable == "dotnet");
        Assert.Equal($"{WORKTREES}/20260916-1830-fix-the-typo-in-the-about-window", build.Directory);
        Assert.Equal(["build", "Dami/Dami.sln", "-nologo", "-v", "quiet"], build.Arguments);
    }

    [Fact]
    public async Task Should_Commit_On_The_Branch_When_Files_Changed()
    {
        var change = await this.Create().ChangeAsync(Request(), CancellationToken.None);

        var commit = this.commands.Calls.Single(call => call.Arguments[0] == "commit");
        Assert.True(change.Committed);
        Assert.Equal(["commit", "-q", "-m", "Fix the typo in the About window"], commit.Arguments);
    }

    [Fact]
    public async Task Should_Report_A_Failed_Build_And_Still_Keep_The_Work()
    {
        // A branch with a broken build is still worth more than a deleted one: Steve can
        // see what was tried. The report says so instead of pretending.
        this.commands.BuildExitCode = 1;

        var change = await this.Create().ChangeAsync(Request(), CancellationToken.None);

        Assert.StartsWith("dotnet build FAILED", change.BuildOutcome, StringComparison.Ordinal);
        Assert.True(change.Committed);
    }

    [Fact]
    public async Task Should_Report_The_Diffstat_Against_The_Base()
    {
        var change = await this.Create().ChangeAsync(Request(), CancellationToken.None);

        Assert.Equal("1 file changed, 2 insertions(+), 1 deletion(-)", change.DiffStat);
    }

    [Fact]
    public async Task Should_Return_The_Agents_Summary()
    {
        var change = await this.Create().ChangeAsync(Request(), CancellationToken.None);

        Assert.Equal("Fixed the typo.", change.Summary);
    }

    [Fact]
    public async Task Should_Record_A_Completed_Egress_Without_The_Task_Text()
    {
        await this.Create().ChangeAsync(Request(), CancellationToken.None);

        await this.events.Received().AppendAsync(
            Arg.Is<ExecutionEvent>(e => e.Type == ExecutionEventType.EgressCompleted
                && !e.Label.Contains("typo", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Record_EgressFailed_When_Codex_Fails()
    {
        this.codex.RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new TimeoutException("codex exceeded 360s"));

        await Assert.ThrowsAsync<TimeoutException>(() => this.Create().ChangeAsync(Request(), CancellationToken.None));

        await this.events.Received().AppendAsync(
            Arg.Is<ExecutionEvent>(e => e.Type == ExecutionEventType.EgressFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Fail_When_The_Worktree_Cannot_Be_Added()
    {
        var failing = Substitute.For<ICommandRunner>();
        failing.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(128, "fatal: a branch named 'dami/x' already exists"));
        var worker = new CodexCodeWorker(
            this.codex, failing, Options.Create(new CodexOptions { Enabled = true }), Options.Create(this.options),
            this.budget, this.events, this.clock, NullLogger<CodexCodeWorker>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ChangeAsync(Request(), CancellationToken.None));

        Assert.Empty(this.codex.ReceivedCalls());
    }

    [Fact]
    public async Task Explain_Should_Run_Read_Only_In_The_Repository()
    {
        await this.Create().ExplainAsync(
            new CodeQuestion("how does the gallery work", Guid.NewGuid(), ExecutionOrigin.UserTurn), CancellationToken.None);

        var arguments = this.CodexArguments();
        Assert.Equal("read-only", After(arguments, "--sandbox"));
        Assert.Equal(REPOSITORY, After(arguments, "--cd"));
    }

    [Fact]
    public async Task Explain_Should_Not_Touch_Git()
    {
        await this.Create().ExplainAsync(
            new CodeQuestion("how does the gallery work", Guid.NewGuid(), ExecutionOrigin.UserTurn), CancellationToken.None);

        Assert.Empty(this.commands.Calls);
    }

    [Fact]
    public async Task Explain_Should_Refuse_When_Code_Work_Is_Off()
    {
        this.options.Enabled = false;

        await Assert.ThrowsAsync<EgressRefusedException>(() => this.Create().ExplainAsync(
            new CodeQuestion("how does the gallery work", Guid.NewGuid(), ExecutionOrigin.UserTurn), CancellationToken.None));
    }

    [Fact]
    public async Task ListChanges_Should_Parse_Branches_With_Their_Diffstat()
    {
        this.commands.BranchList =
            "dami/20260916-1830-fix-the-typo\t2026-09-16T18:31:00+00:00\tFix the typo\n";

        var branches = await this.Create().ListChangesAsync(CancellationToken.None);

        Assert.Equal(
            [new CodeBranch("dami/20260916-1830-fix-the-typo", new DateTimeOffset(2026, 9, 16, 18, 31, 0, TimeSpan.Zero),
                "Fix the typo", "1 file changed, 2 insertions(+), 1 deletion(-)")],
            branches);
    }

    [Fact]
    public async Task ListChanges_Should_Ask_Git_For_The_Prefixed_Refs_Newest_First()
    {
        await this.Create().ListChangesAsync(CancellationToken.None);

        var list = this.commands.Calls.Single(call => call.Arguments[0] == "for-each-ref");
        Assert.Equal(REPOSITORY, list.Directory);
        Assert.Contains("--sort=-committerdate", list.Arguments);
        Assert.Contains("refs/heads/dami/", list.Arguments);
    }
}
