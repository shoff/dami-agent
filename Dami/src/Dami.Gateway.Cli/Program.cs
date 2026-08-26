using Dami.Gateway.Cli;
using Dami.Authentication;
using Dami.Contracts.Models;
using Dami.Persistence;
using Dami.Vision;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// D-005 honored: the CLI is a thin client of the localhost runtime API (dami-host).
// Two deliberate exceptions keep talking to local resources directly:
//   - `dami health` diagnoses the host — including when the API itself is down;
//   - `dami caption` reads a local image file and runs the vision worker;
//   - `dami board-import` writes a repository file the deployed Host cannot see (O1g).
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
services.AddHttpClient<DamiApiClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(15);
    DamiBearerToken.Apply(client, configuration["Authentication:AccessToken"]);
});

// The two direct-access exceptions.
services.AddDamiPersistence(connectionString);
services.AddSingleton(TimeProvider.System);
services.AddSingleton<HealthCommands>();
services.AddOptions<OllamaVisionOptions>();
services.Configure<OllamaVisionOptions>(configuration.GetSection(OllamaVisionOptions.SECTION_NAME));
services.AddHttpClient<IVisionClient, OllamaVisionClient>(client =>
    client.Timeout = TimeSpan.FromMinutes(10));
services.AddSingleton<Dami.Contracts.Workers.IWorkerRunner, Dami.Core.Workers.WorkerRunner>();
services.AddSingleton<VisionCommands>();
services.AddSingleton<BoardImportCommands>();
services.AddSingleton(BoardActor.FromEnvironment());
services.AddSingleton<BoardCommands>();
services.AddSingleton<BoardVerbs>();

// Everything else is the API client.
services.AddSingleton<InboxCommands>();
services.AddSingleton<TraceCommands>();
services.AddSingleton<BeliefCommands>();
services.AddSingleton<RecallCommands>();
services.AddSingleton<AskCommands>();
services.AddSingleton<ContextCommands>();
services.AddSingleton<StatsCommands>();
services.AddSingleton<ChatCommands>();
services.AddSingleton<SessionCommands>();
services.AddSingleton<FrontierCommands>();
services.AddSingleton<ApprovalCommands>();
services.AddSingleton<BriefCommands>();
services.AddSingleton<HealthLogCommands>();
services.AddSingleton<ListenCommands>();
services.AddSingleton<SayCommands>();
services.AddSingleton<VoiceVerbs>();
services.AddSingleton<DisclosureCommands>();
services.AddSingleton<DomainCommands>();
services.AddSingleton<TodayCommands>();
services.AddSingleton<ReviewVerbs>();

await using var provider = services.BuildServiceProvider();

try
{
    return await CommandRouter.RunAsync(
        args,
        provider.GetRequiredService<InboxCommands>(),
        provider.GetRequiredService<TraceCommands>(),
        provider.GetRequiredService<BeliefCommands>(),
        provider.GetRequiredService<HealthCommands>(),
        provider.GetRequiredService<RecallCommands>(),
        provider.GetRequiredService<AskCommands>(),
        provider.GetRequiredService<ContextCommands>(),
        provider.GetRequiredService<VisionCommands>(),
        provider.GetRequiredService<StatsCommands>(),
        provider.GetRequiredService<ChatCommands>(),
        provider.GetRequiredService<SessionCommands>(),
        provider.GetRequiredService<FrontierCommands>(),
        provider.GetRequiredService<ApprovalCommands>(),
        provider.GetRequiredService<BriefCommands>(),
        provider.GetRequiredService<HealthLogCommands>(),
        provider.GetRequiredService<VoiceVerbs>(),
        provider.GetRequiredService<ReviewVerbs>(),
        provider.GetRequiredService<BoardVerbs>());
}
catch (Dami.Contracts.Privacy.EgressRefusedException exception)
{
    // A boundary refusal is an answer, not a crash. The event stream already has it.
    Console.Error.WriteLine($"refused: {exception.Message}");
    return 1;
}
