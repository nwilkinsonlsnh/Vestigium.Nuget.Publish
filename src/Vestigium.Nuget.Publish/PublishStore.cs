using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vestigium.Nuget.Publish;

public sealed class StoredKey
{
    public string Name { get; set; } = "";

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

    public string SelectedKeyName { get; set; } = "New key";
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

    public static IReadOnlyList<string> KeyNames(PublishSettings settings)
        => settings.Keys.Select(item => item.Name).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static void SaveApiKey(PublishSettings settings, string name, string key)
    {
        var trimmedName = name.Trim();
        var trimmed = key.Trim();
        if (trimmedName.Length == 0 || trimmedName.Equals("New key", StringComparison.OrdinalIgnoreCase) || trimmed.Length == 0)
            return;
        var secret = ProtectedData.Protect(Encoding.UTF8.GetBytes(trimmed), null, DataProtectionScope.CurrentUser);
        var cipher = Convert.ToBase64String(secret);
        var row = settings.Keys.FirstOrDefault(item => item.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            settings.Keys.Add(new StoredKey { Name = trimmedName, Cipher = cipher });
        else
            row.Cipher = cipher;
        settings.SelectedKeyName = trimmedName;
        Save(settings);
    }

    public static string? LoadApiKey(PublishSettings settings, string name)
    {
        var row = settings.Keys.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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

    public static void ForgetApiKey(PublishSettings settings, string name)
    {
        settings.Keys.RemoveAll(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        settings.SelectedKeyName = "New key";
        Save(settings);
    }
}
