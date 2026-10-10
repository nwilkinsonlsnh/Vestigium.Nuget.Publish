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

    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        var name = Vm.SelectedKeyName;
        if (name == MainViewModel.NewKey)
        {
            name = AskName();
            if (string.IsNullOrWhiteSpace(name))
                return;
        }

        Vm.SaveNamed(name, KeyBox.Password);
    }

    private void DeleteKey_Click(object sender, RoutedEventArgs e) => Vm.ForgetSelected();

    private string? AskName()
    {
        var window = new Window
        {
            Title = "Key name",
            Width = 480,
            Height = 196,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)FindResource("Vestigium.Brushes.Surface.Window")
        };
        var box = new TextBox
        {
            Style = (Style)FindResource("TextBox.Standard"),
            Margin = new Thickness(16, 8, 16, 0),
            MaxLength = 35,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var count = new TextBlock
        {
            Text = "0 of 35",
            Margin = new Thickness(16, 4, 16, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = (System.Windows.Media.Brush)FindResource("Vestigium.Brushes.Text.Secondary")
        };
        box.TextChanged += (_, _) => count.Text = box.Text.Length + " of 35";
        var label = new TextBlock
        {
            Text = "Name this key",
            Margin = new Thickness(16, 16, 16, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("Vestigium.Brushes.Text.Primary")
        };
        var save = new Button
        {
            Content = "Save",
            Style = (Style)FindResource("Button.Primary"),
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(16, 4, 16, 4),
            IsDefault = true
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Style = (Style)FindResource("Button.Secondary"),
            Padding = new Thickness(16, 4, 16, 4),
            IsCancel = true
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16)
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(label, 0);
        Grid.SetRow(box, 1);
        Grid.SetRow(count, 2);
        Grid.SetRow(buttons, 3);
        panel.Children.Add(label);
        panel.Children.Add(box);
        panel.Children.Add(count);
        panel.Children.Add(buttons);
        window.Content = panel;
        string? name = null;
        save.Click += (_, _) =>
        {
            name = box.Text.Trim();
            if (name.Length == 0)
                return;
            window.DialogResult = true;
        };
        window.Loaded += (_, _) => box.Focus();
        return window.ShowDialog() == true ? name : null;
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
        Vm.RememberTypedKey(KeyBox.Password);
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
