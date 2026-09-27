using System.Collections.ObjectModel;
using System.IO;
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
        PublishStore.ClearApiKey();
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

    public string? ApiKey { get; set; }

    [ObservableProperty]
    private string? _selectedRoot;

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
    private string _status = "Idle";

    [ObservableProperty]
    private VersionBump _bump = VersionBump.Patch;

    public bool IsBumpKeep
    {
        get => Bump == VersionBump.Keep;
        set { if (value) Bump = VersionBump.Keep; }
    }

    public bool IsBumpPatch
    {
        get => Bump == VersionBump.Patch;
        set { if (value) Bump = VersionBump.Patch; }
    }

    public bool IsBumpMinor
    {
        get => Bump == VersionBump.Minor;
        set { if (value) Bump = VersionBump.Minor; }
    }

    public bool IsBumpMajor
    {
        get => Bump == VersionBump.Major;
        set { if (value) Bump = VersionBump.Major; }
    }

    partial void OnBumpChanged(VersionBump value)
    {
        OnPropertyChanged(nameof(IsBumpKeep));
        OnPropertyChanged(nameof(IsBumpPatch));
        OnPropertyChanged(nameof(IsBumpMinor));
        OnPropertyChanged(nameof(IsBumpMajor));
    }

    partial void OnSourceChanged(string value) => Persist();

    partial void OnOutputFolderChanged(string value) => Persist();

    partial void OnSelectedProjectChanged(PackableProject? value) => NotifyRun();

    partial void OnBusyChanged(bool value) => NotifyRun();

    public void AddRoot(string path)
    {
        path = Path.GetFullPath(path.Trim());
        if (Roots.Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
            return;
        Roots.Add(path);
        Persist();
        Append($"root {path}");
    }

    public void ForgetKey() => ApiKey = null;

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
        SelectedProject = null;
        foreach (var repo in RepoScanner.ListRepos(Roots))
            Repos.Add(repo);
        foreach (var project in RepoScanner.ListProjects(Repos))
            Projects.Add(project);
        if (Projects.Count > 0)
            SelectedProject = Projects[0];
        Status = $"{Repos.Count} repos  {Projects.Count} packable";
        Append(Status);
        NotifyRun();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task PackAsync() => RunAsync(push: false);

    [RelayCommand(CanExecute = nameof(CanPush))]
    private Task PushAsync() => RunAsync(push: true, pack: false);

    [RelayCommand(CanExecute = nameof(CanPush))]
    private Task PackAndPushAsync() => RunAsync(push: true, pack: true);

    [RelayCommand]
    private void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private bool CanRun() => !Busy && SelectedProject is not null;

    private bool CanPush() => CanRun() && !string.IsNullOrWhiteSpace(ApiKey);

    private void NotifyRun()
    {
        PackCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
        PackAndPushCommand.NotifyCanExecuteChanged();
    }

    public void NotifyKey() => NotifyRun();

    private CancellationToken Token()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    private async Task RunAsync(bool push, bool pack = true)
    {
        if (SelectedProject is null)
            return;

        Busy = true;
        Status = "Running";
        var token = Token();
        var project = SelectedProject;
        try
        {
            var git = await GitSync.EnsureCurrentAsync(project.RepoPath, Append, token);
            if (git != 0)
            {
                Status = "Git blocked pack";
                return;
            }

            if (pack && Bump != VersionBump.Keep)
            {
                var next = VersionBumper.BumpProject(project.ProjectPath, Bump, out var previous);
                if (next is null)
                {
                    Append($"no <Version> in {project.ProjectName}");
                    Status = "Failed";
                    return;
                }

                Append($"{project.ProjectName} {previous} → {next}");
                var commit = await GitSync.CommitAndPushAsync(
                    project.RepoPath,
                    project.ProjectPath,
                    $"chore: bump {project.ProjectName} to {next}",
                    Append,
                    token);
                if (commit != 0)
                {
                    Status = "Version commit failed";
                    return;
                }

                project.Version = next;
                SelectedProject = project;
            }

            if (pack)
            {
                var packed = await DotnetCli.PackAsync(project, OutputFolder, Append, token);
                if (packed != 0)
                {
                    Status = $"Pack failed  {packed}";
                    return;
                }
            }

            if (push)
            {
                if (string.IsNullOrWhiteSpace(ApiKey))
                {
                    Append("API key is empty — paste it for this session");
                    Status = "No key";
                    return;
                }

                var pushed = await DotnetCli.PushAsync(project, OutputFolder, Source, ApiKey, Append, token);
                if (pushed != 0)
                {
                    Status = $"Push failed  {pushed}";
                    return;
                }
            }

            Status = "Done";
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
