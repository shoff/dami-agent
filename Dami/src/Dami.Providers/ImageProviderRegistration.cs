using Dami.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Dami.Providers;

/// <summary>Registers the one <see cref="IImageGenerator"/> a tier uses (ADR-0035).</summary>
/// <remarks>
/// Shared by both composition roots so the choice cannot drift between them: the Host and
/// the proactive tier draw the same Dami through the same door. Callers bind the options
/// sections themselves; this only decides which implementation stands behind the seam.
/// </remarks>
public static class ImageProviderRegistration
{
    /// <summary>Registers the generator the kind names. Keyed doors get a typed HttpClient.</summary>
    public static IServiceCollection AddImageGenerator(
        this IServiceCollection services, ImageProviderKind provider)
    {
        ArgumentNullException.ThrowIfNull(services);

        switch (provider)
        {
            case ImageProviderKind.Gemini:
                services.AddHttpClient<IImageGenerator, GeminiImageGenerator>();
                break;
            case ImageProviderKind.OpenAi:
                services.AddHttpClient<IImageGenerator, OpenAiImageGenerator>();
                break;
            case ImageProviderKind.Codex:
            default:
                services.AddSingleton<IImageGenerator, CodexSubscriptionImageGenerator>();
                break;
        }

        return services;
    }
}
