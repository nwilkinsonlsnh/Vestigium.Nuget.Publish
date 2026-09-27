using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vestigium.Nuget.Publish;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly PublishSettings _settings;
    private CancellationTokenSource? _cts;

    public MainViewModel()
    {
        _settings = PublishStore.Load();
        Roots = new ObservableCollection<string>(_settings.Roots);
        Repos = [];
        Projects = [];
        Source = _settings.Source;
        OutputFolder = _settings.OutputFolder;
        HasKey = !string.IsNullOrWhiteSpace(PublishStore.LoadApiKey());
        if (Roots.Count == 0)
        {
            var clone = @"D:\Source\Clone";
            if (Directory.Exists(clone))
                Roots.Add(clone);
        }
    }

    public ObservableCollection<string> Roots { get; }

    public ObservableCollection<string> Repos { get; }

    public ObservableCollection<PackableProject> Projects { get; }

    [ObservableProperty]
    private string? _selectedRoot;

    [ObservableProperty]
    private string? _selectedRepo;

    [ObservableProperty]
    private PackableProject? _selectedProject;

    [ObservableProperty]
    private string _source = "https://api.nuget.org/v3/index.json";

    [ObservableProperty]
    private string _outputFolder = "artifacts/nupkg";

    [ObservableProperty]
    private string _log = string.Empty;

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    private bool _hasKey;

    [ObservableProperty]
    private string _status = "Idle";

    partial void OnSourceChanged(string value) => Persist();

    partial void OnOutputFolderChanged(string value) => Persist();

    public void AddRoot(string path)
    {
        path = Path.GetFullPath(path.Trim());
        if (Roots.Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
            return;
        Roots.Add(path);
        Persist();
        Append($"root {path}");
    }

    [RelayCommand]
    private void RemoveRoot()
    {
        if (SelectedRoot is null)
            return;
        Roots.Remove(SelectedRoot);
        Persist();
    }

    [RelayCommand]
    private void Scan()
    {
        Repos.Clear();
        Projects.Clear();
        foreach (var repo in RepoScanner.ListRepos(Roots))
            Repos.Add(repo);
        foreach (var project in RepoScanner.ListProjects(Repos))
            Projects.Add(project);
        Status = $"{Repos.Count} repos  {Projects.Count} packable";
        Append(Status);
    }

    public void SaveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Append("API key blank — not saved");
            return;
        }

        PublishStore.SaveApiKey(key);
        HasKey = true;
        Status = "API key saved on this machine";
        Append(Status);
    }

    [RelayCommand]
    private void ClearKey()
    {
        PublishStore.ClearApiKey();
        HasKey = false;
        Append("API key cleared");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PackAsync()
    {
        if (SelectedProject is null)
            return;
        await RunAsync(() => DotnetCli.PackAsync(SelectedProject, OutputFolder, Append, Token()));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PushAsync()
    {
        if (SelectedProject is null)
            return;
        var key = PublishStore.LoadApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            Append("Save an API key first");
            return;
        }

        await RunAsync(() => DotnetCli.PushAsync(SelectedProject, OutputFolder, Source, key, Append, Token()));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PackAndPushAsync()
    {
        if (SelectedProject is null)
            return;
        var key = PublishStore.LoadApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            Append("Save an API key first");
            return;
        }

        await RunAsync(async () =>
        {
            var pack = await DotnetCli.PackAsync(SelectedProject, OutputFolder, Append, Token());
            if (pack != 0)
                return pack;
            return await DotnetCli.PushAsync(SelectedProject, OutputFolder, Source, key, Append, Token());
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private bool CanRun() => !Busy && SelectedProject is not null;

    private CancellationToken Token()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    private async Task RunAsync(Func<Task<int>> work)
    {
        Busy = true;
        PackCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
        PackAndPushCommand.NotifyCanExecuteChanged();
        Status = "Running";
        try
        {
            var code = await work();
            Status = code == 0 ? "Done" : $"Failed  {code}";
            Append(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
            Append(Status);
        }
        catch (Exception ex)
        {
            Status = "Failed";
            Append(ex.Message);
        }
        finally
        {
            Busy = false;
            PackCommand.NotifyCanExecuteChanged();
            PushCommand.NotifyCanExecuteChanged();
            PackAndPushCommand.NotifyCanExecuteChanged();
        }
    }

    private void Persist()
    {
        _settings.Roots = [.. Roots];
        _settings.Source = Source;
        _settings.OutputFolder = OutputFolder;
        PublishStore.Save(_settings);
    }

    private void Append(string line)
    {
        Log = string.IsNullOrWhiteSpace(Log) ? line : $"{Log}{Environment.NewLine}{line}";
    }
}
