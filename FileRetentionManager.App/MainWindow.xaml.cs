using System.Windows;
using FileRetentionManager.App.ViewModels;

namespace FileRetentionManager.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
