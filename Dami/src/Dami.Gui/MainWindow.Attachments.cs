using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Dami.Gui;

/// <summary>Native attachment gestures, validation feedback and draft-preserving staging.</summary>
public sealed partial class MainWindow
{
    private async Task ChooseChatImagesAsync()
    {
        try
        {
            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Attach images to your message",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif"],
                }],
            }).ConfigureAwait(true);
            await this.StageFilesAsync(files).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            this.ReportAttachmentError(exception);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e) => e.DragEffects = DragDropEffects.Copy;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        _ = this.StageFilesAsync(e.DataTransfer.TryGetFiles());
    }

    private async Task StageFilesAsync(IReadOnlyList<IStorageItem>? files)
    {
        var failures = new List<string>();
        var added = 0;
        foreach (var file in files ?? [])
        {
            try
            {
                var image = await ChatImageInput.FromFileAsync(file, this.lifetime.Token).ConfigureAwait(true);
                this.StageImage(image ?? throw new ArgumentException("Choose a PNG, JPEG, WebP or GIF image."));
                added++;
            }
            catch (Exception exception)
            {
                if (this.lifetime.IsCancellationRequested)
                {
                    return;
                }

                failures.Add($"{file.Name}: {exception.Message}");
            }
        }

        this.ReportStagedFiles(added, failures);
    }

    private void ReportStagedFiles(int added, IReadOnlyList<string> failures)
    {
        if (failures.Count > 0)
        {
            this.SetStatus(GlobalStatus.Failure($"{added} attached. Could not attach {string.Join(" · ", failures)}"));
        }
        else if (added > 0)
        {
            this.SetStatus(GlobalStatus.Success($"{added} image(s) ready. Click a preview to inspect it."));
        }

        this.input.Focus();
    }

    private void OnPaste(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _ = this.PasteAsync();
    }

    private async Task PasteAsync()
    {
        try
        {
            await this.PasteClipboardAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            this.ReportAttachmentError(exception);
        }
    }

    private async Task PasteClipboardAsync()
    {
        var clipboard = this.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        var files = await clipboard.TryGetFilesAsync().ConfigureAwait(true);
        if (files is { Length: > 0 })
        {
            await this.StageFilesAsync(files).ConfigureAwait(true);
            return;
        }

        using var bitmap = await clipboard.TryGetBitmapAsync().ConfigureAwait(true);
        if (bitmap is not null)
        {
            this.StageImage(ChatImageInput.FromBitmap(bitmap));
            this.ReportStagedFiles(1, []);
            return;
        }

        this.InsertPastedText(await clipboard.TryGetTextAsync().ConfigureAwait(true));
    }

    private void StageImage(DirectChatImage image) => this.state.StageImage(new PendingChatImage(image));

    private void ReportAttachmentError(Exception exception)
    {
        if (!this.lifetime.IsCancellationRequested)
        {
            this.SetStatus(GlobalStatus.Failure($"Could not attach image: {exception.Message}"));
        }
    }

    private void InsertPastedText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var current = this.input.Text ?? string.Empty;
        var start = Math.Min(this.input.SelectionStart, this.input.SelectionEnd);
        var end = Math.Max(this.input.SelectionStart, this.input.SelectionEnd);
        this.input.Text = current[..start] + text + current[end..];
        this.input.CaretIndex = start + text.Length;
    }
}
