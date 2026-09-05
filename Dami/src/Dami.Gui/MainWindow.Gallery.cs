using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Dami.Gui;

/// <summary>Dami's persisted portraits and identity-preserving hosted generator.</summary>
public sealed partial class MainWindow
{
    private ListBox galleryList = null!;
    private TextBox galleryPrompt = null!;
    private Button galleryGenerate = null!;
    private Button galleryRefresh = null!;
    private Button galleryImport = null!;
    private Grid gallerySurface = null!;

    private void InitializeGallery()
    {
        this.galleryList = Require<ListBox>(this, "GalleryList");
        this.galleryPrompt = Require<TextBox>(this, "GalleryPrompt");
        this.galleryGenerate = Require<Button>(this, "GalleryGenerate");
        this.galleryRefresh = Require<Button>(this, "GalleryRefresh");
        this.galleryImport = Require<Button>(this, "GalleryImport");
        this.gallerySurface = Require<Grid>(this, "GallerySurface");
        this.galleryList.SelectionChanged += this.OnGallerySelected;
        this.galleryGenerate.Click += this.OnGalleryGenerate;
        this.galleryRefresh.Click += this.OnGalleryRefresh;
        this.galleryImport.Click += this.OnGalleryImport;
        DragDrop.SetAllowDrop(this.gallerySurface, true);
        DragDrop.AddDragOverHandler(this.gallerySurface, this.OnGalleryDragOver);
        DragDrop.AddDropHandler(this.gallerySurface, this.OnGalleryDrop);
        _ = this.LoadGalleryAsync(false);
    }

    private void OnGallerySelected(object? sender, SelectionChangedEventArgs e) =>
        this.state.SelectedGalleryImage = this.galleryList.SelectedItem as GalleryImageCard;

    private void OnGalleryGenerate(object? sender, RoutedEventArgs e) =>
        _ = this.GenerateGalleryImageAsync();

    private void OnGalleryRefresh(object? sender, RoutedEventArgs e) =>
        _ = this.LoadGalleryAsync(true);

    private void OnGalleryImport(object? sender, RoutedEventArgs e) =>
        _ = this.ChooseGalleryImagesAsync();

    private void OnGalleryDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = DragDropEffects.Copy;

    private void OnGalleryDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        _ = this.ImportGalleryFilesAsync(e.DataTransfer.TryGetFiles());
    }

    private async Task LoadGalleryAsync(bool announce)
    {
        this.state.GalleryMessage = "loading portraits…";
        if (announce)
        {
            this.SetStatus(GlobalStatus.Working("Refreshing Dami's gallery…"));
        }

        using var response = await this.runtime.GetAsync("/gallery", this.lifetime.Token)
            .ConfigureAwait(true);
        if (response is null)
        {
            this.state.GalleryMessage = "The runtime did not answer /gallery.";
            this.SetStatus(GlobalStatus.Failure(this.state.GalleryMessage));
            return;
        }

        var items = new List<GalleryImageCard>();
        foreach (var element in response.RootElement.EnumerateArray())
        {
            var item = GalleryImageCard.From(element);
            item.Image = await this.LoadGalleryBitmapAsync(item.FileName).ConfigureAwait(true);
            items.Add(item);
        }

        this.ShowGallery(items);
        if (announce)
        {
            this.SetStatus(GlobalStatus.Success($"Gallery refreshed · {items.Count} portraits."));
        }
    }

    private async Task<Bitmap?> LoadGalleryBitmapAsync(string fileName)
    {
        var path = "/gallery/" + Uri.EscapeDataString(fileName);
        var bytes = await this.runtime.GetBytesAsync(path, this.lifetime.Token).ConfigureAwait(true);
        return bytes is null ? null : new Bitmap(new MemoryStream(bytes));
    }

    private void ShowGallery(IReadOnlyList<GalleryImageCard> items)
    {
        Replace(this.state.GalleryImages, items);
        this.galleryList.SelectedItem = items.FirstOrDefault();
        this.state.SelectedGalleryImage = items.FirstOrDefault();
        this.state.GalleryMessage = items.Count == 1
            ? "1 portrait"
            : $"{items.Count} portraits · hosted OpenAI generation · one identity anchor";
    }

    private async Task GenerateGalleryImageAsync()
    {
        var prompt = this.galleryPrompt.Text?.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            this.state.GalleryMessage = "Describe a scene or emotional beat first.";
            return;
        }

        this.galleryGenerate.IsEnabled = false;
        this.galleryGenerate.Content = "creating…";
        this.state.GalleryMessage = "creating a new Dami portrait…";
        this.SetStatus(GlobalStatus.Working("Creating a new Dami portrait…"));
        try
        {
            using var result = await this.runtime.PostAsync(
                "/gallery/generate", new { prompt }, this.lifetime.Token).ConfigureAwait(true);
            await this.FinishGenerationAsync(result).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.state.GalleryMessage = exception.Message;
            this.SetStatus(GlobalStatus.Failure(exception.Message));
        }
        finally
        {
            this.galleryGenerate.IsEnabled = true;
            this.galleryGenerate.Content = "create new Dami portrait";
        }
    }

    private async Task FinishGenerationAsync(System.Text.Json.JsonDocument? result)
    {
        if (result is null || !result.RootElement.TryGetProperty("fileName", out _))
        {
            this.state.GalleryMessage = result?.RootElement.TryGetProperty("error", out var error) is true
                ? error.GetString() ?? "Image generation failed."
                : "Image generation failed.";
            this.SetStatus(GlobalStatus.Failure(this.state.GalleryMessage));
            return;
        }

        this.galleryPrompt.Text = string.Empty;
        await this.LoadGalleryAsync(false).ConfigureAwait(true);
        this.SetStatus(GlobalStatus.Success("New Dami portrait received and saved."));
    }

    private async Task ChooseGalleryImagesAsync()
    {
        this.SetStatus(GlobalStatus.Working("Choose one or more images to import…"));
        var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import images into Dami's gallery",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Images")
            {
                Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif"],
            }],
        }).ConfigureAwait(true);
        if (files.Count == 0)
        {
            this.SetStatus(GlobalStatus.Success("Image import cancelled."));
            return;
        }

        await this.ImportGalleryFilesAsync(files).ConfigureAwait(true);
    }

    private async Task ImportGalleryFilesAsync(IReadOnlyList<IStorageItem>? files)
    {
        var images = await ReadGalleryImportsAsync(files, this.lifetime.Token).ConfigureAwait(true);
        if (images.Count == 0)
        {
            this.SetStatus(GlobalStatus.Failure("No supported image files were selected."));
            return;
        }

        this.galleryImport.IsEnabled = false;
        this.SetStatus(GlobalStatus.Working($"Importing {images.Count} image(s)…"));
        try
        {
            using var result = await this.runtime.PostAsync(
                "/gallery/import", new { images }, this.lifetime.Token).ConfigureAwait(true);
            await this.FinishImportAsync(result, images.Count).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.SetStatus(GlobalStatus.Failure(exception.Message));
        }
        finally
        {
            this.galleryImport.IsEnabled = true;
        }
    }

    private static async Task<IReadOnlyList<DirectChatImage>> ReadGalleryImportsAsync(
        IReadOnlyList<IStorageItem>? files, CancellationToken cancellationToken)
    {
        var images = new List<DirectChatImage>();
        foreach (var file in files ?? [])
        {
            if (await ChatImageInput.FromFileAsync(file, cancellationToken).ConfigureAwait(true)
                is { } image)
            {
                images.Add(image);
            }
        }

        return images;
    }

    private async Task FinishImportAsync(System.Text.Json.JsonDocument? result, int expected)
    {
        if (result is null || result.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            var error = result?.RootElement.TryGetProperty("error", out var detail) is true
                ? detail.GetString() ?? "Image import failed."
                : "Image import failed.";
            this.SetStatus(GlobalStatus.Failure(error));
            return;
        }

        await this.LoadGalleryAsync(false).ConfigureAwait(true);
        this.SetStatus(GlobalStatus.Success($"Imported {expected} image(s) into Dami's gallery."));
    }
}
