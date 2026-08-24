using Avalonia;

namespace Dami.Gui;

/// <summary>Entry point for the Dami desktop client.</summary>
public static class Program
{
    /// <summary>Starts the application.</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Builds the Avalonia application. Used by the visual designer too.</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
