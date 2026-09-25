using System.Windows;
using WhisperMd.App.ViewModels;

namespace WhisperMd.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
