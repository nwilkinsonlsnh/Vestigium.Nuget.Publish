# Vestigium.Nuget.Publish

WPF host that remembers repo roots, lists packable projects, packs with `dotnet pack`, and pushes to nuget.org.

The NuGet API key is stored only on this machine under `%AppData%\Vestigium\NugetPublish\` using Windows DPAPI. It is not in git.

## Run

Open `Vestigium.Nuget.Publish.slnx` in VS 2026. F5.

1. Add a root such as `D:\Source\Clone`
2. Scan
3. Save the API key once
4. Select a project → Pack → Push
