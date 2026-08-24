using System.Text.Json;
using Dami.Contracts.Proactive;
using Npgsql;

namespace Dami.Gateway.Cli;

/// <summary>One command that answers "is the machine under Dami actually healthy".</summary>
/// <remarks>
/// The runbook's copy-paste health check, codified. The device-binding check exists
/// because it has bitten three times: an inference sidecar can fall back to CPU,
/// keep answering health probes, and serve correctly at a tenth of the speed —
/// a healthy endpoint says nothing about the device (runbook §4.3).
/// </remarks>
public sealed class HealthCommands
{
    private static readonly Uri teiInfo = new("http://127.0.0.1:8080/info");
    private static readonly Uri rerankInfo = new("http://127.0.0.1:8081/info");
    private static readonly Uri ollamaLoaded = new("http://127.0.0.1:11434/api/ps");

    private readonly NpgsqlDataSource dataSource;
    private readonly ISurfacingQueue surfacingQueue;

    /// <summary>Creates the commands.</summary>
    public HealthCommands(NpgsqlDataSource dataSource, ISurfacingQueue surfacingQueue)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(surfacingQueue);

        this.dataSource = dataSource;
        this.surfacingQueue = surfacingQueue;
    }

    /// <summary>Checks every dependency and prints one line each. Exit 1 if any failed.</summary>
    public async Task<int> CheckAsync(CancellationToken cancellationToken)
    {
        var healthy = true;
        healthy &= await this.CheckDatabaseAsync(cancellationToken).ConfigureAwait(false);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        healthy &= await CheckSidecarAsync(httpClient, "embeddings", teiInfo, cancellationToken).ConfigureAwait(false);
        healthy &= await CheckSidecarAsync(httpClient, "reranker", rerankInfo, cancellationToken).ConfigureAwait(false);
        healthy &= await CheckOllamaAsync(httpClient, cancellationToken).ConfigureAwait(false);
        healthy &= await CheckRuntimeApiAsync(httpClient, cancellationToken).ConfigureAwait(false);
        await this.PrintTierAsync(cancellationToken).ConfigureAwait(false);

        return healthy ? 0 : 1;
    }

    private static async Task<bool> CheckRuntimeApiAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient
                .GetAsync(new Uri(DamiApiClient.BASE_URL + "/health"), cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            Console.WriteLine("ok    runtime-api   dami-host on 127.0.0.1:5810");
            return true;
        }
        catch (HttpRequestException exception)
        {
            Console.WriteLine($"FAIL  runtime-api   {exception.Message} - systemctl status dami-host");
            return false;
        }
    }

    private async Task<bool> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = this.dataSource.CreateCommand(
                "select (select count(*) from dami.execution_events)"
                + " || ' events, ' || (select count(*) from dami.observations)"
                + " || ' observations, ' || (select count(*) from dami.conclusions where retracted_at is null)"
                + " || ' active beliefs'");
            var summary = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"ok    postgres      {summary}");
            return true;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            Console.WriteLine($"FAIL  postgres      {exception.Message}");
            return false;
        }
    }

    private static async Task<bool> CheckSidecarAsync(
        HttpClient httpClient,
        string name,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(
                await httpClient.GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false));
            var model = document.RootElement.TryGetProperty("model_id", out var found)
                ? found.GetString()
                : "unknown";
            Console.WriteLine($"ok    {name,-13} {model}");
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.WriteLine($"FAIL  {name,-13} {exception.Message}");
            return false;
        }
    }

    private static async Task<bool> CheckOllamaAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(
                await httpClient.GetStringAsync(ollamaLoaded, cancellationToken).ConfigureAwait(false));

            var models = document.RootElement.GetProperty("models");
            if (models.GetArrayLength() == 0)
            {
                Console.WriteLine("ok    llm           idle (no model loaded; loads on demand)");
                return true;
            }

            var healthy = true;
            foreach (var model in models.EnumerateArray())
            {
                healthy &= PrintPlacement(model);
            }

            return healthy;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.WriteLine($"FAIL  llm           {exception.Message}");
            return false;
        }
    }

    private static bool PrintPlacement(JsonElement model)
    {
        var name = model.GetProperty("name").GetString();
        var total = model.GetProperty("size").GetInt64();
        var inVram = model.TryGetProperty("size_vram", out var vram) ? vram.GetInt64() : 0;

        // The check that has bitten three times: loaded but not on the GPU.
        if (inVram >= total)
        {
            Console.WriteLine($"ok    llm           {name} on GPU");
            return true;
        }

        var percent = total == 0 ? 0 : inVram * 100 / total;
        Console.WriteLine(
            $"WARN  llm           {name} only {percent}% in VRAM - generation will crawl; "
            + "restart dami-llm (runbook 4.3)");
        return false;
    }

    private async Task PrintTierAsync(CancellationToken cancellationToken)
    {
        var pending = 0;
        await foreach (var _ in this.surfacingQueue.PendingAsync(100, cancellationToken).ConfigureAwait(false))
        {
            pending++;
        }

        await using var command = this.dataSource.CreateCommand(
            "select coalesce(max(ran_at)::text, 'never') from dami.proactive_runs");
        var lastRun = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"info  proactive     last pass {lastRun}; {pending} surfacing(s) pending");
    }
}
