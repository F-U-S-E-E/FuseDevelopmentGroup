using System.Threading.Tasks;

namespace Fuse.ExternalEditor.Services;

/// <summary>
/// Abstraction over modal dialogs (the Avalonia replacement for the Python editor's
/// <c>pygame_dialogs</c>). View models depend on this so they stay headless-testable
/// with a fake; the real implementation shows Avalonia windows.
/// </summary>
public interface IDialogService
{
    /// <summary>Prompt for a line of text; returns null if cancelled.</summary>
    Task<string?> PromptInputAsync(string title, string prompt, string initial = "");

    Task<bool> ConfirmAsync(string title, string message);

    Task ShowMessageAsync(string title, string message);
}
