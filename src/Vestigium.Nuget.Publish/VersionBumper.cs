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

    public static string? BumpProject(string csprojPath, VersionBump bump, out string previous)
    {
        previous = "";
        var xml = File.ReadAllText(csprojPath);
        var match = VersionTag.Match(xml);
        if (!match.Success || !System.Version.TryParse(Normalize(match.Groups[1].Value), out var version))
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
        File.WriteAllText(csprojPath, VersionTag.Replace(xml, $"<Version>{written}</Version>", 1));
        return written;
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
