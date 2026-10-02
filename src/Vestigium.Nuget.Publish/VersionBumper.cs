using System.IO;
using System.Text.RegularExpressions;

namespace Vestigium.Nuget.Publish;

public enum VersionBump
{
    Keep = 0,
    Patch = 1,
    Minor = 2,
    Major = 3
}

public static class VersionBumper
{
    private static readonly Regex VersionTag = new(
        @"<Version>\s*([^<]+)\s*</Version>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? BumpProject(string csprojPath, string? nuspecPath, string basis, VersionBump bump, out string previous)
    {
        previous = "";
        if (!System.Version.TryParse(Normalize(basis), out var version))
            return null;

        previous = version.ToString();
        if (bump == VersionBump.Keep)
            return previous;

        var next = bump switch
        {
            VersionBump.Major => new System.Version(version.Major + 1, 0, 0),
            VersionBump.Minor => new System.Version(version.Major, version.Minor + 1, 0),
            _ => new System.Version(version.Major, version.Minor, Math.Max(0, version.Build) + 1)
        };

        var written = next.ToString();
        Write(csprojPath, written);
        if (!string.IsNullOrWhiteSpace(nuspecPath))
            Write(nuspecPath, written);
        return written;
    }

    private static void Write(string path, string version)
    {
        if (!File.Exists(path))
            return;
        var xml = File.ReadAllText(path);
        if (!VersionTag.IsMatch(xml))
            return;
        var tag = path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) ? "version" : "Version";
        File.WriteAllText(path, VersionTag.Replace(xml, $"<{tag}>{version}</{tag}>", 1));
    }

    private static string Normalize(string raw)
    {
        raw = raw.Trim();
        var dash = raw.IndexOf('-');
        if (dash > 0)
            raw = raw[..dash];
        var parts = raw.Split('.');
        if (parts.Length == 2)
            return raw + ".0";
        return raw;
    }
}
