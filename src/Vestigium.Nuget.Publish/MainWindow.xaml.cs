using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Vestigium.Nuget.Publish;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closed += (_, _) =>
        {
            KeyBox.Clear();
            Vm.ForgetKey();
        };
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

    private void KeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        Vm.ApiKey = KeyBox.Password;
        Vm.NotifyKey();
    }

    private void RepoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list)
            return;
        Vm.ApplyRepoSelection(list.SelectedItems.OfType<RepoRow>());
    }
}
