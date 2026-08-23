using Dami.Gateway.Cli;
using Dami.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The surfacing channel, phase 4's "a queue Steve reads when he wants" — plus the
// feedback capture that trains the taste model (D-019).
//
// DEVIATION FROM D-005, recorded: the CLI is meant to be a thin client of the localhost
// runtime API, and no such API exists yet. Until Dami.Host exposes one, this talks to
// the stores directly. The command surface is what will survive that change; the
// transport behind it will not.
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<InboxCommands>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var connectionString =
    configuration.GetConnectionString("Dami")
    ?? "Host=127.0.0.1;Port=5432;Database=dami-data;Username=dami_app;Passfile="
       + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.pgpass";

var services = new ServiceCollection();
services.AddLogging(logging => logging.AddFilter(_ => false));
services.AddDamiPersistence(connectionString);
services.AddSingleton(TimeProvider.System);
services.AddSingleton<InboxCommands>();
services.AddSingleton<TraceCommands>();
services.AddSingleton<BeliefCommands>();

await using var provider = services.BuildServiceProvider();

return await CommandRouter.RunAsync(
    args,
    provider.GetRequiredService<InboxCommands>(),
    provider.GetRequiredService<TraceCommands>(),
    provider.GetRequiredService<BeliefCommands>());
