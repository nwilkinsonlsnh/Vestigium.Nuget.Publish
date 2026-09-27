using CommunityToolkit.Mvvm.ComponentModel;

namespace Vestigium.Nuget.Publish;

public sealed partial class RepoRow : ObservableObject
{
    public RepoRow(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        Head = "—";
    }

    public string Name { get; }

    public string Path { get; }

    [ObservableProperty]
    private string _head = "—";
}
