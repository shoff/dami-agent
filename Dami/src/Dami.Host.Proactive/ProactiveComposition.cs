using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Persistence;
using Dami.Privacy;
using Dami.Proactive;
using Dami.Proactive.Audit;
using Dami.Proactive.CodeAudit;
using Dami.Proactive.Hygiene;
using Dami.Proactive.Curation;
using Dami.Proactive.Health;
using Dami.Proactive.Civic;
using Dami.Proactive.Network;
using Dami.Proactive.Portrait;
using Dami.Proactive.Releases;
using Dami.Proactive.Recalls;
using Dami.Proactive.Security;
using Dami.Proactive.Weather;
using Dami.Proactive.Embedder;
using Dami.Proactive.Librarian;
using Dami.Proactive.Reflection;
using Dami.Proactive.Scout;
using Dami.Vision;
using Dami.Providers;
using Microsoft.Extensions.Options;

namespace Dami.Host.Proactive;

/// <summary>The proactive tier's registrations, in one callable place.</summary>
/// <remarks>
/// Extracted from Program's top-level statements so the composition root can be resolved
/// in a test. It was the one part of the system with no coverage at all: on 2026-08-29 a
/// service gained a dependency nobody registered, and the change built clean, passed the
/// whole suite, deployed, and then aborted at startup in a restart loop. Every test
/// constructs these services directly - which is what makes them testable - so nothing
/// exercised the wiring itself.
///
/// THE D-012 AUDIT POINT moved here with them. This is where egress capability is granted,
/// and the grant is still meant to be visible at a glance: exactly one IEgressClient
/// registration, consumed by exactly one service.
/// </remarks>
public static class ProactiveComposition
{
    /// <summary>Registers the proactive tier against <paramref name="configuration"/>.</summary>
    public static IServiceCollection AddDamiProactiveTier(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(connectionString);

        AddFoundation(services, configuration, connectionString);
        AddEgress(services, configuration, connectionString);
        AddCollectors(services, configuration, connectionString);
        AddModelBacked(services, configuration, connectionString);
        AddRemaining(services, configuration, connectionString);

        return services;
    }

    private static void AddFoundation(
        IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services.AddDamiPersistence(connectionString);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ProactivePassRunner>();
        services.AddSingleton<ProactiveScheduler>();

        // Egress: one client, allowlist-gated, every send a durable event, rate-bounded (C5).
        services.Configure<EgressOptions>(configuration.GetSection(EgressOptions.SECTION_NAME));
        services.Configure<EgressBudgetOptions>(
            configuration.GetSection(EgressBudgetOptions.SECTION_NAME));
        services.AddSingleton<IEgressBudget, EventCountEgressBudget>();
        services.AddHttpClient<IEgressClient, HttpEgressClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        // Local inference: loopback TEI. Not egress, and must never be routed through it.
        services.Configure<TeiOptions>(configuration.GetSection(TeiOptions.SECTION_NAME));
        services.AddHttpClient<IEmbeddingClient, TeiEmbeddingClient>();

        // H8: thresholds tune themselves from recorded reactions, bounded and stateless.
        services.Configure<ThresholdTuningOptions>(
            configuration.GetSection(ThresholdTuningOptions.SECTION_NAME));
        services.AddSingleton<ISurfacingThresholdTuner, ReactionThresholdTuner>();
    }

    private static void AddEgress(
        IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        // The first proactive service (D-019).
        services.Configure<InterestScoutOptions>(
            configuration.GetSection(InterestScoutOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, InterestScout>();

        // D-011's quarterly decay detector. Local-only: no egress dependency, by design.
        services.AddSingleton<IProactiveService, PushbackAuditService>();

        // K2: the health domain collector — reads observations, writes structured health rows
        // (LocalOnly, no egress path), the timeline D-007's reflection join will read.
        services.Configure<HealthCollectorOptions>(
            configuration.GetSection(HealthCollectorOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, HealthCollectorService>();

        // K4: the network domain from this host's own state — interfaces, the LAN, loopback
        // services. LocalOnly by construction; it has no egress client.
        services.Configure<NetworkCollectorOptions>(
            configuration.GetSection(NetworkCollectorOptions.SECTION_NAME));
        services.AddSingleton<INetworkProbe, SystemNetworkProbe>();
        services.AddSingleton<LanScanner>();
        services.AddSingleton<IProactiveService, NetworkCollectorService>();
    }

    private static void AddCollectors(
        IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        // K4: the civic domain from public feeds through the egress boundary. Defaults are
        // Lakeville, MN; the feed host must be on Egress:AllowedHosts or every fetch is refused.
        services.Configure<CivicFeedOptions>(
            configuration.GetSection(CivicFeedOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, CivicFeedCollectorService>();
        services.AddSingleton<IProactiveService, CivicAgendaService>();

        AddInternetWatchers(services, configuration);

        // Rewrites imported transcript voice into usable knowledge. Derived, reversible.
        services.Configure<CuratorOptions>(
            configuration.GetSection(CuratorOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, CuratorService>();

        // D-016: the codebase audit reads the repo and proposes; it commits nothing.
        services.Configure<CodebaseAuditOptions>(
            configuration.GetSection(CodebaseAuditOptions.SECTION_NAME));
        services.AddSingleton<IGitLog, GitProcessLog>();
        services.AddSingleton<IProactiveService, CodebaseAuditService>();

        // Repo hygiene: notices when work is stranded on this disk. Steve's stated safety net is
    }

    /// <summary>The watchers that scour the internet and join what they find locally.</summary>
    /// <remarks>
    /// Both are dark until their hosts join Egress:AllowedHosts — H13 needs
    /// download.nvidia.com, github.com and www.postgresql.org; H11 needs ubuntu.com and
    /// api.github.com. A refused host is a loud per-source warning, not a dead pass.
    /// </remarks>
    private static void AddInternetWatchers(IServiceCollection services, IConfiguration configuration)
    {
        // H13: fixes for what this host runs. Baselines stay in configuration and are
        // compared locally; only GETs of public URLs leave; a release surfaces once.
        services.Configure<ReleaseWatchOptions>(
            configuration.GetSection(ReleaseWatchOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, ReleaseWatchService>();

        // H11: public vulnerability data joined locally against what this host runs.
        // The inventory never leaves; the advisories query carries public package
        // names only, never versions.
        services.Configure<CveWatchOptions>(
            configuration.GetSection(CveWatchOptions.SECTION_NAME));
        services.AddSingleton<IInstalledInventory>(provider => new LocalInstalledInventory(
            provider.GetRequiredService<IOptions<CveWatchOptions>>().Value.RepositoryRoot));
        services.AddSingleton<IProactiveService, CveWatchService>();

        // H12, split on the D-012 rule: the collector holds egress and never reads
        // health; the matcher reads health and holds no egress client at all. New
        // hosts: api.fda.gov, www.saferproducts.gov.
        services.Configure<RecallSentinelOptions>(
            configuration.GetSection(RecallSentinelOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, RecallCollectorService>();
        services.AddSingleton<IProactiveService, RecallMatchService>();

        // H14, the same split for the same reason: the collector holds egress and reads
        // nothing personal; the window scorer reads the fitness domain and holds no
        // egress client. New host: api.weather.gov.
        services.Configure<WeatherOptions>(
            configuration.GetSection(WeatherOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, WeatherCollectorService>();
        services.AddSingleton<IProactiveService, WeatherWindowService>();
    }

    private static void AddModelBacked(
        IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        // that nothing here matters provided the repository is committed and pushed; on
        // 2026-08-29 that had been false for four days and nothing could say so. Read-only — it
        // never commits, pushes, or stages, because a nightly job with write authority over a
        // working copy eventually uses it at the wrong moment.
        services.Configure<RepoHygieneOptions>(
            configuration.GetSection(RepoHygieneOptions.SECTION_NAME));
        services.AddSingleton<IRepoState, GitRepoState>();
        services.AddSingleton<IProactiveService, RepoHygieneService>();

        // The weekly reflection pass: observations -> at most one belief, via the loopback
        // sidecar. The most personal pass in the system, and deliberately egress-free.
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SECTION_NAME));
        services.AddHttpClient<IChatClient, OllamaChatClient>(client =>
            client.Timeout = TimeSpan.FromMinutes(10));
        services.Configure<ReflectionOptions>(
            configuration.GetSection(ReflectionOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, ReflectionService>();

        // Local vision: loopback, on-demand load; images never leave the host.
        services.Configure<OllamaVisionOptions>(configuration.GetSection(OllamaVisionOptions.SECTION_NAME));
        services.AddHttpClient<IVisionClient, OllamaVisionClient>(client =>
            client.Timeout = TimeSpan.FromMinutes(10));

        // ADR-0027: the third door through the boundary, and the only one with a bill
        // attached. Refused unless api.openai.com is allowlisted and a key is configured;
        // the portrait pass is off until DailyPortrait:Enabled says otherwise.
        services.Configure<OpenAiImageOptions>(
            configuration.GetSection(OpenAiImageOptions.SECTION_NAME));
        services.AddHttpClient<IImageGenerator, OpenAiImageGenerator>(client =>
            client.Timeout = TimeSpan.FromMinutes(5));
        services.Configure<DailyPortraitOptions>(
            configuration.GetSection(DailyPortraitOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, DailyPortraitService>();
    }

    private static void AddRemaining(
        IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        // Propose-only file organization (D-020). Holds no move, rename, or delete code at
        // all. Quiet until MediaLibrarian:RootPaths names directories to survey.
        services.Configure<MediaLibrarianOptions>(
            configuration.GetSection(MediaLibrarianOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, MediaLibrarianService>();

        // Keeps the corpus's semantic index current (ADR-0009). Loopback inference only.
        services.Configure<EmbedderOptions>(configuration.GetSection(EmbedderOptions.SECTION_NAME));
        services.AddSingleton<IProactiveService, EmbedderService>();
    }
}
