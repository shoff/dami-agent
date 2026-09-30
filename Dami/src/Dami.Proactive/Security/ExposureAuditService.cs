using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Security;

/// <summary>Which ports may listen beyond loopback.</summary>
public sealed class ExposureAuditOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION = "ExposureAudit";

    /// <summary>Runbook §1: sshd, and the authenticated LAN proxy for the web view (G30).</summary>
    public IList<int> ExpectedPorts { get; } = [22, 8443];
}

/// <summary>Weekly: anything listening beyond loopback that the runbook does not name (H5).</summary>
/// <remarks>
/// Everything here binds to loopback by design (runbook §1); the exposed self-hosted agents
/// counted in 2026 (21k by Censys for OpenClaw alone) were each a service someone did not
/// know was listening. Read from the kernel's own table, nothing sent anywhere; one surfacing
/// names what is new.
/// </remarks>
public sealed class ExposureAuditService : IProactiveService
{
    private readonly Func<IReadOnlyList<Listener>> listeners;
    private readonly ExposureAuditOptions auditOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<ExposureAuditService> logger;

    /// <summary>Creates the audit over this host's own TCP tables.</summary>
    public ExposureAuditService(IOptions<ExposureAuditOptions> auditOptions, TimeProvider clock, ILogger<ExposureAuditService> logger)
        : this(ExposedListeners.ReadThisHost, auditOptions, clock, logger)
    {
    }

    /// <summary>Creates the audit over <paramref name="listeners"/>.</summary>
    public ExposureAuditService(
        Func<IReadOnlyList<Listener>> listeners, IOptions<ExposureAuditOptions> auditOptions, TimeProvider clock,
        ILogger<ExposureAuditService> logger)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(auditOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.listeners = listeners;
        this.auditOptions = auditOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "exposure-audit";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Weekly;

    /// <inheritdoc />
    public Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var unexpected = this.listeners().Where(listener => !this.auditOptions.ExpectedPorts.Contains(listener.Port)).ToList();
        this.logger.LogInformation("Exposure audit: {Count} unexpected listener(s)", unexpected.Count);
        if (unexpected.Count == 0)
        {
            return Task.FromResult(ProactiveResult.Did("nothing unexpected listens beyond loopback"));
        }

        var body = "Listening beyond this machine, and not in runbook §1:\n"
            + string.Join('\n', unexpected.Select(listener => "• " + listener))
            + "\nFind the owner with `sudo ss -ltnp`; bind it to 127.0.0.1, or add the port to ExposureAudit:ExpectedPorts if it is meant.";
        return Task.FromResult(new ProactiveResult(
            [], [new Surfacing(Guid.NewGuid(), this.ServiceName, $"{unexpected.Count} unexpected listener(s) on this host", body, 0.85, this.clock.GetUtcNow())],
            ProactiveStatus.Completed, $"{unexpected.Count} unexpected listener(s)"));
    }
}
