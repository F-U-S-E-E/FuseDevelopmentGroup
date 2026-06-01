using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Fuse.ExternalEditor.Services;

/// <summary>Avalonia implementation of <see cref="IDialogService"/> — small modal windows owned by the main window.</summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly Window _owner;

    public AvaloniaDialogService(Window owner) => _owner = owner;

    public async Task<string?> PromptInputAsync(string title, string prompt, string initial = "")
    {
        var box = new TextBox { Text = initial, Width = 300 };
        string? result = null;
        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dialog = NewDialog(title);
        ok.Click += (_, _) => { result = box.Text; dialog.Close(); };
        cancel.Click += (_, _) => { result = null; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = prompt },
                box,
                Buttons(ok, cancel),
            },
        };
        await dialog.ShowDialog(_owner);
        return result;
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var result = false;
        var yes = new Button { Content = "Yes", IsDefault = true };
        var no = new Button { Content = "No", IsCancel = true };
        var dialog = NewDialog(title);
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => { result = false; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children = { new TextBlock { Text = message, MaxWidth = 360, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, Buttons(yes, no) },
        };
        await dialog.ShowDialog(_owner);
        return result;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var ok = new Button { Content = "OK", IsDefault = true };
        var dialog = NewDialog(title);
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children = { new TextBlock { Text = message, MaxWidth = 360, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, Buttons(ok) },
        };
        await dialog.ShowDialog(_owner);
    }

    private static Window NewDialog(string title) => new()
    {
        Title = title,
        SizeToContent = SizeToContent.WidthAndHeight,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
    };

    private static StackPanel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons)
        {
            panel.Children.Add(b);
        }

        return panel;
    }
}
