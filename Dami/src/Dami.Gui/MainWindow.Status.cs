namespace Dami.Gui;

/// <summary>One persistent status rail shared by every interactive surface.</summary>
public sealed partial class MainWindow
{
    private void SetStatus(GlobalStatus status) => this.state.SystemStatus = status;
}
