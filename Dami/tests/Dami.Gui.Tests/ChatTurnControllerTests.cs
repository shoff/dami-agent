using Xunit;

namespace Dami.Gui.Tests;

public sealed class ChatTurnControllerTests
{
    [Fact]
    public async Task Stop_Should_Cancel_The_Active_Request()
    {
        var controller = new ChatTurnController();
        using var release = new SemaphoreSlim(0, 1);
        var requestToken = CancellationToken.None;
        var running = controller.RunAsync(token =>
        {
            requestToken = token;
            return release.WaitAsync(CancellationToken.None);
        }, CancellationToken.None);
        try
        {
            controller.Stop();
            Assert.True(requestToken.IsCancellationRequested);
        }
        finally
        {
            release.Release();
            await running;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_Should_Refuse_A_Second_Send_While_The_First_Is_Active(bool stopping)
    {
        var controller = new ChatTurnController();
        using var release = new SemaphoreSlim(0, 1);
        // Keep the first request pending until finally, including after Stop. A token-
        // cancelled wait may finish before the second send, which would then be valid.
        var first = controller.RunAsync(_ => release.WaitAsync(CancellationToken.None), CancellationToken.None);
        var calls = 0;
        try
        {
            if (stopping)
            {
                controller.Stop();
            }

            await controller.RunAsync(_ =>
            {
                calls++;
                return Task.CompletedTask;
            }, CancellationToken.None);
            Assert.Equal(0, calls);
        }
        finally
        {
            release.Release();
            await first;
        }
    }

    [Fact]
    public async Task RunAsync_Should_Release_A_Failed_Request()
    {
        var controller = new ChatTurnController();
        await Record.ExceptionAsync(() => controller.RunAsync(
            _ => Task.FromException(new IOException("connection lost")), CancellationToken.None));
        var sent = false;

        await controller.RunAsync(_ =>
        {
            sent = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(sent);
    }

    [Fact]
    public async Task Stop_Should_Not_Cancel_The_Window_Lifetime()
    {
        var controller = new ChatTurnController();
        using var lifetime = new CancellationTokenSource();

        await controller.RunAsync(_ =>
        {
            controller.Stop();
            return Task.CompletedTask;
        }, lifetime.Token);

        Assert.False(lifetime.IsCancellationRequested);
    }

    [Fact]
    public async Task RunAsync_Should_Start_A_Fresh_Request_After_Stop()
    {
        var controller = new ChatTurnController();
        await controller.RunAsync(_ =>
        {
            controller.Stop();
            return Task.CompletedTask;
        }, CancellationToken.None);
        var cancelled = true;

        await controller.RunAsync(token =>
        {
            cancelled = token.IsCancellationRequested;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.False(cancelled);
    }

    [Fact]
    public async Task RunAsync_Should_Reject_A_Null_Send()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new ChatTurnController().RunAsync(null!, CancellationToken.None));
    }
}
