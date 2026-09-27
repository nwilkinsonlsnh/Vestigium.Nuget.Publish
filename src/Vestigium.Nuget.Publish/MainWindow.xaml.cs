using System.Windows;
using Microsoft.Win32;

namespace Vestigium.Nuget.Publish;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void AddRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Repo root" };
        if (dialog.ShowDialog(this) != true)
            return;
        if (!string.IsNullOrWhiteSpace(dialog.FolderName))
            Vm.AddRoot(dialog.FolderName);
    }

    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        Vm.SaveKey(KeyBox.Password);
        KeyBox.Clear();
    }
}
