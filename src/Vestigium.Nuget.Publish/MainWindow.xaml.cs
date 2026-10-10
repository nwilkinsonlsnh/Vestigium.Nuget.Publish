using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Vestigium.Nuget.Publish;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Vm.KeyChanged += ApplyKey;
        Vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainViewModel.Log))
                return;
            if (Dispatcher.CheckAccess())
                RenderLog();
            else
                Dispatcher.BeginInvoke(RenderLog);
        };
        ApplyKey();
        RenderLog();
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

    private void RenderLog()
    {
        var doc = SessionLog.Document;
        doc.Blocks.Clear();
        foreach (var line in (Vm.Log ?? "").Split(Environment.NewLine))
        {
            var brush = LineBrush(line);
            foreach (var shown in DisplayLines(line))
            {
                var run = new Run(shown) { Foreground = brush };
                doc.Blocks.Add(new Paragraph(run) { Margin = new Thickness(shown.StartsWith("  ") ? 16 : 0, 0, 0, 0) });
            }
        }

        SessionLog.ScrollToEnd();
    }

    private System.Windows.Media.Brush LineBrush(string line)
    {
        var ink = (System.Windows.Media.Brush)FindResource("Vestigium.Brushes.Text.Primary");
        var lower = line.Trim().ToLowerInvariant();
        if (lower.Contains("success") || lower.Contains("was pushed"))
            return System.Windows.Media.Brushes.LimeGreen;
        if (lower.Contains("error") || lower.Contains("failure") || lower.Contains("failed"))
            return System.Windows.Media.Brushes.Red;
        if (lower.Contains("warning") || lower.StartsWith("warn"))
            return System.Windows.Media.Brushes.Yellow;
        if (lower.StartsWith("git ") || lower.StartsWith("git") && lower.Contains("fetch"))
            return System.Windows.Media.Brushes.DeepSkyBlue;
        if (line.Contains(".exe", StringComparison.OrdinalIgnoreCase))
            return (System.Windows.Media.Brush)FindResource("Vestigium.Brushes.Accent.Primary");
        if (lower.StartsWith("push") || lower.Contains(" pushing "))
            return BrushOr("Vestigium.Brushes.Status.Warning", System.Windows.Media.Brushes.Goldenrod);
        if (lower.StartsWith("pack") || lower.Contains("packing "))
            return BrushOr("Vestigium.Brushes.Accent.Secondary", (System.Windows.Media.Brush)FindResource("Vestigium.Brushes.Accent.Primary"));
        return ink;
    }

    private System.Windows.Media.Brush BrushOr(string key, System.Windows.Media.Brush fallback)
        => TryFindResource(key) as System.Windows.Media.Brush ?? fallback;

    private static IEnumerable<string> DisplayLines(string line)
    {
        var joined = line.IndexOf(",Readme missing", StringComparison.OrdinalIgnoreCase);
        if (joined > 0)
        {
            foreach (var part in DisplayLines(line[..joined].Trim().TrimEnd(',')))
                yield return part;
            foreach (var part in DisplayLines("warning " + line[(joined + 1)..].Trim()))
                yield return part;
            yield break;
        }

        var duplicate = Regex.Match(line, @"warning (NU\d+): File '([^']+)' is not added because the package already contains file '([^']+)'", RegexOptions.IgnoreCase);
        if (duplicate.Success)
        {
            yield return $"warning {duplicate.Groups[1].Value}  {Path.GetFileName(duplicate.Groups[2].Value)}";
            yield return $"  already in {duplicate.Groups[3].Value}";
            yield break;
        }

        var framework = Regex.Match(line, @"warning (NU\d+):(?:.*)?(net[0-9][^\s\]]+)", RegexOptions.IgnoreCase);
        if (line.Contains("NU5128", StringComparison.OrdinalIgnoreCase) && framework.Success)
        {
            yield return $"warning {framework.Groups[1].Value}  no lib folder for {framework.Groups[2].Value}";
            yield break;
        }

        var created = Regex.Match(line, @"Successfully created package '([^']+)'", RegexOptions.IgnoreCase);
        if (created.Success)
        {
            yield return "Successfully created " + Path.GetFileName(created.Groups[1].Value);
            yield break;
        }

        var cut = line;
        var sdk = cut.IndexOf("warning ", StringComparison.OrdinalIgnoreCase);
        if (sdk > 0 && cut.Contains("NuGet.Build.Tasks", StringComparison.OrdinalIgnoreCase))
            cut = cut[sdk..];
        var tail = cut.LastIndexOf(" [", StringComparison.Ordinal);
        if (tail > 0 && cut.EndsWith(".csproj]", StringComparison.OrdinalIgnoreCase))
            cut = cut[..tail];
        yield return cut.Trim();
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
