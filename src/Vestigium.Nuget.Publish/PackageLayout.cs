using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Vestigium.Nuget.Publish;

public static class PackageLayout
{
    private static readonly Regex PackedNone = new(
        """<None\s+Include="([^"]+)"[^>]*Pack="true"[^>]*PackagePath="([^"]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PackedNoneAlt = new(
        """<None\s+Include="([^"]+)"[^>]*PackagePath="([^"]*)"[^>]*Pack="true""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool Prepare(PackableProject project, Action<string> log)
    {
        var props = ReadProps(project.ProjectPath).ToArray();
        if (props.Any(row => PacksReadme(row.Text)))
            StripProjectReadme(project, log);

        var collisions = Collisions(project);
        if (collisions.Count == 0)
            return true;

        foreach (var (path, sources) in collisions)
            log($"duplicate package path {path}  {string.Join(" | ", sources)}");
        log("pack stopped. Two files would land in the same package folder.");
        return false;
    }

    public static bool Inspect(PackableProject project, string outputFolder, Action<string> log)
    {
        var output = Path.IsPathRooted(outputFolder)
            ? outputFolder
            : Path.Combine(project.RepoPath, outputFolder);
        var nupkg = Find(output, project);
        if (nupkg is null)
        {
            log($"no nupkg to inspect for {project.PackageId}");
            return false;
        }

        using var zip = ZipFile.OpenRead(nupkg);
        var groups = zip.Entries
            .GroupBy(entry => entry.FullName.Replace('\', '/'), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1 && !group.Key.EndsWith('/'))
            .ToArray();
        if (groups.Length == 0)
            return true;

        foreach (var group in groups)
            log($"duplicate in {Path.GetFileName(nupkg)}: {group.Key} ({group.Count()})");
        log("NuGet rejects a package with two files at the same path. Push stopped.");
        return false;
    }

    private static void StripProjectReadme(PackableProject project, Action<string> log)
    {
        var dir = Path.GetDirectoryName(project.ProjectPath)!;
        var local = Path.Combine(dir, "README.md");
        if (File.Exists(local) && IsStub(File.ReadAllText(local)))
        {
            File.Delete(local);
            log($"removed stub {local}");
        }

        var xml = File.ReadAllText(project.ProjectPath);
        var next = xml;
        next = Regex.Replace(next, """\s*<PackageReadmeFile>\s*README\.md\s*</PackageReadmeFile>""", "", RegexOptions.IgnoreCase);
        next = Regex.Replace(next, """\s*<None\s+Include="README\.md"[^>]*Pack="true"[^>]*/>""", "", RegexOptions.IgnoreCase);
        if (next == xml)
            return;
        File.WriteAllText(project.ProjectPath, next);
        log("readme already supplied by Directory.Build.props. Removed the project pack entry.");
    }

    private static List<(string Path, string[] Sources)> Collisions(PackableProject project)
    {
        var entries = new List<(string Path, string Source)>();
        entries.AddRange(Packed(project.ProjectPath, File.ReadAllText(project.ProjectPath)));
        foreach (var props in ReadProps(project.ProjectPath))
            entries.AddRange(Packed(props.Path, props.Text));

        return entries
            .GroupBy(entry => Normalize(entry.Path), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => (group.Key, group.Select(item => item.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToList();
    }

    private static IEnumerable<(string Path, string Source)> Packed(string file, string xml)
    {
        var name = Path.GetFileName(file);
        foreach (Match match in PackedNone.Matches(xml).Concat(PackedNoneAlt.Matches(xml)))
        {
            var include = match.Groups[1].Value.Trim();
            var path = match.Groups[2].Value.Trim();
            if (path.Length == 0 || path == "\\" || path == "/")
                path = Path.GetFileName(include.Replace('/', Path.DirectorySeparatorChar));
            yield return (path, $"{name}:{include}");
        }

        if (Regex.IsMatch(xml, """<PackageReadmeFile>\s*README\.md\s*</PackageReadmeFile>""", RegexOptions.IgnoreCase))
            yield return ("README.md", name + ":PackageReadmeFile");
    }

    private static bool PacksReadme(string xml) =>
        Regex.IsMatch(xml, """<PackageReadmeFile>\s*README\.md\s*</PackageReadmeFile>""", RegexOptions.IgnoreCase)
        || Regex.IsMatch(xml, """Include="[^"]*README\.md"[^>]*Pack="true"""", RegexOptions.IgnoreCase);

    private static bool IsStub(string text) =>
        text.Contains("Packaged by Vestigium.Nuget.Publish", StringComparison.Ordinal);

    private static IEnumerable<(string Path, string Text)> ReadProps(string csproj)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(csproj)!);
        while (dir is not null)
        {
            var props = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(props))
                yield return (props, File.ReadAllText(props));
            dir = dir.Parent;
        }
    }

    private static string Normalize(string path) =>
        path.Replace('\', '/').TrimStart('/').Trim();

    private static string? Find(string output, PackableProject project)
    {
        if (!Directory.Exists(output))
            return null;
        var exact = Path.Combine(output, $"{project.PackageId}.{project.ProjectVersion}.nupkg");
        if (File.Exists(exact))
            return exact;
        return Directory.EnumerateFiles(output, $"{project.PackageId}.*.nupkg")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
