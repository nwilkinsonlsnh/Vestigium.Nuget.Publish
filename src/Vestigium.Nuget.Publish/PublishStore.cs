using System.IO;
using System.Text.Json;

namespace Vestigium.Nuget.Publish;

public sealed class PublishSettings
{
    public List<string> Roots { get; set; } = [];

    public string Source { get; set; } = "https://api.nuget.org/v3/index.json";

    public string OutputFolder { get; set; } = "artifacts/nupkg";
}

public static class PublishStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vestigium", "NugetPublish");

    public static string SettingsPath => Path.Combine(Folder, "settings.json");

    public static string KeyPath => Path.Combine(Folder, "apikey.dpapi");

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

    public static void SaveApiKey(string key)
    {
        Directory.CreateDirectory(Folder);
        var plain = System.Text.Encoding.UTF8.GetBytes(key.Trim());
        var secret = System.Security.Cryptography.ProtectedData.Protect(
            plain,
            null,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        File.WriteAllBytes(KeyPath, secret);
    }

    public static string? LoadApiKey()
    {
        if (!File.Exists(KeyPath))
            return null;
        try
        {
            var secret = File.ReadAllBytes(KeyPath);
            var plain = System.Security.Cryptography.ProtectedData.Unprotect(
                secret,
                null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public static void ClearApiKey()
    {
        if (File.Exists(KeyPath))
            File.Delete(KeyPath);
    }
}
