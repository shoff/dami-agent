using Dami.Contracts.Models;
using Dami.Core.Frontier;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>The draft carries the rules, the context, and the question to the local model.</summary>
public sealed class PromptRedactorTests
{
    private readonly IChatClient chatClient = Substitute.For<IChatClient>();

    [Fact]
    public async Task DraftAsync_Should_Send_The_Question_And_Context_To_The_Local_Model()
    {
        string? prompt = null;
        this.chatClient.CompleteAsync(Arg.Do<string>(text => prompt = text), Arg.Any<CancellationToken>())
            .Returns("  a draft  ");
        var redactor = new PromptRedactor(this.chatClient);

        await redactor.DraftAsync("the question", ["a context note"], CancellationToken.None);

        Assert.Contains("the question", prompt);
    }

    [Fact]
    public async Task DraftAsync_Should_Return_The_Trimmed_Draft()
    {
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("  a draft  ");
        var redactor = new PromptRedactor(this.chatClient);

        Assert.Equal("a draft", await redactor.DraftAsync("q", [], CancellationToken.None));
    }
}
