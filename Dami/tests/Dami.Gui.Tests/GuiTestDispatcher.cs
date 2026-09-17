using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace Dami.Gui.Tests;

internal static class GuiTestDispatcher
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Avalonia.Skia.SkiaPlatform.Initialize();
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            dispatcher.MainLoop(CancellationToken.None);
        }) { IsBackground = true, Name = "GUI test dispatcher" };
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("The GUI test dispatcher did not start.");
        }
    }
}
