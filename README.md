# Vestigium.Nuget.Publish

WPF host that remembers repo roots, lists packable projects, packs with `dotnet pack`, and pushes to nuget.org.

The NuGet API key is stored only on this machine under `%AppData%\Vestigium\NugetPublish\` using Windows DPAPI. It is not in git.

## Run

Open `Vestigium.Nuget.Publish.slnx` in VS 2026. F5.

1. Add a root such as `D:\Source\Clone`
2. Scan
3. Save the API key once
4. Select a project → Pack → Push

## Package layout

Before pack, the host checks the project and every `Directory.Build.props` for two files that would land at the same package path. The usual case is a project `README.md` plus the repo readme already packed by `Directory.Build.props`. That collision is removed. Any other collision stops the pack and names the path and the sources.

After pack, the nupkg is opened. A duplicate entry stops the push with the path. NuGet returns 400 for that and does not say which file.
