using Dami.Host.Proactive;
using Dami.Proactive;

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

builder.Services.AddDamiProactiveTier(builder.Configuration, connectionString);

// `--run <service-name>`: one pass now, due or not, then exit — the operator's hand on
// the tier, for a collector whose feeds were refused or a config just changed.
var runNow = Array.IndexOf(args, "--run") is var flag && flag >= 0 && flag + 1 < args.Length ? args[flag + 1] : null;
if (runNow is null)
{
    builder.Services.AddHostedService<ProactiveWorker>();
}

var host = builder.Build();
if (runNow is null)
{
    await host.RunAsync();
    return;
}

var ran = await host.Services.GetRequiredService<ProactiveScheduler>().RunNowAsync(runNow, CancellationToken.None);
Console.WriteLine(ran ? $"ran {runNow}" : $"no proactive service is named {runNow}");
Environment.ExitCode = ran ? 0 : 2;
