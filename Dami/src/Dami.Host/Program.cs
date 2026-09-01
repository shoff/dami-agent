using Dami.Authentication;
using Dami.Contracts.Approvals;
using Dami.Contracts.Models;
using Dami.Contracts.TaskBoard;
using Dami.Core.Approvals;
using Dami.Core.Context;
using Dami.Core.Frontier;
using Dami.Core.Sessions;
using Dami.Core.TaskBoard;
using Dami.Core.Turns;
using Dami.Host;
using Dami.Host.Discord;
using Dami.Persistence;
using Dami.Privacy;
using Dami.Proactive.Librarian;
using Dami.Providers;

// D-005: the interactive runtime is an API on localhost; CLI, GUI, and voice are
// thin clients of the same surface. Localhost-only is a privacy boundary, not a
// deployment detail — exposing this beyond loopback is a separate auth decision.
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});
builder.WebHost.UseUrls("http://127.0.0.1:5810");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));

var connectionString =
    builder.Configuration.GetConnectionString("Dami")
    ?? "Host=127.0.0.1;Port=5432;Database=dami-data;Username=dami_app;Passfile="
       + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.pgpass";

builder.Services.AddDamiPersistence(connectionString);
bool authenticationEnabled = builder.Configuration.GetValue<bool>(
    $"{DamiAuthenticationOptions.SECTION_NAME}:Enabled");
if (authenticationEnabled)
{
    builder.Services.AddDamiAuthentication(
        builder.Configuration, builder.Environment, connectionString);
}
builder.Services.AddSingleton<TaskBoardActorResolver>();

builder.Services.AddSingleton(TimeProvider.System);

// Turns: the same runner the CLI proved out — leading with the §9.1 identity block.
builder.Services.Configure<Dami.Core.Identity.IdentityOptions>(
    builder.Configuration.GetSection(Dami.Core.Identity.IdentityOptions.SECTION_NAME));
builder.Services.AddSingleton<IIdentityProvider, Dami.Core.Identity.FileIdentityProvider>();
builder.Services.AddSingleton<TurnRunner>();
builder.Services.AddSingleton<ITurnRunner>(services => services.GetRequiredService<TurnRunner>());
builder.Services.AddSingleton<ITracedTurnRunner>(services =>
    services.GetRequiredService<TurnRunner>());
builder.Services.Configure<SessionContextOptions>(
    builder.Configuration.GetSection(SessionContextOptions.SECTION_NAME));
builder.Services.AddSingleton<ISessionCancellationRegistry, SessionCancellationRegistry>();
builder.Services.AddSingleton<IConversationWindowBuilder, ConversationWindowBuilder>();
builder.Services.AddSingleton<ISessionTurnRunner, SessionTurnRunner>();

// The same durable session machinery driven by the subscription frontier instead of
// the sidecar (ADR-0011). Reusing SessionTurnRunner means reservation, interruption,
// replay, and durable completion behave identically; only the model adapter differs.
builder.Services.AddSingleton<Dami.Core.Frontier.FrontierTracedTurnRunner>();
builder.Services.AddKeyedSingleton<ISessionTurnRunner>("frontier", (provider, _) =>
    new SessionTurnRunner(
        provider.GetRequiredService<Dami.Contracts.Sessions.IConversationTurnStore>(),
        provider.GetRequiredService<IConversationWindowBuilder>(),
        provider.GetRequiredService<Dami.Core.Frontier.FrontierTracedTurnRunner>(),
        provider.GetRequiredService<ISessionCancellationRegistry>(),
        provider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IConversationSessionManager, ConversationSessionManager>();
builder.Services.Configure<QueryPlanOptions>(
    builder.Configuration.GetSection(QueryPlanOptions.SECTION_NAME));
builder.Services.AddSingleton<Dami.Contracts.Context.IQueryPlanner, LocalQueryPlanner>();
builder.Services.AddSingleton<Dami.Contracts.Context.IContextBuilder, ContextBuilder>();
builder.Services.Configure<ContextOptions>(builder.Configuration.GetSection(ContextOptions.SECTION_NAME));
builder.Services.AddSingleton<IModelRouter, ModelRouter>();
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SECTION_NAME));
builder.Services.Configure<TeiOptions>(builder.Configuration.GetSection(TeiOptions.SECTION_NAME));
builder.Services.Configure<TeiRerankOptions>(builder.Configuration.GetSection(TeiRerankOptions.SECTION_NAME));
builder.Services.AddHttpClient<IChatClient, OllamaChatClient>(client =>
    client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddHttpClient<IEmbeddingClient, TeiEmbeddingClient>();
builder.Services.AddHttpClient<IRerankClient, TeiRerankClient>();

// Bounded workers with child traces (G8). The transcription endpoint runs through this.
builder.Services.AddSingleton<Dami.Contracts.Workers.IWorkerRunner, Dami.Core.Workers.WorkerRunner>();

// L3: local speech to text. Loopback only — spoken input is as personal as the corpus.
builder.Services.Configure<WhisperOptions>(
    builder.Configuration.GetSection(WhisperOptions.SECTION_NAME));
// L4: text to speech through the local Piper sidecar; audio never leaves the host.
builder.Services.Configure<PiperOptions>(builder.Configuration.GetSection(PiperOptions.SECTION_NAME));
builder.Services.AddHttpClient<ISpeechClient, PiperSpeechClient>(client => client.Timeout = TimeSpan.FromMinutes(2));
builder.Services.AddHttpClient<ITranscriptionClient, WhisperTranscriptionClient>(client =>
    client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddDamiNativeTools(builder.Configuration, TimeProvider.System);
builder.Services.AddDamiMcpTools(builder.Configuration);
builder.Services.AddDamiSkills(builder.Configuration, TimeProvider.System);
builder.Services.AddDamiSandboxedTools(builder.Configuration);

// Approvals execute in the runtime (D-005): librarian manifests and egress briefs.
builder.Services.AddSingleton<ManifestExecutor>();
builder.Services.AddSingleton<BriefExecutor>();
builder.Services.AddSingleton<IApprovalExecutionHandler>(services =>
    services.GetRequiredService<ManifestExecutor>());
builder.Services.AddSingleton<IApprovalExecutionHandler>(services =>
    services.GetRequiredService<BriefExecutor>());

builder.Services.AddSingleton<ApprovalExecutionDispatcher>();
builder.Services.AddSingleton<Dami.Contracts.Privacy.IPromptRedactor, PromptRedactor>();

// The frontier answers; the local sidecar does the retrieval that feeds it.
builder.Services.Configure<DisclosureOptions>(
    builder.Configuration.GetSection(DisclosureOptions.SECTION_NAME));
builder.Services.AddSingleton<Dami.Contracts.Privacy.IContextDisclosureGate, LocalDisclosureGate>();
builder.Services.Configure<AugmentedTurnOptions>(
    builder.Configuration.GetSection(AugmentedTurnOptions.SECTION_NAME));
builder.Services.AddSingleton<AugmentedFrontierTurn>();
builder.Services.AddSingleton<IAugmentedTurn>(services =>
    services.GetRequiredService<AugmentedFrontierTurn>());

// Frontier: subscription door (ADR-0011) behind the C5 egress budget.
builder.Services.Configure<CodexOptions>(builder.Configuration.GetSection(CodexOptions.SECTION_NAME));
builder.Services.AddSingleton<ICodexProcess, CodexProcess>();
builder.Services.AddSingleton<IFrontierChat, CodexChatClient>();
builder.Services.Configure<EgressBudgetOptions>(
    builder.Configuration.GetSection(EgressBudgetOptions.SECTION_NAME));
builder.Services.AddSingleton<Dami.Contracts.Privacy.IEgressBudget, EventCountEgressBudget>();

// Feature planning is provider-neutral at the application boundary. The three
// adapters share the already-composed model clients and router; only the selected
// IFeaturePlanner is invoked for a request.
builder.Services.AddSingleton<LocalFeaturePlanner>();
builder.Services.AddSingleton<FrontierFeaturePlanner>();
builder.Services.AddSingleton<DamiFeaturePlanner>(services => new DamiFeaturePlanner(
    services.GetRequiredService<IModelRouter>(),
    services.GetRequiredService<LocalFeaturePlanner>(),
    services.GetRequiredService<FrontierFeaturePlanner>()));
builder.Services.AddSingleton<IFeaturePlanner>(services =>
    services.GetRequiredService<LocalFeaturePlanner>());
builder.Services.AddSingleton<IFeaturePlanner>(services =>
    services.GetRequiredService<FrontierFeaturePlanner>());
builder.Services.AddSingleton<IFeaturePlanner>(services =>
    services.GetRequiredService<DamiFeaturePlanner>());
builder.Services.AddSingleton<FeaturePlanningService>();

// "Work this task now" (V1, advisory): runs one turn against one task and records it on
// the board. It takes ITurnRunner, so it inherits exactly the tool budget the interactive
// turn has — no wider surface was opened for it.
builder.Services.AddSingleton<TaskWorkService>();

// Local vision, loopback only: the process that runs the Discord gateway needs it to read
// what Steve sends (ADR-0026). It was registered in the proactive tier and the CLI but not
// here, so an image had nothing on hand to look at it. Images never leave the host.
builder.Services.Configure<Dami.Vision.OllamaVisionOptions>(
    builder.Configuration.GetSection(Dami.Vision.OllamaVisionOptions.SECTION_NAME));
builder.Services.AddHttpClient<IVisionClient, Dami.Vision.OllamaVisionClient>(client =>
    client.Timeout = TimeSpan.FromMinutes(10));

// Discord (ADR-0024, M1). Dormant unless Discord__Token and Discord__OwnerUserId are set,
// which the systemd drop-in supplies; the registration is here rather than inside the
// gateway so that what can reach off the host stays readable in one place.
builder.Services.AddDamiDiscordGateway(builder.Configuration);

var app = builder.Build();

// A boundary refusal is an answer, not a server fault. Without this it escapes as an
// unhandled 500 and every client reports the host as unreachable — which sent us
// looking at the network when the runtime had simply, correctly, said no.
app.Use(async (context, next) =>
{
    try
    {
        await next(context).ConfigureAwait(false);
    }
    catch (Dami.Contracts.Privacy.EgressRefusedException refusal)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { refused = refusal.Message })
            .ConfigureAwait(false);
    }
    catch (Exception failure) when (!context.Response.HasStarted)
    {
        // Name the cause. An empty 500 makes every client report the host as
        // unreachable, which sends the reader to the network when the truth was a
        // sidecar that had stopped. This is a single-user host on loopback; the
        // detail is Steve's to see.
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(
            new { error = failure.Message, kind = failure.GetType().Name }).ConfigureAwait(false);
    }
});

// J3 first cut: a zero-install conversation + live-graph view, rendered entirely
// from the same endpoints every other client uses. Localhost-only like the API.
app.UseDefaultFiles();
app.UseStaticFiles();

if (authenticationEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
    AuthenticationEndpoints.Map(app);
}

app.MapDamiRuntime();
app.MapDamiProactive();
app.MapDamiActivity();
app.Run();

/// <summary>Web entry point exposed for in-memory composition tests.</summary>
public partial class Program
{
}
