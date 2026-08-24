using Dami.Contracts.Models;
using Dami.Core.Context;
using Dami.Core.Frontier;
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
builder.Services.AddSingleton<ITurnRunner, TurnRunner>();
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

// Approvals execute in the runtime (D-005): librarian manifests and egress briefs.
builder.Services.AddSingleton<ManifestExecutor>();
builder.Services.AddSingleton<BriefExecutor>();
builder.Services.AddSingleton<Dami.Contracts.Privacy.IPromptRedactor, PromptRedactor>();

// Frontier: subscription door (ADR-0011) behind the C5 egress budget.
builder.Services.Configure<CodexOptions>(builder.Configuration.GetSection(CodexOptions.SECTION_NAME));
builder.Services.AddSingleton<ICodexProcess, CodexProcess>();
builder.Services.AddSingleton<IFrontierChat, CodexChatClient>();
builder.Services.Configure<EgressBudgetOptions>(
    builder.Configuration.GetSection(EgressBudgetOptions.SECTION_NAME));
builder.Services.AddSingleton<Dami.Contracts.Privacy.IEgressBudget, EventCountEgressBudget>();

var app = builder.Build();

// J3 first cut: a zero-install conversation + live-graph view, rendered entirely
// from the same endpoints every other client uses. Localhost-only like the API.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapDamiRuntime();
app.Run();
