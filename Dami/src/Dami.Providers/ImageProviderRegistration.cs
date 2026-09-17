using Dami.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dami.Providers;

/// <summary>Registers the one <see cref="IImageGenerator"/> a tier uses (ADR-0035).</summary>
/// <remarks>
/// Shared by both composition roots so the choice cannot drift between them: the Host and
/// the proactive tier draw the same Dami through the same door. Callers bind the options
/// sections themselves; this only decides which implementation stands behind the seam,
/// and whether a second one stands behind that.
/// </remarks>
public static class ImageProviderRegistration
{
    /// <summary>Registers the generator the kind names, with the backup behind it when one is named.</summary>
    /// <param name="services">The collection.</param>
    /// <param name="provider">The primary door.</param>
    /// <param name="backup">The door to retry on, or null (or the primary itself) for none.</param>
    public static IServiceCollection AddImageGenerator(
        this IServiceCollection services, ImageProviderKind provider, ImageProviderKind? backup)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddConcrete(services, provider);
        if (backup is not { } second || second == provider)
        {
            services.AddSingleton<IImageGenerator>(sp => Resolve(sp, provider));
            return services;
        }

        AddConcrete(services, second);
        services.AddSingleton<IImageGenerator>(sp => new FallbackImageGenerator(
            Resolve(sp, provider), Resolve(sp, second), sp.GetRequiredService<ILogger<FallbackImageGenerator>>()));
        return services;
    }

    /// <summary>Keyed doors get a typed HttpClient; the subscription door is a plain singleton.</summary>
    private static void AddConcrete(IServiceCollection services, ImageProviderKind kind)
    {
        switch (kind)
        {
            case ImageProviderKind.Gemini:
                services.AddHttpClient<GeminiImageGenerator>();
                break;
            case ImageProviderKind.OpenAi:
                services.AddHttpClient<OpenAiImageGenerator>();
                break;
            case ImageProviderKind.Codex:
            default:
                services.AddSingleton<CodexSubscriptionImageGenerator>();
                break;
        }
    }

    private static IImageGenerator Resolve(IServiceProvider services, ImageProviderKind kind) => kind switch
    {
        ImageProviderKind.Gemini => services.GetRequiredService<GeminiImageGenerator>(),
        ImageProviderKind.OpenAi => services.GetRequiredService<OpenAiImageGenerator>(),
        _ => services.GetRequiredService<CodexSubscriptionImageGenerator>(),
    };
}
