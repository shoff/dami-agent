using System.Text;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Providers.Tests;

public sealed class CodexSubscriptionImageGeneratorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"dami-image-sub-{Guid.NewGuid():N}");
    private readonly ICodexProcess process = Substitute.For<ICodexProcess>();
    private readonly IEgressBudget budget = Substitute.For<IEgressBudget>();
    private readonly IExecutionEventStore events = Substitute.For<IExecutionEventStore>();

    [Fact]
    public async Task Should_Use_The_Subscription_Builtin_With_One_Reference()
    {
        Directory.CreateDirectory(this.root);
        IReadOnlyList<string>? sent = null;
        this.budget.FindRefusalAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        this.process.RunAsync(
                Arg.Any<string>(), Arg.Do<IReadOnlyList<string>>(value => sent = value),
                Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => WriteResult(call.ArgAt<IReadOnlyList<string>>(1)));
        var subject = this.Create();
        var request = new ImageRequest(
            "Dami drinking coffee", "portrait", PrivacyClass.Egressable,
            Guid.NewGuid(), ExecutionOrigin.UserTurn)
        {
            Reference = new ImageReference("canonical.png", "image/png", new byte[] { 1, 2 }),
        };

        var image = await subject.GenerateAsync(request, CancellationToken.None);

        Assert.Equal("generated", Encoding.UTF8.GetString(image.Bytes.ToArray()));
        Assert.NotNull(sent);
        Assert.Contains("workspace-write", sent);
        Assert.Contains("--image", sent);
        var promptIndex = sent.ToList().FindIndex(
            item => item.Contains("built-in image_gen tool", StringComparison.Ordinal));
        Assert.True(promptIndex >= 0);
        Assert.True(promptIndex < sent.ToList().IndexOf("--image"));
        Assert.DoesNotContain("API key", sent[promptIndex], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_Ask_For_A_Change_To_The_Attached_Picture_When_It_Is_The_Source()
    {
        // The anchor rule ("create a new scene, do not edit the pose") is the opposite of
        // what an edit wants; the prompt has to say which one this is.
        Directory.CreateDirectory(this.root);
        IReadOnlyList<string>? sent = null;
        this.budget.FindRefusalAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        this.process.RunAsync(
                Arg.Any<string>(), Arg.Do<IReadOnlyList<string>>(value => sent = value),
                Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => WriteResult(call.ArgAt<IReadOnlyList<string>>(1)));
        var request = new ImageRequest(
            "make it golden hour", "edit", PrivacyClass.Egressable, Guid.NewGuid(), ExecutionOrigin.UserTurn)
        {
            Reference = new ImageReference("source.png", "image/png", new byte[] { 1 }),
            EditReference = true,
        };

        await this.Create().GenerateAsync(request, CancellationToken.None);

        var prompt = sent!.Single(item => item.Contains("built-in image_gen tool", StringComparison.Ordinal));
        Assert.Contains("source picture", prompt, StringComparison.Ordinal);
        Assert.Contains("keep everything else", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("sole identity reference", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Leave_Time_For_The_Outer_Turn_To_Recover_From_An_Image_Timeout()
    {
        Directory.CreateDirectory(this.root);
        TimeSpan? timeout = null;
        this.budget.FindRefusalAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        this.process.RunAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Do<TimeSpan>(value => timeout = value), Arg.Any<CancellationToken>())
            .Returns(call => WriteResult(call.ArgAt<IReadOnlyList<string>>(1)));
        var request = new ImageRequest(
            "Dami in the workshop", "portrait", PrivacyClass.Egressable,
            Guid.NewGuid(), ExecutionOrigin.UserTurn);

        await this.Create().GenerateAsync(request, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(480), timeout);
    }

    private CodexSubscriptionImageGenerator Create() => new(
        this.process,
        Options.Create(new CodexOptions
        {
            Enabled = true,
            WorkingDirectory = this.root,
            TimeoutSeconds = 900,
        }),
        this.budget,
        this.events,
        TimeProvider.System,
        NullLogger<CodexSubscriptionImageGenerator>.Instance);

    private static string WriteResult(IReadOnlyList<string> arguments)
    {
        var prompt = arguments.Single(
            item => item.Contains("built-in image_gen tool", StringComparison.Ordinal));
        var marker = prompt.Split('\n')
            .Single(line => line.StartsWith("SAVE_TO:", StringComparison.Ordinal));
        File.WriteAllBytes(marker["SAVE_TO:".Length..], Encoding.UTF8.GetBytes("generated"));
        return "generated";
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, true);
        }
    }
}
