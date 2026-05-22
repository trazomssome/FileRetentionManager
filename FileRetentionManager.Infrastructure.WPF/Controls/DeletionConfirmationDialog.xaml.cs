using System.Windows;
using System.Windows.Input;
using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.Infrastructure.WPF.Controls;

public partial class DeletionConfirmationDialog : Window
{
    public DeletionConfirmationDialog(SequenceStartRequest request)
    {
        InitializeComponent();
        DataContext = new DeletionConfirmationDialogViewModel(request, Approve, Reject);
    }

    private void Approve()
    {
        DialogResult = true;
        Close();
    }

    private void Reject()
    {
        DialogResult = false;
        Close();
    }
}

internal sealed class DeletionConfirmationDialogViewModel
{
    public DeletionConfirmationDialogViewModel(SequenceStartRequest request, Action approve, Action reject)
    {
        Request = request;
        ConfirmCommand = new DialogCommand(approve);
        CancelCommand = new DialogCommand(reject);
    }

    public SequenceStartRequest Request { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }
}

internal sealed class DialogCommand : ICommand
{
    private readonly Action execute;

    public DialogCommand(Action execute)
    {
        this.execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        execute();
    }
}
