using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.Infrastructure.WPF.Controls;

public sealed class DeletionConfirmationControl : Control
{
    public static readonly DependencyProperty RequestProperty =
        DependencyProperty.Register(
            nameof(Request),
            typeof(SequenceStartRequest),
            typeof(DeletionConfirmationControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ConfirmCommandProperty =
        DependencyProperty.Register(
            nameof(ConfirmCommand),
            typeof(ICommand),
            typeof(DeletionConfirmationControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CancelCommandProperty =
        DependencyProperty.Register(
            nameof(CancelCommand),
            typeof(ICommand),
            typeof(DeletionConfirmationControl),
            new PropertyMetadata(null));

    static DeletionConfirmationControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(DeletionConfirmationControl),
            new FrameworkPropertyMetadata(typeof(DeletionConfirmationControl)));
    }

    public SequenceStartRequest? Request
    {
        get => (SequenceStartRequest?)GetValue(RequestProperty);
        set => SetValue(RequestProperty, value);
    }

    public ICommand? ConfirmCommand
    {
        get => (ICommand?)GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }
}
