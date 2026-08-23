using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Host.Proactive;
using Dami.Persistence;
using Dami.Privacy;
using Dami.Proactive;
using Dami.Proactive.Audit;
using Dami.Proactive.Reflection;
using Dami.Proactive.Scout;
using Dami.Providers;

// The proactive tier's composition root (D-006): its own process, its own failure
// domain, sharing only the event store and the data layer with the interactive tier.
//
// THE D-012 AUDIT POINT. This file is where egress capability is granted, and the
// grant is visible: exactly one IEgressClient registration, consumed by exactly one
// service (the interest scout). The allowlist defaults to empty, so even the scout
// fetches nothing until Egress:AllowedHosts is configured deliberately. The embedding
// client is loopback inference, not egress — interests embedded through it never
// leave the host.
var builder = Host.CreateApplicationBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Dami")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Dami is not configured. Set it with 'dotnet user-secrets set' "
        + "or the ConnectionStrings__Dami environment variable - never a file in the repo.");

builder.Services.AddDamiPersistence(connectionString);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ProactivePassRunner>();
builder.Services.AddSingleton<ProactiveScheduler>();

// Egress: one client, allowlist-gated, every send a durable event.
builder.Services.Configure<EgressOptions>(builder.Configuration.GetSection(EgressOptions.SECTION_NAME));
builder.Services.AddHttpClient<IEgressClient, HttpEgressClient>();

// Local inference: loopback TEI. Not egress, and must never be routed through it.
builder.Services.Configure<TeiOptions>(builder.Configuration.GetSection(TeiOptions.SECTION_NAME));
builder.Services.AddHttpClient<IEmbeddingClient, TeiEmbeddingClient>();

// The first proactive service (D-019).
builder.Services.Configure<InterestScoutOptions>(
    builder.Configuration.GetSection(InterestScoutOptions.SECTION_NAME));
builder.Services.AddSingleton<IProactiveService, InterestScout>();

// D-011's quarterly decay detector. Local-only: no egress dependency, by design.
builder.Services.AddSingleton<IProactiveService, PushbackAuditService>();

// The weekly reflection pass: observations -> at most one belief, via the loopback
// sidecar. The most personal pass in the system, and deliberately egress-free.
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SECTION_NAME));
builder.Services.AddHttpClient<IChatClient, OllamaChatClient>(client =>
    client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.Configure<ReflectionOptions>(
    builder.Configuration.GetSection(ReflectionOptions.SECTION_NAME));
builder.Services.AddSingleton<IProactiveService, ReflectionService>();

builder.Services.AddHostedService<ProactiveWorker>();

await builder.Build().RunAsync();
