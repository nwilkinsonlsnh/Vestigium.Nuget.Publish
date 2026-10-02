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

    public required string LocalVersion { get; set; }

    [ObservableProperty]
    private string _publishedVersion = "…";
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

    public static IReadOnlyList<PackableProject> ListProjects(IEnumerable<string> repos)
    {
        var rows = new List<PackableProject>();
        foreach (var repo in repos)
        {
            if (!Directory.Exists(repo))
                continue;

            var name = Path.GetFileName(repo.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            foreach (var csproj in Directory.EnumerateFiles(repo, "*.csproj", SearchOption.AllDirectories))
            {
                if (csproj.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || csproj.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || csproj.Contains($"{Path.DirectorySeparatorChar}samples{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var xml = File.ReadAllText(csproj);
                if (PackableFalse.IsMatch(xml))
                    continue;

                var projectName = Path.GetFileNameWithoutExtension(csproj);
                if (projectName.Contains("Tests", StringComparison.OrdinalIgnoreCase)
                    || projectName.Contains("InventoryLab", StringComparison.OrdinalIgnoreCase)
                    || projectName.Contains("MenuLab", StringComparison.OrdinalIgnoreCase)
                    || projectName.Contains("ThemeLab", StringComparison.OrdinalIgnoreCase)
                    || projectName.Contains("Documentation", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var marked = xml.Contains("<IsPackable>", StringComparison.OrdinalIgnoreCase)
                    || VersionTag.IsMatch(xml)
                    || xml.Contains("PackageId", StringComparison.OrdinalIgnoreCase)
                    || NuspecFileTag.IsMatch(xml);
                if (!marked && !InheritedPackable(csproj))
                    continue;

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
                    LocalVersion = version
                });
            }
        }

        return rows
            .OrderBy(r => r.RepoName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
