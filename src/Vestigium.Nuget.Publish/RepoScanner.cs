using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vestigium.Nuget.Publish;

public sealed partial class PackableProject : ObservableObject
{
    public required string RepoName { get; init; }

    public required string RepoPath { get; init; }

    public required string ProjectName { get; init; }

    public required string PackageId { get; init; }

    public required string ProjectPath { get; init; }

    public string? NuspecPath { get; init; }

    public required string ProjectVersion { get; set; }

    private string _localVersion = "—";

    public string LocalVersion
    {
        get => _localVersion;
        set
        {
            if (_localVersion == value)
                return;
            _localVersion = value;
            OnPropertyChanged(nameof(LocalVersion));
            OnPropertyChanged(nameof(IsListed));
        }
    }

    public string IsListed
    {
        get
        {
            var hasPack = System.Version.TryParse(VersionText(LocalVersion), out var packed);
            var onFeed = System.Version.TryParse(VersionText(PublishedVersion), out var published);
            if (!hasPack)
                return onFeed ? "Yes" : "No";
            return onFeed && packed == published ? "Yes" : "Pending";
        }
    }

    private static string VersionText(string? raw)
    {
        raw = raw?.Trim() ?? "";
        var dash = raw.IndexOf('-');
        if (dash > 0)
            raw = raw[..dash];
        return raw.Count(c => c == '.') == 1 ? raw + ".0" : raw;
    }

    public bool IsPackable { get; init; }

    public bool IsTool { get; init; }

    public string IsPackableText => IsPackable ? "Yes" : "No";

    public string IsToolText => IsTool ? "Yes" : "No";

    [ObservableProperty]
    private string _publishedVersion = "…";

    partial void OnPublishedVersionChanged(string value) => OnPropertyChanged(nameof(IsListed));
}

public static class RepoScanner
{
    private static readonly Regex VersionTag = new(
        @"<Version>\s*([^<]+)\s*</Version>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PackageIdTag = new(
        @"<id>\s*([^<]+)\s*</id>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NuspecFileTag = new(
        @"<NuspecFile>\s*([^<]+)\s*</NuspecFile>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PackableFalse = new(
        @"<IsPackable>\s*false\s*</IsPackable>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PackableTrue = new(
        @"<IsPackable>\s*true\s*</IsPackable>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> ListRepos(IEnumerable<string> roots)
    {
        var repos = new List<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            if (LooksLikeRepo(root))
                repos.Add(Path.GetFullPath(root));

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (LooksLikeRepo(dir))
                    repos.Add(Path.GetFullPath(dir));
            }
        }

        return repos
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<PackableProject> ListProjects(IEnumerable<string> repos, IEnumerable<string>? exclusions = null)
    {
        var rows = new List<PackableProject>();
        foreach (var repo in repos)
        {
            if (!Directory.Exists(repo))
                continue;

            var name = Path.GetFileName(repo.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            foreach (var csproj in SolutionProjects(repo))
            {
                if (csproj.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || csproj.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var xml = File.ReadAllText(csproj);
                var projectName = Path.GetFileNameWithoutExtension(csproj);
                if (Excluded(projectName, exclusions))
                    continue;
                var packable = !PackableFalse.IsMatch(xml) && (xml.Contains("<IsPackable>", StringComparison.OrdinalIgnoreCase) || VersionTag.IsMatch(xml) || xml.Contains("PackageId", StringComparison.OrdinalIgnoreCase) || NuspecFileTag.IsMatch(xml) || InheritedPackable(csproj));
                var tool = xml.Contains("<PackAsTool>true</PackAsTool>", StringComparison.OrdinalIgnoreCase)
                    || xml.Contains("<BuildOutputTargetFolder>tools</BuildOutputTargetFolder>", StringComparison.OrdinalIgnoreCase)
                    || xml.Contains("PackagePath=\"build", StringComparison.OrdinalIgnoreCase);
                var nuspec = ReadNuspec(csproj, xml);
                var version = nuspec.Version
                    ?? (VersionTag.Match(xml).Success ? VersionTag.Match(xml).Groups[1].Value.Trim() : null)
                    ?? ReadDirectoryVersion(csproj)
                    ?? "—";

                rows.Add(new PackableProject
                {
                    RepoName = name,
                    RepoPath = repo,
                    ProjectName = projectName,
                    PackageId = nuspec.Id ?? projectName,
                    ProjectPath = csproj,
                    NuspecPath = nuspec.Path,
                    ProjectVersion = version,
                    IsPackable = packable,
                    IsTool = tool
                });
            }
        }

        return rows
            .OrderBy(r => r.RepoName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool Excluded(string name, IEnumerable<string>? exclusions)
    {
        var last = name.LastIndexOf('.');
        var segment = last < 0 ? name : name[(last + 1)..];
        foreach (var raw in exclusions ?? [])
        {
            var pattern = raw.Trim().TrimStart('.');
            if (pattern.Length == 0)
                continue;
            if (Glob(segment, pattern))
                return true;
        }

        return false;
    }

    private static bool Glob(string segment, string pattern)
    {
        var body = string.Concat(pattern.Select(ch => ch switch
        {
            '*' => ".*",
            '?' => ".",
            _ => Regex.Escape(ch.ToString())
        }));
        var anchored = pattern.Contains('*') || !pattern.Contains('?');
        var regex = anchored ? "^" + body + "$" : body;
        return Regex.IsMatch(segment, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> SolutionProjects(string repo)
    {
        var listed = new List<string>();
        foreach (var slnx in Directory.EnumerateFiles(repo, "*.slnx"))
        {
            foreach (var line in File.ReadAllLines(slnx))
            {
                var mark = "Project Path=\"";
                var at = line.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    continue;
                var start = at + mark.Length;
                var end = line.IndexOf('"', start);
                if (end < 0)
                    continue;
                var path = Path.GetFullPath(Path.Combine(repo, line[start..end].Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(path))
                    listed.Add(path);
            }
        }

        if (listed.Count > 0)
            return listed.Distinct(StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(repo, "*.csproj", SearchOption.AllDirectories);
    }

    public static void MakePackable(string csproj)
    {
        var xml = File.ReadAllText(csproj);
        xml = PackableFalse.Replace(xml, "");
        if (!xml.Contains("<IsPackable>true</IsPackable>", StringComparison.OrdinalIgnoreCase))
            xml = InsertBeforeGroup(xml, "    <IsPackable>true</IsPackable>\n  ");
        if (!VersionTag.IsMatch(xml))
            xml = InsertBeforeGroup(xml, "    <Version>1.0.0</Version>\n  ");
        File.WriteAllText(csproj, xml);
    }

    public static void MakeUnpackable(string csproj)
    {
        var xml = File.ReadAllText(csproj);
        xml = PackableTrue.Replace(xml, "<IsPackable>false</IsPackable>");
        xml = xml.Replace("<PackAsTool>true</PackAsTool>", "", StringComparison.OrdinalIgnoreCase);
        if (!xml.Contains("<IsPackable>", StringComparison.OrdinalIgnoreCase))
            xml = InsertBeforeGroup(xml, "    <IsPackable>false</IsPackable>\n  ");
        File.WriteAllText(csproj, xml);
    }

    public static void RemoveTool(string csproj)
    {
        var xml = File.ReadAllText(csproj);
        xml = xml.Replace("<PackAsTool>true</PackAsTool>", "", StringComparison.OrdinalIgnoreCase);
        xml = xml.Replace("<BuildOutputTargetFolder>tools</BuildOutputTargetFolder>", "", StringComparison.OrdinalIgnoreCase);
        var name = Path.GetFileNameWithoutExtension(csproj);
        var marker = name + ".targets";
        var kept = xml.Split('\n').Where(row => !row.Contains(marker, StringComparison.OrdinalIgnoreCase) || !row.Contains("Pack=\"true\"", StringComparison.OrdinalIgnoreCase));
        xml = string.Join("\n", kept);
        File.WriteAllText(csproj, xml);
    }

    public static void MakeTool(string csproj)
    {
        MakePackable(csproj);
        var xml = File.ReadAllText(csproj);
        if (!xml.Contains("<BuildOutputTargetFolder>tools</BuildOutputTargetFolder>", StringComparison.OrdinalIgnoreCase))
            xml = InsertBeforeGroup(xml, "    <BuildOutputTargetFolder>tools</BuildOutputTargetFolder>\n  ");
        var name = Path.GetFileNameWithoutExtension(csproj);
        var dir = Path.GetDirectoryName(csproj)!;
        Directory.CreateDirectory(Path.Combine(dir, "build"));
        var pkg = name.Replace('.', '_');
        File.WriteAllText(Path.Combine(dir, "build", name + ".targets"), "<Project>\n  <Target Name=\"CopyVestigiumTool\" AfterTargets=\"Build\">\n    <ItemGroup>\n      <_ToolFiles Include=\"$(Pkg" + pkg + ")\\tools\\**\\*.*\" />\n    </ItemGroup>\n    <Copy SourceFiles=\"@(_ToolFiles)\" DestinationFolder=\"$(OutputPath)\" SkipUnchangedFiles=\"true\" Condition=\"'@(_ToolFiles)' != ''\" />\n  </Target>\n</Project>\n");
        if (!xml.Contains(name + ".targets", StringComparison.OrdinalIgnoreCase))
            xml += "\n  <ItemGroup>\n    <None Include=\"build\\" + name + ".targets\" Pack=\"true\" PackagePath=\"build\\" + name + ".targets\" />\n    <None Include=\"build\\" + name + ".targets\" Pack=\"true\" PackagePath=\"buildTransitive\\" + name + ".targets\" />\n  </ItemGroup>\n";
        var packExe = "\n  <Target Name=\"PackVestigiumExe\" AfterTargets=\"_GetPackageFiles\">\n    <Error Text=\"Tool exe was not built at $(TargetDir)$(AssemblyName).exe\" Condition=\"!Exists('$(TargetDir)$(AssemblyName).exe')\" />\n    <ItemGroup>\n      <_PackageFiles Include=\"$(TargetDir)$(AssemblyName).exe\">\n        <PackagePath>tools/$(TargetFramework)/$(AssemblyName).exe</PackagePath>\n      </_PackageFiles>\n      <_PackageFiles Include=\"$(TargetDir)*.dll\">\n        <PackagePath>tools/$(TargetFramework)/%(Filename)%(Extension)</PackagePath>\n      </_PackageFiles>\n      <_PackageFiles Include=\"$(TargetDir)$(AssemblyName).runtimeconfig.json\">\n        <PackagePath>tools/$(TargetFramework)/$(AssemblyName).runtimeconfig.json</PackagePath>\n      </_PackageFiles>\n      <_PackageFiles Include=\"$(TargetDir)$(AssemblyName).deps.json\">\n        <PackagePath>tools/$(TargetFramework)/$(AssemblyName).deps.json</PackagePath>\n      </_PackageFiles>\n    </ItemGroup>\n    <Message Importance=\"high\" Text=\"Packing $(TargetDir)$(AssemblyName).exe into tools/$(TargetFramework)\" />\n  </Target>\n";
        if (!xml.Contains("PackVestigiumExe", StringComparison.Ordinal))
            xml += packExe;
        File.WriteAllText(csproj, xml);
    }

    private static string InsertBeforeGroup(string xml, string insertion)
    {
        var at = xml.IndexOf("</PropertyGroup>", StringComparison.Ordinal);
        return at < 0 ? xml : xml[..at] + insertion + xml[at..];
    }

    private static bool InheritedPackable(string csproj)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(csproj)!);
        while (dir is not null)
        {
            var props = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(props) && PackableTrue.IsMatch(File.ReadAllText(props)))
                return true;
            dir = dir.Parent;
        }

        return false;
    }

    private static (string? Id, string? Version, string? Path) ReadNuspec(string csproj, string xml)
    {
        var match = NuspecFileTag.Match(xml);
        if (!match.Success)
            return (null, null, null);

        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(csproj)!, match.Groups[1].Value.Trim()));
        if (!File.Exists(path))
            return (null, null, path);

        var nuspec = File.ReadAllText(path);
        var id = PackageIdTag.Match(nuspec);
        var version = VersionTag.Match(nuspec);
        return (
            id.Success ? id.Groups[1].Value.Trim() : null,
            version.Success ? version.Groups[1].Value.Trim() : null,
            path);
    }

    private static bool LooksLikeRepo(string path)
    {
        return Directory.Exists(Path.Combine(path, ".git"))
            || Directory.EnumerateFiles(path, "*.sln").Any()
            || Directory.EnumerateFiles(path, "*.slnx").Any();
    }

    private static string? ReadDirectoryVersion(string csproj)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(csproj)!);
        while (dir is not null)
        {
            var props = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(props))
            {
                var match = VersionTag.Match(File.ReadAllText(props));
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }

            dir = dir.Parent;
        }

        return null;
    }
}
