using Vestigium.Themes;

namespace Vestigium.Nuget.Publish;

internal static class ThemeCatalog
{
    public static void RegisterAll(ThemeManager manager)
    {
        manager.Register(ThemeDefinition.FromPack(
            "LightBlue", "Light Blue", "Vestigium.Themes.LightBlue", isDark: false,
            "Default Vestigium diagnostic chrome."));
        manager.Register(ThemeDefinition.FromPack(
            "DarkMode", "Dark Mode", "Vestigium.Themes.DarkMode", isDark: true,
            "Low-glare dark surfaces."));
        manager.Register(ThemeDefinition.FromPack(
            "Terminal", "Terminal", "Vestigium.Themes.Terminal", isDark: true,
            "Operator console."));
        manager.Register(ThemeDefinition.FromPack(
            "SolarizedDark", "Solarized Dark", "Vestigium.Themes.SolarizedDark", isDark: true,
            "Calibrated Solarized Dark."));
        manager.Register(ThemeDefinition.FromPack(
            "StandardWPF", "Standard WPF", "Vestigium.Themes.StandardWPF", isDark: false,
            "Platform-default approximation."));
        manager.Register(ThemeDefinition.FromPack(
            "Monokai", "Monokai", "Vestigium.Themes.Monokai", isDark: true,
            "Pink accent on olive-black chrome."));
        manager.Register(ThemeDefinition.FromPack(
            "Sublime", "Sublime", "Vestigium.Themes.Sublime", isDark: true,
            "Mariana slate chrome."));
        manager.Register(ThemeDefinition.FromPack(
            "Dracula", "Dracula", "Vestigium.Themes.Dracula", isDark: true,
            "Purple accent on midnight surfaces."));
        manager.Register(ThemeDefinition.FromPack(
            "Nord", "Nord", "Vestigium.Themes.Nord", isDark: true,
            "Polar Night surfaces, Frost cyan accent."));
    }
}
