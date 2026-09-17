using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace Dami.Gui;

/// <summary>Shared image inspection without leaving the active workspace or changing the source.</summary>
public sealed partial class MainWindow
{
    private Grid imageOverlay = null!;
    private ImageViewer imageCanvas = null!;
    private IInputElement? imagePreviousFocus;

    private void InitializeImages()
    {
        this.imageOverlay = Require<Grid>(this, "ImageOverlay");
        this.imageCanvas = Require<ImageViewer>(this, "ImageCanvas");
        this.imageCanvas.ViewChanged += (_, _) =>
            Require<TextBlock>(this, "ImageScale").Text = $"{this.imageCanvas.Scale:P0}";
        Require<Button>(this, "ImageFit").Click += (_, _) => this.imageCanvas.HandleKey(Key.D0);
        Require<Button>(this, "ImageActual").Click += (_, _) => this.imageCanvas.HandleKey(Key.D1);
        Require<Button>(this, "ImageZoomIn").Click += (_, _) => this.imageCanvas.HandleKey(Key.Add);
        Require<Button>(this, "ImageZoomOut").Click += (_, _) => this.imageCanvas.HandleKey(Key.Subtract);
        Require<Button>(this, "ImageClose").Click += (_, _) => this.CloseImage();
        Require<Button>(this, "AttachImages").Click += (_, _) => _ = this.ChooseChatImagesAsync();
        this.AddHandler(Button.ClickEvent, this.OnImageClick);
    }

    private void OnImageClick(object? sender, RoutedEventArgs e)
    {
        switch (e.Source)
        {
            case Button { Tag: "image-preview", DataContext: PendingChatImage image }:
                this.OpenImage(image.Thumbnail, image.FileName, string.Empty);
                break;
            case Button { Tag: "image-remove", DataContext: PendingChatImage image }:
                this.state.RemovePendingImage(image);
                this.SetStatus(GlobalStatus.Success($"Removed {image.FileName} from your draft."));
                this.input.Focus();
                break;
            case Button { Tag: "reply-image", DataContext: Message { Image: { } bitmap } message }:
                this.OpenImage(bitmap, "Image from Dami", message.Body);
                break;
            case Button { Tag: "gallery-image" } when this.state.SelectedGalleryImage is { Image: { } bitmap } card:
                this.OpenImage(bitmap, card.FileName, card.Description);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OpenImage(Bitmap bitmap, string title, string description)
    {
        this.imagePreviousFocus = this.FocusManager?.GetFocusedElement();
        Require<TextBlock>(this, "ImageTitle").Text = title;
        var details = $"{bitmap.PixelSize.Width:N0} × {bitmap.PixelSize.Height:N0} pixels";
        details += string.IsNullOrWhiteSpace(description) ? string.Empty : $" · {description}";
        var label = Require<TextBlock>(this, "ImageDetails");
        label.Text = details;
        ToolTip.SetTip(label, details);
        this.imageOverlay.IsVisible = true;
        Require<DockPanel>(this, "WorkspaceBody").IsEnabled = false;
        this.imageCanvas.ShowImage(bitmap, new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height));
        this.imageCanvas.Focus();
    }

    private void CloseImage()
    {
        this.imageOverlay.IsVisible = false;
        this.imageCanvas.Clear();
        Require<DockPanel>(this, "WorkspaceBody").IsEnabled = true;
        this.imagePreviousFocus?.Focus();
        this.imagePreviousFocus = null;
    }

    private bool HandleImageKey(KeyEventArgs e)
    {
        if (this.imageOverlay?.IsVisible != true)
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            this.CloseImage();
            e.Handled = true;
        }
        else
        {
            e.Handled = this.imageCanvas.HandleKey(e.Key);
        }

        return true;
    }
}
