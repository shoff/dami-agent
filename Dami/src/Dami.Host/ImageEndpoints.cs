namespace Dami.Host;

/// <summary>Interactive image generation over the loopback runtime surface.</summary>
public static class ImageEndpoints
{
    /// <summary>Maps the image route.</summary>
    public static void Map(WebApplication app)
    {
        app.MapPost("/images/generate", async (
            ImageGenerationRequest request,
            InteractiveImageGenerator generator,
            CancellationToken cancellationToken) =>
        {
            var image = await generator.GenerateAsync(request.Prompt, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(new
            {
                image.FileName,
                image.ContentType,
                Bytes = image.Bytes.ToArray(),
                image.Prompt,
            });
        });
        MapGallery(app);
    }

    private static void MapGallery(WebApplication app)
    {
        app.MapGet("/gallery", async (
            GalleryCatalog catalog, CancellationToken cancellationToken) =>
            Results.Ok(await catalog.ListAsync(cancellationToken).ConfigureAwait(false)));
        app.MapGet("/gallery/search", async (
            string q, int? limit, GalleryCatalog catalog, CancellationToken cancellationToken) =>
            string.IsNullOrWhiteSpace(q)
                ? Results.BadRequest(new { error = "q is required" })
                : Results.Ok(await catalog.SearchAsync(q, Math.Clamp(limit ?? 12, 1, 60), cancellationToken)
                    .ConfigureAwait(false)));
        app.MapGet("/gallery/{fileName}", (string fileName, ImageGallery gallery) =>
            gallery.Resolve(fileName) is { } path
                ? Results.File(path, ContentType(path))
                : Results.NotFound());
        app.MapPost("/gallery/generate", async (
            ImageGenerationRequest request,
            GalleryImageGenerator generator,
            CancellationToken cancellationToken) =>
            Results.Ok(await generator.GenerateAsync(request.Prompt, cancellationToken)
                .ConfigureAwait(false)));
        app.MapPost("/gallery/import", async (
            GalleryImportRequest request,
            ImageGallery gallery,
            CancellationToken cancellationToken) =>
            Results.Ok(await gallery.ImportAsync(request.Images, cancellationToken)
                .ConfigureAwait(false)))
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(256 * 1024 * 1024));
    }

    private static string ContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/png",
        };
}

/// <summary>An explicit user request to create an image.</summary>
public sealed record ImageGenerationRequest(string Prompt);

/// <summary>A local-only batch copied into Dami's gallery.</summary>
public sealed record GalleryImportRequest(IReadOnlyList<GalleryImport> Images);
