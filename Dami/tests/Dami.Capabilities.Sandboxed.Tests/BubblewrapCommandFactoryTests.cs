using System.Diagnostics;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class BubblewrapCommandFactoryTests
{
    private static readonly string[] expectedArguments =
    [
        "--user", "--quiet", "--pipe", "--wait", "--collect", "--service-type=exec",
        "--unit=dami-tool-test", "--property=MemoryMax=268435456",
        "--property=TasksMax=16", "--property=RuntimeMaxSec=5s",
        "--property=KillMode=control-group", "/usr/bin/bwrap", "--unshare-all",
        "--unshare-user", "--disable-userns", "--die-with-parent", "--new-session",
        "--cap-drop", "ALL",
        "--clearenv", "--setenv", "HOME", "/tmp", "--setenv", "PATH", "/usr/bin",
        "--setenv", "DOTNET_CLI_HOME", "/tmp/dotnet", "--setenv", "NUGET_PACKAGES",
        "/tmp/nuget", "--setenv", "DOTNET_NOLOGO", "1", "--setenv",
        "DOTNET_CLI_TELEMETRY_OPTOUT", "1", "--setenv",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1", "--setenv",
        "DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK", "1", "--setenv",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE", "true", "--setenv",
        "MSBuildEnableWorkloadResolver", "false", "--ro-bind", "/usr", "/usr",
        "--ro-bind-try", "/lib", "/lib", "--ro-bind-try", "/lib64", "/lib64",
        "--dir", "/etc", "--ro-bind", "/etc/passwd", "/etc/passwd", "--ro-bind",
        "/etc/group", "/etc/group", "--ro-bind-try", "/etc/nsswitch.conf",
        "/etc/nsswitch.conf", "--ro-bind-try", "/etc/ld.so.cache", "/etc/ld.so.cache",
        "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp", "--ro-bind",
        "/tmp/artifact", "/tool", "--chdir", "/tool", "/usr/share/dotnet/dotnet", "Tool.dll",
    ];

    [Fact]
    public void Create_Should_Compose_Systemd_Resource_Bounds_Around_Isolated_Bubblewrap()
    {
        var factory = new BubblewrapCommandFactory(new SandboxProcessOptions
        {
            MemoryMaxBytes = 268_435_456,
            ProcessMax = 16,
            RuntimeMax = TimeSpan.FromSeconds(5),
            UserRuntimeDirectory = "/run/user/1000",
        });

        var start = factory.Create(
            "/tmp/artifact", SandboxMountAccess.ReadOnly,
            ["/usr/share/dotnet/dotnet", "Tool.dll"], "dami-tool-test");

        AssertStartInfo(start);
    }

    [Fact]
    public void Create_Should_Reject_An_Unknown_Mount_Access_Instead_Of_Granting_Write()
    {
        var factory = new BubblewrapCommandFactory(new SandboxProcessOptions());

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create(
            "/tmp/artifact", (SandboxMountAccess)99, ["/usr/bin/true"], "dami-tool-test"));
    }

    private static void AssertStartInfo(ProcessStartInfo start)
    {
        Assert.Equal("/usr/bin/systemd-run", start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.True(start.RedirectStandardInput);
        Assert.True(start.RedirectStandardOutput);
        Assert.True(start.RedirectStandardError);
        Assert.Equal("/run/user/1000", start.Environment["XDG_RUNTIME_DIR"]);
        Assert.Equal(
            "unix:path=/run/user/1000/bus", start.Environment["DBUS_SESSION_BUS_ADDRESS"]);
        Assert.Equal(expectedArguments, start.ArgumentList);
    }
}
