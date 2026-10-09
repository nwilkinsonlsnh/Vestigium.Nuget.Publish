using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vestigium.Nuget.Publish;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly PublishSettings _settings;
    private readonly Dictionary<string, string> _pushed = new(StringComparer.OrdinalIgnoreCase);
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
        if (_settings.Exclusions.Count == 0)
            _settings.Exclusions = ["Tests", "Test", "Sample", "Samples", "Demo", "Documentation"];
        Exclusions = new ObservableCollection<string>(_settings.Exclusions);
        PublishStore.ClearApiKey();
        if (Roots.Count > 0)
            _ = RefreshReposAsync();
    }

    public ObservableCollection<string> Roots { get; }

    public ObservableCollection<RepoRow> Repos { get; }

    public ObservableCollection<RepoRow> SelectedRepos { get; } = [];

    public ObservableCollection<PackableProject> Projects { get; }

    [ObservableProperty]
    private bool _showPackages = true;

    [ObservableProperty]
    private bool _showLog;

    [ObservableProperty]
    private bool _showSettings;

    [ObservableProperty]
    private string _exclusionDraft = "";

    public ObservableCollection<string> Exclusions { get; }

    [ObservableProperty]
    private string? _selectedExclusion;

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
        ClearCacheCommand.NotifyCanExecuteChanged();
    }

    partial void OnBusyChanged(bool value)
    {
        NotifyRun();
        SynchronizeCommand.NotifyCanExecuteChanged();
        CopyHeadCommand.NotifyCanExecuteChanged();
        ClearCacheCommand.NotifyCanExecuteChanged();
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
        ClearCacheCommand.NotifyCanExecuteChanged();
    }

    public void LoadProjects()
    {
        Projects.Clear();
        SelectedProject = null;
        var paths = SelectedRepos.Count > 0
            ? SelectedRepos.Select(r => r.Path)
            : [];
        foreach (var project in RepoScanner.ListProjects(paths, Exclusions))
            Projects.Add(project);
        if (Projects.Count > 0)
            SelectedProject = Projects[0];
        var hidden = Projects.Count(item => !item.IsPackable);
        Status = $"{SelectedRepos.Count} selected  {Projects.Count} in solution  {hidden} not packable";
        NotifyRun();
        _ = RefreshPublishedAsync();
    }

    private async Task RefreshPublishedAsync()
    {
        foreach (var project in Projects.ToArray())
        {
            try
            {
                var published = await NugetCatalog.LatestAsync(project.PackageId, Source, CancellationToken.None);
                project.PublishedVersion = string.IsNullOrWhiteSpace(published) ? "none" : published;
                project.LocalVersion = PackedVersion(project);
                project.IsListed = ListedState(project);
            }
            catch (Exception ex)
            {
                project.PublishedVersion = "none";
                project.LocalVersion = PackedVersion(project);
                project.IsListed = ListedState(project);
                Append($"{project.PackageId} nuget lookup failed  {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void CleanPacks()
    {
        var repos = SelectedRepos.Count > 0
            ? SelectedRepos.Select(r => r.Path)
            : Repos.Select(r => r.Path);
        DotnetCli.CleanPacks(repos, OutputFolder, Append);
        Status = "Cleaned packs";
        LoadProjects();
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
    private void AddExclusion()
    {
        var pattern = ExclusionDraft.Trim().TrimStart('.');
        if (pattern.Length == 0)
            return;
        if (Exclusions.Any(item => string.Equals(item, pattern, StringComparison.OrdinalIgnoreCase)))
            return;
        Exclusions.Add(pattern);
        ExclusionDraft = "";
        Persist();
        LoadProjects();
    }

    [RelayCommand]
    private void RemoveExclusion()
    {
        if (SelectedExclusion is null)
            return;
        Exclusions.Remove(SelectedExclusion);
        Persist();
        LoadProjects();
    }

    [RelayCommand]
    private void MakePackable()
    {
        if (SelectedProject is null)
        {
            Status = "Select a project.";
            return;
        }

        RepoScanner.MakePackable(SelectedProject.ProjectPath);
        Append("Marked packable " + SelectedProject.ProjectPath);
        LoadProjects();
    }

    [RelayCommand]
    private void MakeUnpackable()
    {
        if (SelectedProject is null)
        {
            Status = "Select a project.";
            return;
        }

        RepoScanner.MakeUnpackable(SelectedProject.ProjectPath);
        Append("Marked unpackable " + SelectedProject.ProjectPath);
        LoadProjects();
    }

    [RelayCommand]
    private void MakeTool()
    {
        if (SelectedProject is null)
        {
            Status = "Select a project.";
            return;
        }

        RepoScanner.MakeTool(SelectedProject.ProjectPath);
        Append("Marked tool " + SelectedProject.ProjectPath);
        LoadProjects();
    }

    [RelayCommand]
    private void RemoveTool()
    {
        if (SelectedProject is null)
        {
            Status = "Select a project.";
            return;
        }

        RepoScanner.RemoveTool(SelectedProject.ProjectPath);
        Append("Removed tool " + SelectedProject.ProjectPath);
        LoadProjects();
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
        ClearCacheCommand.NotifyCanExecuteChanged();
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

    private async Task<string?> BasisAsync(PackableProject project, CancellationToken token)
    {
        if (System.Version.TryParse(Normalize(project.PublishedVersion), out _))
            return project.PublishedVersion;

        var published = await NugetCatalog.LatestAsync(project.PackageId, Source, token);
        if (!string.IsNullOrWhiteSpace(published))
        {
            project.PublishedVersion = published;
            return published;
        }

        Append($"no nuget version for {project.PackageId} — publishing local {project.LocalVersion}");
        return null;
    }

    private string ListedState(PackableProject project)
    {
        var packed = project.LocalVersion;
        var hasPack = !string.IsNullOrWhiteSpace(packed) && packed != "—";
        var published = project.PublishedVersion;
        var onFeed = !string.IsNullOrWhiteSpace(published) && published != "none" && published != "…";
        if (!hasPack)
            return onFeed ? "Yes" : "No";
        return string.Equals(packed, published, StringComparison.OrdinalIgnoreCase) ? "Yes" : "Pending";
    }

    private string PackedVersion(PackableProject project)
    {
        var output = Path.IsPathRooted(OutputFolder)
            ? OutputFolder
            : Path.Combine(project.RepoPath, OutputFolder);
        if (!Directory.Exists(output))
            return "—";
        System.Version? best = null;
        var text = "";
        foreach (var file in Directory.EnumerateFiles(output, project.PackageId + ".*.nupkg"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var version = name[(project.PackageId.Length + 1)..];
            if (!System.Version.TryParse(Normalize(version), out var parsed))
                continue;
            if (best is null || parsed > best)
            {
                best = parsed;
                text = version;
            }
        }

        return string.IsNullOrWhiteSpace(text) ? "—" : text;
    }

    private string? Remembered(string packageId) =>
        _pushed.TryGetValue(packageId, out var version) ? version : null;

    private static string? Newer(string? left, string? right)
    {
        var leftOk = System.Version.TryParse(Normalize(left ?? ""), out var leftVersion);
        var rightOk = System.Version.TryParse(Normalize(right ?? ""), out var rightVersion);
        if (!leftOk && !rightOk)
            return null;
        if (!rightOk)
            return left;
        if (!leftOk)
            return right;
        return rightVersion > leftVersion ? right : left;
    }

    private static bool AtLeast(string? local, string? next)
    {
        var localOk = System.Version.TryParse(Normalize(local ?? ""), out var localVersion);
        var nextOk = System.Version.TryParse(Normalize(next ?? ""), out var nextVersion);
        return localOk && nextOk && localVersion >= nextVersion;
    }

    private static string? Next(string? basis, VersionBump bump)
    {
        if (!System.Version.TryParse(Normalize(basis ?? ""), out var version))
            return null;
        var next = bump switch
        {
            VersionBump.Major => new System.Version(version.Major + 1, 0, 0),
            VersionBump.Minor => new System.Version(version.Major, version.Minor + 1, 0),
            VersionBump.Keep => version,
            _ => new System.Version(version.Major, version.Minor, Math.Max(0, version.Build) + 1)
        };
        return next.ToString();
    }

    private async Task<string?> UnpublishedDependencyAsync(PackableProject project, CancellationToken token)
    {
        if (!File.Exists(project.ProjectPath))
            return null;

        var xml = await File.ReadAllTextAsync(project.ProjectPath, token);
        foreach (Match match in PackageReferenceTag.Matches(xml))
        {
            var id = match.Groups["id"].Value;
            var version = match.Groups["ver"].Value;
            if (!id.StartsWith("Vestigium.", StringComparison.OrdinalIgnoreCase))
                continue;
            var published = await NugetCatalog.LatestAsync(id, Source, token);
            if (!System.Version.TryParse(Normalize(version), out var wanted))
                continue;
            if (!System.Version.TryParse(Normalize(published ?? ""), out var have) || have < wanted)
                return $"{id} {version}";
        }

        return null;
    }

    private PackableProject? FindDependency(PackableProject project, string missing)
    {
        var id = missing.Split(' ', 2)[0];
        return Projects.FirstOrDefault(item =>
            item.RepoPath.Equals(project.RepoPath, StringComparison.OrdinalIgnoreCase)
            && item.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly Regex PackageReferenceTag = new(
        @"PackageReference\s+Include=""(?<id>[^""]+)""\s+Version=""(?<ver>[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private void MarkPushed(PackableProject project)
    {
        _pushed[project.PackageId] = project.LocalVersion;
        project.PublishedVersion = project.LocalVersion;
        Append($"nuget column {project.PackageId} {project.LocalVersion}");
        var index = Projects.IndexOf(project);
        if (index < 0)
            return;
        Projects.RemoveAt(index);
        Projects.Insert(index, project);
        SelectedProject = project;
    }

    private static string Normalize(string raw)
    {
        raw = raw.Trim();
        var dash = raw.IndexOf('-');
        if (dash > 0)
            raw = raw[..dash];
        return raw.Count(c => c == '.') == 1 ? raw + ".0" : raw;
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
                var basis = await BasisAsync(project, token);
                var floor = basis ?? project.ProjectVersion;
                if (basis is null)
                {
                    Append($"pack {project.PackageId} {project.ProjectVersion}  first publish");
                }
                else
                {
                    var next = VersionBumper.BumpProject(project.ProjectPath, project.NuspecPath, floor, Bump, out var previous);
                    if (next is null)
                    {
                        Append($"no version to bump for {project.PackageId}");
                        Status = "Failed";
                        return;
                    }

                    Append($"{project.PackageId} nuget {previous} → {next}");
                    var files = string.IsNullOrWhiteSpace(project.NuspecPath)
                        ? new[] { project.ProjectPath }
                        : new[] { project.ProjectPath, project.NuspecPath };
                    var commit = await GitSync.CommitAndPushAsync(
                        project.RepoPath,
                        files,
                        $"chore: bump {project.PackageId} to {next}",
                        Append,
                        token);
                    if (commit != 0)
                    {
                        Status = "Version commit failed";
                        return;
                    }

                    project.ProjectVersion = next;
                    project.LocalVersion = PackedVersion(project);
                    SelectedProject = project;
                }
            }
            else if (pack)
            {
                Append($"pack {project.PackageId} {project.ProjectVersion}  no bump");
            }

            if (pack)
            {
                var missing = await UnpublishedDependencyAsync(project, token);
                string? extraSource = null;
                if (missing is not null)
                {
                    var dependency = FindDependency(project, missing);
                    if (dependency is null)
                    {
                        Append($"pack blocked: {project.PackageId} needs {missing} on the feed. Publish that package first.");
                        Status = "Dependency not published";
                        return;
                    }

                    Append($"packing {dependency.PackageId} {dependency.LocalVersion} before {project.PackageId}");
                    var dependencyPacked = await DotnetCli.PackAsync(dependency, OutputFolder, Append, token);
                    if (dependencyPacked != 0)
                    {
                        Status = $"Pack failed  {dependency.PackageId}";
                        return;
                    }

                    if (push)
                    {
                        if (string.IsNullOrWhiteSpace(ApiKey))
                        {
                            Append("API key is empty — paste it for this session");
                            Status = "No key";
                            return;
                        }

                        var dependencyPushed = await DotnetCli.PushAsync(dependency, OutputFolder, Source, ApiKey, Append, token);
                        if (dependencyPushed != 0)
                        {
                            Status = $"Push failed  {dependency.PackageId}";
                            return;
                        }

                        MarkPushed(dependency);
                    }

                    extraSource = Path.IsPathRooted(OutputFolder)
                        ? OutputFolder
                        : Path.Combine(project.RepoPath, OutputFolder);
                }

                var packed = await DotnetCli.PackAsync(project, OutputFolder, Append, token, extraSource);
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

                MarkPushed(project);
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
            LoadProjects();
        }
    }

    private void Persist()
    {
        _settings.Roots = [.. Roots];
        _settings.Source = Source;
        _settings.OutputFolder = OutputFolder;
        _settings.Bump = Bump.ToString();
        _settings.Exclusions = [.. Exclusions];
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
