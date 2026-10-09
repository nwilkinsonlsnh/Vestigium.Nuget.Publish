using System.Net.Http;
using System.Text.Json;

namespace Vestigium.Nuget.Publish;

public static class NugetCatalog
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public static async Task<string?> LatestAsync(string packageId, string source, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return null;

        var id = packageId.Trim().ToLowerInvariant();
        var url = source.Contains("nuget.org", StringComparison.OrdinalIgnoreCase)
            ? $"https://api.nuget.org/v3-flatcontainer/{id}/index.json"
            : $"{source.TrimEnd('/')}/{id}/index.json";

        using var response = await Http.GetAsync(url, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!doc.RootElement.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            return null;

        System.Version? best = null;
        var text = "";
        foreach (var item in versions.EnumerateArray())
        {
            var raw = item.GetString();
            if (string.IsNullOrWhiteSpace(raw) || !System.Version.TryParse(Normalize(raw), out var parsed))
                continue;
            if (best is null || parsed > best)
            {
                best = parsed;
                text = raw;
            }
        }

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static async Task<string> ListedAsync(string packageId, string source, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return "No";
        var id = packageId.Trim().ToLowerInvariant();
        var url = source.Contains("nuget.org", StringComparison.OrdinalIgnoreCase)
            ? $"https://api.nuget.org/v3/registration5-gz-semver2/{id}/index.json"
            : $"{source.TrimEnd('/')}/{id}/index.json";
        try
        {
            using var response = await Http.GetAsync(url, token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return "No";
            if (!response.IsSuccessStatusCode)
                return "Pending";
            var body = await response.Content.ReadAsStringAsync(token);
            if (body.Contains("\"listed\":false", StringComparison.OrdinalIgnoreCase)
                && !body.Contains("\"listed\":true", StringComparison.OrdinalIgnoreCase))
                return "No";
            return body.Contains("\"listed\":true", StringComparison.OrdinalIgnoreCase) ? "Yes" : "Pending";
        }
        catch (Exception)
        {
            return "Pending";
        }
    }

    private static string Normalize(string raw)
    {
        raw = raw.Trim();
        var dash = raw.IndexOf('-');
        if (dash > 0)
            raw = raw[..dash];
        return raw.Count(c => c == '.') == 1 ? raw + ".0" : raw;
    }
}
