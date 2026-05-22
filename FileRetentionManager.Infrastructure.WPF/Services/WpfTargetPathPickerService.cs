using System.Windows;
using FileRetentionManager.Domain.Services;
using Microsoft.Win32;

namespace FileRetentionManager.Infrastructure.WPF.Services;

public sealed class WpfTargetPathPickerService : ITargetPathPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return PickFolder(cancellationToken);
        }

        return await dispatcher.InvokeAsync(() => PickFolder(cancellationToken));
    }

    private static string? PickFolder(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Select a target folder",
            Multiselect = false
        };

        return dialog.ShowDialog(FindOwnerWindow()) == true ? dialog.FolderName : null;
    }

    private static Window? FindOwnerWindow()
    {
        var application = Application.Current;

        return application?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive) ?? application?.MainWindow;
    }
}
