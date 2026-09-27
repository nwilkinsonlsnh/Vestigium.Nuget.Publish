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
        Bump = ParseBump(_settings.Bump);
        PublishStore.ClearApiKey();
        if (Roots.Count > 0)
            _ = RefreshReposAsync();
    }

    public ObservableCollection<string> Roots { get; }

    public ObservableCollection<RepoRow> Repos { get; }

    public ObservableCollection<RepoRow> SelectedRepos { get; } = [];

    public ObservableCollection<PackableProject> Projects { get; }

    public string? ApiKey { get; set; }

    [ObservableProperty]
    private string? _selectedRoot;

    [ObservableProperty]
    private RepoRow? _selectedRepo;

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

    public bool IsBumpMajor
    {
        get => Bump == VersionBump.Major;
        set { if (value) Bump = VersionBump.Major; }
    }

    public bool IsBumpMinor
    {
        get => Bump == VersionBump.Minor;
        set { if (value) Bump = VersionBump.Minor; }
    }

    public bool IsBumpPatch
    {
        get => Bump == VersionBump.Patch;
        set { if (value) Bump = VersionBump.Patch; }
    }

    partial void OnBumpChanged(VersionBump value)
    {
        if (value == VersionBump.Keep)
        {
            Bump = VersionBump.Patch;
            return;
        }

        OnPropertyChanged(nameof(IsBumpMajor));
        OnPropertyChanged(nameof(IsBumpMinor));
        OnPropertyChanged(nameof(IsBumpPatch));
        Persist();
    }

    partial void OnSourceChanged(string value) => Persist();

    partial void OnOutputFolderChanged(string value) => Persist();

    partial void OnSelectedProjectChanged(PackableProject? value) => NotifyRun();

    partial void OnSelectedRepoChanged(RepoRow? value)
    {
        SynchronizeCommand.NotifyCanExecuteChanged();
        CopyHeadCommand.NotifyCanExecuteChanged();
    }

    partial void OnBusyChanged(bool value)
    {
        NotifyRun();
        SynchronizeCommand.NotifyCanExecuteChanged();
        CopyHeadCommand.NotifyCanExecuteChanged();
    }

    public void AddRoot(string path)
    {
        path = Path.GetFullPath(path.Trim());
        if (Roots.Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
            return;
        Roots.Add(path);
        Persist();
        Append($"root {path}");
        _ = RefreshReposAsync();
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

    public void ApplyRepoSelection(IEnumerable<RepoRow> rows)
    {
        SelectedRepos.Clear();
        foreach (var row in rows)
            SelectedRepos.Add(row);
        SelectedRepo = SelectedRepos.LastOrDefault();
        LoadProjects();
        SynchronizeCommand.NotifyCanExecuteChanged();
        CopyHeadCommand.NotifyCanExecuteChanged();
    }

    public void LoadProjects()
    {
        Projects.Clear();
        SelectedProject = null;
        var paths = SelectedRepos.Count > 0
            ? SelectedRepos.Select(r => r.Path)
            : [];
        foreach (var project in RepoScanner.ListProjects(paths))
            Projects.Add(project);
        if (Projects.Count > 0)
            SelectedProject = Projects[0];
        Status = $"{SelectedRepos.Count} selected  {Projects.Count} packable";
        NotifyRun();
    }

    [RelayCommand]
    private void CleanPacks()
    {
        var repos = SelectedRepos.Count > 0
            ? SelectedRepos.Select(r => r.Path)
            : Repos.Select(r => r.Path);
        DotnetCli.CleanPacks(repos, OutputFolder, Append);
        Status = "Cleaned packs";
    }

    [RelayCommand]
    private void ListPacks()
    {
        var repos = SelectedRepos.Count > 0
            ? SelectedRepos.Select(r => r.Path)
            : Repos.Select(r => r.Path);
        DotnetCli.ListPacks(repos, OutputFolder, Append);
        Status = "Listed packs";
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        await RefreshReposAsync();
        LoadProjects();
        Append($"{Repos.Count} repos on disk");
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SynchronizeAsync()
    {
        if (SelectedRepos.Count == 0)
            return;
        Busy = true;
        try
        {
            foreach (var repo in SelectedRepos.ToArray())
            {
                var code = await GitSync.SyncAsync(repo.Path, Append, Token());
                repo.Head = await GitSync.DescribeAsync(repo.Path, CancellationToken.None);
                if (code != 0)
                    Status = $"Sync failed  {repo.Name}";
            }

            if (SelectedRepos.Count == 1)
                Status = $"Synced  {SelectedRepos[0].Head}";
            else
                Status = $"Synced  {SelectedRepos.Count} repos";
            LoadProjects();
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    private void CopyHead()
    {
        var text = string.Join(Environment.NewLine, SelectedRepos.Select(r => $"{r.Name}  {r.Head}"));
        if (string.IsNullOrWhiteSpace(text))
            return;
        System.Windows.Clipboard.SetText(text);
        Append("copied HEAD");
    }

    private bool CanSync() => !Busy && SelectedRepos.Count > 0;

    private async Task RefreshReposAsync()
    {
        Repos.Clear();
        foreach (var path in RepoScanner.ListRepos(Roots))
        {
            var row = new RepoRow(path);
            Repos.Add(row);
            try
            {
                row.Head = await GitSync.DescribeAsync(path, CancellationToken.None);
            }
            catch (Exception ex)
            {
                row.Head = ex.Message;
            }
        }

        SynchronizeCommand.NotifyCanExecuteChanged();
        CopyHeadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task PackAsync() => RunAsync(push: false);

    [RelayCommand(CanExecute = nameof(CanPush))]
    private Task PushAsync() => RunAsync(push: true, pack: false);

    [RelayCommand(CanExecute = nameof(CanPush))]
    private Task PackAndPushAsync() => RunAsync(push: true, pack: true);

    [RelayCommand]
    private void CopyLog()
    {
        var text = Redact(Log);
        if (string.IsNullOrWhiteSpace(text))
            return;
        System.Windows.Clipboard.SetText(text);
        Status = "Log copied";
    }

    [RelayCommand]
    private void ClearLog()
    {
        Log = string.Empty;
        Status = "Log cleared";
    }

    private string Redact(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var redacted = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"--api-key\s+\S+",
            "--api-key ****************",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!string.IsNullOrWhiteSpace(ApiKey) && ApiKey.Length >= 8)
            redacted = redacted.Replace(ApiKey, "****************", StringComparison.Ordinal);

        return redacted;
    }

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
            else if (pack)
            {
                Append($"pack {project.ProjectName} {project.Version}  no bump");
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
        _settings.Bump = Bump.ToString();
        PublishStore.Save(_settings);
    }

    private static VersionBump ParseBump(string? raw)
    {
        return raw switch
        {
            nameof(VersionBump.Major) => VersionBump.Major,
            nameof(VersionBump.Minor) => VersionBump.Minor,
            _ => VersionBump.Patch
        };
    }

    private void Append(string line)
    {
        Log = string.IsNullOrWhiteSpace(Log) ? line : $"{Log}{Environment.NewLine}{line}";
    }
}
