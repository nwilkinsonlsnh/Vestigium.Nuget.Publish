using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vestigium.Nuget.Publish;

public sealed class StoredKey
{
    public string Source { get; set; } = "";

    public string Cipher { get; set; } = "";
}

public sealed class PublishSettings
{
    public List<string> Roots { get; set; } = [];

    public string Source { get; set; } = "https://api.nuget.org/v3/index.json";

    public string OutputFolder { get; set; } = "artifacts/nupkg";

    public string Bump { get; set; } = "Patch";

    public List<string> Exclusions { get; set; } = ["Tests", "Test", "Sample", "Samples", "Demo", "Documentation"];

    public List<StoredKey> Keys { get; set; } = [];
}

public static class PublishStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vestigium", "NugetPublish");

    public static string SettingsPath => Path.Combine(Folder, "settings.json");

    public static PublishSettings Load()
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(SettingsPath))
            return new PublishSettings();
        try
        {
            return JsonSerializer.Deserialize<PublishSettings>(File.ReadAllText(SettingsPath)) ?? new PublishSettings();
        }
        catch (JsonException)
        {
            return new PublishSettings();
        }
    }

    public static void Save(PublishSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Json));
    }

    public static void SaveApiKey(PublishSettings settings, string source, string key)
    {
        var trimmed = key.Trim();
        if (trimmed.Length == 0 || string.IsNullOrWhiteSpace(source))
            return;
        var secret = ProtectedData.Protect(Encoding.UTF8.GetBytes(trimmed), null, DataProtectionScope.CurrentUser);
        var cipher = Convert.ToBase64String(secret);
        var row = settings.Keys.FirstOrDefault(item => item.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            settings.Keys.Add(new StoredKey { Source = source, Cipher = cipher });
        else
            row.Cipher = cipher;
        Save(settings);
    }

    public static string? LoadApiKey(PublishSettings settings, string source)
    {
        var row = settings.Keys.FirstOrDefault(item => item.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
        if (row is null || string.IsNullOrWhiteSpace(row.Cipher))
            return null;
        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(row.Cipher), null, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void ForgetApiKey(PublishSettings settings, string source)
    {
        settings.Keys.RemoveAll(item => item.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
        Save(settings);
    }
}
