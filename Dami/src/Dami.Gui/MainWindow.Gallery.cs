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
    private TextBox gallerySearch = null!;
    private TextBox galleryEdit = null!;
    private Button galleryEditRun = null!;
    private Button gallerySimilar = null!;
    private Button galleryFavourite = null!;
    private Button galleryHide = null!;
    private Button galleryLineage = null!;
    private ComboBox gallerySourceFilter = null!;
    private CheckBox galleryFavouritesOnly = null!;
    private CheckBox galleryShowHidden = null!;
    private IReadOnlyList<GalleryImageCard> galleryAll = [];
    private Button galleryGenerate = null!;
    private Button galleryRefresh = null!;
    private Button galleryImport = null!;
    private Grid gallerySurface = null!;

    private void InitializeGallery()
    {
        this.galleryList = Require<ListBox>(this, "GalleryList");
        this.galleryPrompt = Require<TextBox>(this, "GalleryPrompt");
        this.gallerySearch = Require<TextBox>(this, "GallerySearch");
        this.gallerySearch.KeyDown += this.OnGallerySearchKeyDown;
        this.galleryEdit = Require<TextBox>(this, "GalleryEdit");
        this.galleryEditRun = Require<Button>(this, "GalleryEditRun");
        this.gallerySimilar = Require<Button>(this, "GallerySimilar");
        this.galleryEditRun.Click += (_, _) => _ = this.EditGalleryImageAsync();
        this.gallerySimilar.Click += (_, _) => _ = this.SimilarGalleryImagesAsync();
        this.InitializeGalleryFilters();
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

    private void OnGallerySearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        var query = this.gallerySearch.Text?.Trim() ?? string.Empty;
        _ = query.Length == 0 ? this.LoadGalleryAsync(announce: true) : this.SearchGalleryAsync(query);
    }

    /// <summary>Search by what is in the pictures (ADR-0031); an empty box is the full list again.</summary>
    private async Task SearchGalleryAsync(string query)
    {
        this.state.GalleryMessage = $"searching for “{query}”…";
        using var response = await this.runtime
            .GetAsync("/gallery/search?q=" + Uri.EscapeDataString(query) + "&limit=24", this.lifetime.Token)
            .ConfigureAwait(true);
        if (response is null)
        {
            this.state.GalleryMessage = "The runtime did not answer /gallery/search.";
            this.SetStatus(GlobalStatus.Failure(this.state.GalleryMessage));
            return;
        }

        var items = await this.CardsAsync(response).ConfigureAwait(true);
        Replace(this.state.GalleryImages, items);
        this.galleryList.SelectedItem = items.FirstOrDefault();
        this.state.SelectedGalleryImage = items.FirstOrDefault();
        this.state.GalleryMessage = items.Count == 0
            ? $"nothing matches “{query}” — the curator may not have captioned everything yet"
            : $"{items.Count} match(es) for “{query}” · clear the box and press Enter for all";
    }

    private void InitializeGalleryFilters()
    {
        this.galleryFavourite = Require<Button>(this, "GalleryFavourite");
        this.galleryHide = Require<Button>(this, "GalleryHide");
        this.galleryLineage = Require<Button>(this, "GalleryLineage");
        this.gallerySourceFilter = Require<ComboBox>(this, "GallerySourceFilter");
        this.galleryFavouritesOnly = Require<CheckBox>(this, "GalleryFavouritesOnly");
        this.galleryShowHidden = Require<CheckBox>(this, "GalleryShowHidden");
        this.galleryFavourite.Click += (_, _) => _ = this.FlagSelectedAsync(favourite: true);
        this.galleryHide.Click += (_, _) => _ = this.FlagSelectedAsync(favourite: false);
        this.galleryLineage.Click += (_, _) => this.SelectGalleryImage(this.state.SelectedGalleryImage?.DerivedFrom);
        this.gallerySourceFilter.SelectionChanged += (_, _) => this.ApplyGalleryFilter();
        this.galleryFavouritesOnly.IsCheckedChanged += (_, _) => this.ApplyGalleryFilter();
        this.galleryShowHidden.IsCheckedChanged += (_, _) => _ = this.LoadGalleryAsync(false);
    }

    /// <summary>The loaded cards through the source, favourite and hidden filters.</summary>
    private void ApplyGalleryFilter()
    {
        var source = (this.gallerySourceFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "all sources";
        var favouritesOnly = this.galleryFavouritesOnly.IsChecked == true;
        var items = this.galleryAll
            .Where(card => source == "all sources" || card.Source == source)
            .Where(card => !favouritesOnly || card.Favourite)
            .ToList();
        Replace(this.state.GalleryImages, items);
        if (!items.Contains(this.state.SelectedGalleryImage!))
        {
            this.galleryList.SelectedItem = items.FirstOrDefault();
            this.state.SelectedGalleryImage = items.FirstOrDefault();
        }

        this.state.GalleryMessage = items.Count == this.galleryAll.Count
            ? $"{items.Count} portraits"
            : $"{items.Count} of {this.galleryAll.Count} portraits";
    }

    /// <summary>Toggles the selected picture's favourite or hidden flag on the runtime, then reloads.</summary>
    private async Task FlagSelectedAsync(bool favourite)
    {
        if (this.state.SelectedGalleryImage is not { } selected)
        {
            return;
        }

        var body = favourite
            ? new { favourite = (bool?)!selected.Favourite, hidden = (bool?)null }
            : new { favourite = (bool?)null, hidden = (bool?)!selected.Hidden };
        using var result = await this.runtime.PostAsync(
            "/gallery/" + Uri.EscapeDataString(selected.FileName) + "/flags", body, this.lifetime.Token)
            .ConfigureAwait(true);
        if (result is null)
        {
            this.state.GalleryMessage = "The runtime did not answer /gallery/{file}/flags.";
            return;
        }

        await this.LoadGalleryAsync(false).ConfigureAwait(true);
        this.SelectGalleryImage(selected.FileName);
    }

    private void SelectGalleryImage(string? fileName)
    {
        if (fileName is null)
        {
            return;
        }

        var match = this.state.GalleryImages.FirstOrDefault(card => card.FileName == fileName);
        if (match is not null)
        {
            this.galleryList.SelectedItem = match;
            this.state.SelectedGalleryImage = match;
        }
    }

    /// <summary>The pictures most like the selected one (ADR-0031).</summary>
    private async Task SimilarGalleryImagesAsync()
    {
        if (this.state.SelectedGalleryImage is not { } selected)
        {
            return;
        }

        this.state.GalleryMessage = "finding pictures like this one…";
        using var response = await this.runtime
            .GetAsync("/gallery/" + Uri.EscapeDataString(selected.FileName) + "/similar?limit=24", this.lifetime.Token)
            .ConfigureAwait(true);
        if (response is null)
        {
            this.state.GalleryMessage = "The runtime did not answer /gallery/{file}/similar.";
            return;
        }

        var items = await this.CardsAsync(response).ConfigureAwait(true);
        items.Insert(0, selected);
        Replace(this.state.GalleryImages, items);
        this.galleryList.SelectedItem = selected;
        this.state.GalleryMessage = items.Count == 1
            ? "nothing similar yet — the curator may not have captioned this one"
            : $"{items.Count - 1} picture(s) like this one · clear the search box and press Enter for all";
    }

    /// <summary>Changes the selected picture as instructed; the result is a new picture linked to it.</summary>
    private async Task EditGalleryImageAsync()
    {
        var instruction = this.galleryEdit.Text?.Trim();
        if (this.state.SelectedGalleryImage is not { } selected || string.IsNullOrWhiteSpace(instruction))
        {
            this.state.GalleryMessage = "Select a picture and say what to change.";
            return;
        }

        this.galleryEditRun.IsEnabled = false;
        this.galleryEditRun.Content = "editing…";
        this.SetStatus(GlobalStatus.Working("Editing the selected picture…"));
        try
        {
            using var result = await this.runtime.PostAsync(
                "/gallery/" + Uri.EscapeDataString(selected.FileName) + "/edit", new { instruction }, this.lifetime.Token)
                .ConfigureAwait(true);
            this.galleryEdit.Text = string.Empty;
            await this.FinishGenerationAsync(result).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.state.GalleryMessage = exception.Message;
            this.SetStatus(GlobalStatus.Failure(exception.Message));
        }
        finally
        {
            this.galleryEditRun.IsEnabled = true;
            this.galleryEditRun.Content = "edit selected";
        }
    }

    private async Task<List<GalleryImageCard>> CardsAsync(System.Text.Json.JsonDocument response)
    {
        var items = new List<GalleryImageCard>();
        foreach (var element in response.RootElement.EnumerateArray())
        {
            var item = GalleryImageCard.From(element);
            item.Image = await this.LoadGalleryBitmapAsync(item.FileName).ConfigureAwait(true);
            items.Add(item);
        }

        return items;
    }

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

        var path = this.galleryShowHidden?.IsChecked == true ? "/gallery?hidden=true" : "/gallery";
        using var response = await this.runtime.GetAsync(path, this.lifetime.Token)
            .ConfigureAwait(true);
        if (response is null)
        {
            this.state.GalleryMessage = "The runtime did not answer /gallery.";
            this.SetStatus(GlobalStatus.Failure(this.state.GalleryMessage));
            return;
        }

        var items = await this.CardsAsync(response).ConfigureAwait(true);
        this.galleryAll = items;
        this.ShowGallery(items);
        this.ApplyGalleryFilter();
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
