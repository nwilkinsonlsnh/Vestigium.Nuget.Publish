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
        Vm.KeyChanged += ApplyKey;
        ApplyKey();
    }

    private bool _applying;

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void ApplyKey()
    {
        _applying = true;
        KeyBox.Password = Vm.ApiKey ?? "";
        _applying = false;
        Vm.NotifyKey();
    }

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
        if (_applying)
            return;
        Vm.SaveKey(KeyBox.Password);
        Vm.NotifyKey();
    }

    private void SessionLog_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        box.CaretIndex = box.Text.Length;
        box.ScrollToEnd();
    }

    private void RepoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list)
            return;
        Vm.ApplyRepoSelection(list.SelectedItems.OfType<RepoRow>());
    }
}

public sealed class InverseBoolConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
