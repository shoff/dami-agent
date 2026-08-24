using Dami.Contracts.Approvals;
using Dami.Contracts.Models;
using Dami.Core.Approvals;
using Dami.Core.Context;
using Dami.Core.Frontier;
using Dami.Core.Sessions;
using Dami.Core.Turns;
using Dami.Host;
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
builder.Services.AddDamiNativeTools(builder.Configuration, TimeProvider.System);
builder.Services.AddDamiMcpTools(builder.Configuration);
builder.Services.AddDamiSkills(builder.Configuration, TimeProvider.System);

// Approvals execute in the runtime (D-005): librarian manifests and egress briefs.
builder.Services.AddSingleton<ManifestExecutor>();
builder.Services.AddSingleton<BriefExecutor>();
builder.Services.AddSingleton<IApprovalExecutionHandler>(services =>
    services.GetRequiredService<ManifestExecutor>());
builder.Services.AddSingleton<IApprovalExecutionHandler>(services =>
    services.GetRequiredService<BriefExecutor>());

builder.Services.AddSingleton<ApprovalExecutionDispatcher>();
builder.Services.AddSingleton<Dami.Contracts.Privacy.IPromptRedactor, PromptRedactor>();

// Frontier: subscription door (ADR-0011) behind the C5 egress budget.
builder.Services.Configure<CodexOptions>(builder.Configuration.GetSection(CodexOptions.SECTION_NAME));
builder.Services.AddSingleton<ICodexProcess, CodexProcess>();
builder.Services.AddSingleton<IFrontierChat, CodexChatClient>();
builder.Services.Configure<EgressBudgetOptions>(
    builder.Configuration.GetSection(EgressBudgetOptions.SECTION_NAME));
builder.Services.AddSingleton<Dami.Contracts.Privacy.IEgressBudget, EventCountEgressBudget>();

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

app.MapDamiRuntime();
app.Run();

/// <summary>Web entry point exposed for in-memory composition tests.</summary>
public partial class Program
{
}
