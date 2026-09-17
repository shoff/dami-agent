using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace Dami.Gui;

/// <summary>Local workspace navigation, searchable commands and conversation affordances.</summary>
public sealed partial class MainWindow
{
    private readonly WorkspaceState workspace = new();
    private TabControl workspaceTabs = null!;
    private Grid commandOverlay = null!;
    private TextBox commandSearch = null!;
    private ListBox commandResults = null!;
    private IInputElement? previousFocus;

    private void InitializeWorkspace()
    {
        this.workspaceTabs = Require<TabControl>(this, "WorkspaceTabs");
        this.commandOverlay = Require<Grid>(this, "CommandOverlay");
        this.commandSearch = Require<TextBox>(this, "CommandSearch");
        this.commandResults = Require<ListBox>(this, "CommandResults");
        Require<Button>(this, "CommandMenuButton").Click += (_, _) => this.ToggleCommandMenu();
        Require<Button>(this, "CommandClose").Click += (_, _) => this.CloseCommandMenu(true);
        Require<ToggleButton>(this, "FocusToggle").Click += (_, _) =>
            this.ExecuteWorkspaceCommand(WorkspaceCommands.Search("toggle focus")[0]);
        this.commandSearch.TextChanged += (_, _) => this.FilterCommands();
        this.commandResults.PointerReleased += this.OnCommandPointerReleased;
        this.commandOverlay.PointerPressed += (_, args) =>
        {
            if (ReferenceEquals(args.Source, this.commandOverlay))
            {
                this.CloseCommandMenu(true);
            }
        };
    }

    private void ToggleCommandMenu()
    {
        if (this.commandOverlay.IsVisible)
        {
            this.CloseCommandMenu(true);
            return;
        }

        this.previousFocus = this.FocusManager?.GetFocusedElement();
        this.commandOverlay.IsVisible = true;
        this.commandSearch.Text = string.Empty;
        this.FilterCommands();
        this.commandSearch.Focus();
    }

    private void FilterCommands()
    {
        var commands = WorkspaceCommands.Search(this.commandSearch.Text);
        this.commandResults.ItemsSource = commands;
        this.commandResults.SelectedIndex = commands.Count > 0 ? 0 : -1;
        Require<TextBlock>(this, "CommandEmpty").IsVisible = commands.Count == 0;
    }

    private void CloseCommandMenu(bool restoreFocus)
    {
        this.commandOverlay.IsVisible = false;
        if (restoreFocus)
        {
            this.previousFocus?.Focus();
        }
    }

    private void ExecuteWorkspaceCommand(WorkspaceCommand command)
    {
        this.CloseCommandMenu(false);
        this.workspace.Activate(command);
        this.workspaceTabs.SelectedIndex = this.workspace.PageIndex;
        Require<ScrollViewer>(this, "AttentionPanel").IsVisible = !this.workspace.IsFocusMode;
        Require<Grid>(this, "ConversationLayout").ColumnDefinitions[1].Width =
            new GridLength(this.workspace.IsFocusMode ? 0 : 330);
        Require<ToggleButton>(this, "FocusToggle").IsChecked = this.workspace.IsFocusMode;
        if (command.PageIndex == 0)
        {
            this.input.Focus();
        }
        else
        {
            this.workspaceTabs.Focus();
        }
    }

    private bool HandleWorkspaceKey(KeyEventArgs args)
    {
        if (args.Key == Key.K && args.KeyModifiers == KeyModifiers.Control)
        {
            this.ToggleCommandMenu();
            return true;
        }

        if (this.commandOverlay.IsVisible)
        {
            return this.HandleCommandMenuKey(args);
        }

        if (this.HandleConversationFindKey(args))
        {
            return true;
        }

        if (WorkspaceCommands.FromKey(args.Key, args.KeyModifiers) is { } command)
        {
            this.ExecuteWorkspaceCommand(command);
            return true;
        }

        return false;
    }

    private bool HandleCommandMenuKey(KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            this.CloseCommandMenu(true);
            return true;
        }

        if (args.Key == Key.Enter)
        {
            this.OpenSelectedCommand();
            return true;
        }

        if (args.Key == Key.Down && this.commandSearch.IsFocused)
        {
            this.commandResults.Focus();
            if (this.commandResults.ContainerFromIndex(0) is ListBoxItem item)
            {
                item.Focus();
            }

            return true;
        }

        return false;
    }

    private void OpenSelectedCommand()
    {
        if (this.commandResults.SelectedItem is WorkspaceCommand command)
        {
            this.ExecuteWorkspaceCommand(command);
        }
    }

    private void OnCommandPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (args.InitialPressMouseButton == MouseButton.Left
            && args.Source is Control control
            && (control is ListBoxItem || control.FindAncestorOfType<ListBoxItem>() is not null))
        {
            this.OpenSelectedCommand();
            args.Handled = true;
        }
    }

    private async Task CopyMessageAsync(Message message)
    {
        if (message.CanCopy)
        {
            await this.CopyTextAsync(message.Body, "Message").ConfigureAwait(true);
        }
    }

    private async Task CopyTextAsync(string text, string description)
    {
        if (this.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
            this.SetStatus(GlobalStatus.Success($"{description} copied to clipboard."));
        }
        catch (Exception exception)
        {
            this.SetStatus(GlobalStatus.Failure($"Could not copy: {exception.Message}"));
        }
    }
}
