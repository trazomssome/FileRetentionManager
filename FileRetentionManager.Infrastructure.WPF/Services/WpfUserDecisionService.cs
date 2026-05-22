using System.Windows;
using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Services;
using FileRetentionManager.Infrastructure.WPF.Controls;

namespace FileRetentionManager.Infrastructure.WPF.Services;

public sealed class WpfUserDecisionService : IUserDecisionService
{
    public async Task<UserDecision> AskAsync(SequenceStartRequest request, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return AskOnUiThread(request, cancellationToken);
        }

        return await dispatcher.InvokeAsync(() => AskOnUiThread(request, cancellationToken));
    }

    private static UserDecision AskOnUiThread(SequenceStartRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return UserDecision.Rejected;
        }

        var dialog = new DeletionConfirmationDialog(request)
        {
            Owner = FindOwnerWindow()
        };

        var result = dialog.ShowDialog();
        return result == true ? UserDecision.Approved : UserDecision.Rejected;
    }

    private static Window? FindOwnerWindow()
    {
        var application = Application.Current;

        return application?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive) ?? application?.MainWindow;
    }
}
