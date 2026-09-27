using System.Windows;

namespace Vestigium.Nuget.Publish;

public partial class App : Application
{
    public static Vestigium.Themes.ThemeManager Themes { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Themes.Initialize(this);
    }
}
