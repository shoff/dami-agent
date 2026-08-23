using Dami.Host.Proactive;
using Dami.Persistence;
using Dami.Proactive;

// The proactive tier's composition root (D-006): its own process, its own failure
// domain, sharing only the event store and the data layer with the interactive tier.
//
// The audit point D-012 asks for: this file registers NO egress client. Every service
// this host runs is local-only until an IEgressClient registration appears here, and
// adding one is a visible, reviewable change to exactly one file.
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

// IProactiveService implementations register here as they are built. None exist yet;
// the worker says so at startup rather than pretending to be busy.
builder.Services.AddHostedService<ProactiveWorker>();

await builder.Build().RunAsync();
