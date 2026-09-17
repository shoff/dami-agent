using System.Diagnostics;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class SpeechTests
{
    [Fact]
    public async Task WaitForExitAsync_Should_Stop_The_Player_When_Playback_Is_Cancelled()
    {
        using var player = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false })
            ?? throw new InvalidOperationException("Could not start the silent playback fixture.");
        using var cancellation = new CancellationTokenSource();
        try
        {
            await cancellation.CancelAsync();
            await Record.ExceptionAsync(() => Speech.WaitForExitAsync(player, cancellation.Token));

            Assert.True(player.HasExited);
        }
        finally
        {
            if (!player.HasExited)
            {
                player.Kill(entireProcessTree: true);
                await player.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
