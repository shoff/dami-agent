using System.Diagnostics;
using System.Globalization;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Builds the fixed systemd-cgroup and bubblewrap command boundary.</summary>
public sealed class BubblewrapCommandFactory : ISandboxCommandFactory
{
    private const string BUBBLEWRAP = "/usr/bin/bwrap";
    private const string SYSTEMD_RUN = "/usr/bin/systemd-run";

    private readonly SandboxProcessOptions options;

    /// <summary>Creates a command factory with immutable resource limits.</summary>
    public BubblewrapCommandFactory(SandboxProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        SandboxProcessOptionsGuard.Validate(options);
        this.options = options;
    }

    /// <summary>Creates one no-shell command for trusted executable arguments.</summary>
    public ProcessStartInfo Create(
        string toolDirectory,
        SandboxMountAccess mountAccess,
        IReadOnlyList<string> command,
        string unitName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolDirectory);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(unitName);
        if (!Enum.IsDefined(mountAccess))
        {
            throw new ArgumentOutOfRangeException(nameof(mountAccess));
        }

        if (command.Count == 0 || !Path.IsPathFullyQualified(command[0]))
        {
            throw new ArgumentException(
                "A sandbox command requires an absolute executable path.", nameof(command));
        }

        var start = this.CreateStartInfo();
        this.AddSystemdArguments(start, unitName);
        AddBubblewrapArguments(start, Path.GetFullPath(toolDirectory), mountAccess);
        foreach (string argument in command)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var start = new ProcessStartInfo
        {
            FileName = SYSTEMD_RUN,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.Environment.Clear();
        start.Environment["XDG_RUNTIME_DIR"] = this.options.UserRuntimeDirectory;
        start.Environment["DBUS_SESSION_BUS_ADDRESS"] =
            $"unix:path={this.options.UserRuntimeDirectory}/bus";
        return start;
    }

    private void AddSystemdArguments(ProcessStartInfo start, string unitName)
    {
        Add(start, "--user", "--quiet", "--pipe", "--wait", "--collect",
            "--service-type=exec", $"--unit={unitName}",
            $"--property=MemoryMax={this.options.MemoryMaxBytes.ToString(CultureInfo.InvariantCulture)}",
            $"--property=TasksMax={this.options.ProcessMax.ToString(CultureInfo.InvariantCulture)}",
            $"--property=RuntimeMaxSec={this.options.RuntimeMax.TotalSeconds.ToString(CultureInfo.InvariantCulture)}s",
            "--property=KillMode=control-group", BUBBLEWRAP);
    }

    private static void AddBubblewrapArguments(
        ProcessStartInfo start,
        string toolDirectory,
        SandboxMountAccess mountAccess)
    {
        Add(start, "--unshare-all", "--unshare-user", "--disable-userns", "--die-with-parent",
            "--new-session", "--cap-drop", "ALL", "--clearenv", "--setenv", "HOME", "/tmp", "--setenv",
            "PATH", "/usr/bin", "--setenv", "DOTNET_CLI_HOME", "/tmp/dotnet", "--setenv",
            "NUGET_PACKAGES", "/tmp/nuget", "--setenv", "DOTNET_NOLOGO", "1", "--setenv",
            "DOTNET_CLI_TELEMETRY_OPTOUT", "1", "--setenv",
            "DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1", "--setenv",
            "DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK", "1", "--setenv",
            "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE", "true", "--setenv",
            "MSBuildEnableWorkloadResolver", "false", "--ro-bind", "/usr", "/usr",
            "--ro-bind-try", "/lib", "/lib", "--ro-bind-try", "/lib64", "/lib64",
            "--dir", "/etc", "--ro-bind", "/etc/passwd", "/etc/passwd", "--ro-bind",
            "/etc/group", "/etc/group", "--ro-bind-try", "/etc/nsswitch.conf",
            "/etc/nsswitch.conf", "--ro-bind-try", "/etc/ld.so.cache", "/etc/ld.so.cache",
            "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp",
            mountAccess == SandboxMountAccess.ReadOnly ? "--ro-bind" : "--bind",
            toolDirectory, "/tool", "--chdir", "/tool");
    }

    private static void Add(ProcessStartInfo start, params ReadOnlySpan<string> arguments)
    {
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
    }

}
