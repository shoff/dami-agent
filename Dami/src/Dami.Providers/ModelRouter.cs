using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>§7.4's tiered routing, with D-012 as the first and unconditional rule.</summary>
/// <remarks>
/// Local-only work routes local, always — not as policy but as the boundary itself; no
/// configuration in this class can override it. Beyond that the split is deterministic
/// by work kind: simple classification, summarization, and categorization stay on the
/// sidecar; synthesis and code generation go frontier when the privacy class allows.
/// Cheap-model-assisted routing can replace the table later behind the same interface.
/// </remarks>
public sealed class ModelRouter : IModelRouter
{
    private readonly RoutingOptions routingOptions;
    private readonly ILogger<ModelRouter> logger;

    /// <summary>Creates the router.</summary>
    public ModelRouter(IOptions<RoutingOptions> routingOptions, ILogger<ModelRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(routingOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.routingOptions = routingOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public ModelRoute Route(string workKind, PrivacyClass privacy)
    {
        ArgumentNullException.ThrowIfNull(workKind);

        if (privacy == PrivacyClass.LocalOnly)
        {
            return new ModelRoute(
                ModelTier.Local, privacy, "local-only work never leaves the host (D-012)");
        }

        if (this.routingOptions.LocalWorkKinds.Contains(workKind))
        {
            return new ModelRoute(
                ModelTier.Local, privacy, $"'{workKind}' is simple work; the sidecar handles it");
        }

        if (!this.routingOptions.FrontierEnabled)
        {
            return new ModelRoute(
                ModelTier.Local, privacy, "no frontier provider is configured; degrading to local");
        }

        return new ModelRoute(
            ModelTier.Frontier, privacy, $"'{workKind}' warrants a frontier model");
    }
}
