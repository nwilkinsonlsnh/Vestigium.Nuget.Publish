using System.Diagnostics;
using System.IO;
using System.Text;

namespace Vestigium.Nuget.Publish;

public static class DotnetCli
{
    public static int CleanPacks(IEnumerable<string> repoPaths, string outputFolder, Action<string> log)
    {
        var removed = 0;
        foreach (var repo in repoPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var output = Path.IsPathRooted(outputFolder)
                ? outputFolder
                : Path.Combine(repo, outputFolder);
            if (!Directory.Exists(output))
                continue;

            foreach (var file in Directory.EnumerateFiles(output, "*.nupkg")
                         .Concat(Directory.EnumerateFiles(output, "*.snupkg")))
            {
                try
                {
                    File.Delete(file);
                    removed++;
                    log($"deleted {Path.GetFileName(file)}");
                }
                catch (IOException ex)
                {
                    log($"{Path.GetFileName(file)}  {ex.Message}");
                }
            }
        }

        log(removed == 0 ? "no packed files" : $"cleaned {removed} files");
        return 0;
    }

    public static int ListPacks(IEnumerable<string> repoPaths, string outputFolder, Action<string> log)
    {
        var found = 0;
        foreach (var repo in repoPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var output = Path.IsPathRooted(outputFolder)
                ? outputFolder
                : Path.Combine(repo, outputFolder);
            if (!Directory.Exists(output))
            {
                log($"{Path.GetFileName(repo)}  no pack folder");
                continue;
            }

            var files = Directory.EnumerateFiles(output, "*.nupkg")
                .Concat(Directory.EnumerateFiles(output, "*.snupkg"))
                .OrderBy(Path.GetFileName)
                .ToArray();
            if (files.Length == 0)
            {
                log($"{Path.GetFileName(repo)}  {output}  empty");
                continue;
            }

            log($"{Path.GetFileName(repo)}  {output}");
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                log($"  {info.Name}  {info.Length:N0} bytes  {info.LastWriteTime:g}");
                found++;
            }
        }

        log(found == 0 ? "no packed files" : $"{found} packed files");
        return 0;
    }

    public static async Task<int> PackAsync(PackableProject project, string outputFolder, Action<string> log, CancellationToken token)
    {
        var repo = project.RepoPath;
        var output = Path.IsPathRooted(outputFolder)
            ? outputFolder
            : Path.Combine(repo, outputFolder);
        Directory.CreateDirectory(output);
        log($"restore {project.PackageId}");
        var restore = await RunAsync("dotnet", $"restore \"{project.ProjectPath}\"", repo, log, token);
        if (restore != 0)
            return restore;
        log($"pack {project.PackageId} {project.LocalVersion}");
        return await RunAsync(
            "dotnet",
            $"pack \"{project.ProjectPath}\" -c Release -o \"{output}\"",
            repo,
            log,
            token);
    }

    public static async Task<int> PushAsync(
        PackableProject project,
        string outputFolder,
        string source,
        string apiKey,
        Action<string> log,
        CancellationToken token)
    {
        var repo = project.RepoPath;
        var output = Path.IsPathRooted(outputFolder)
            ? outputFolder
            : Path.Combine(repo, outputFolder);
        var nupkg = FindPackage(output, project);
        if (nupkg is null)
        {
            log($"no nupkg for {project.PackageId} {project.LocalVersion} in {output}");
            return 1;
        }

        log($"push {Path.GetFileName(nupkg)}");
        return await RunAsync(
            "dotnet",
            $"nuget push \"{nupkg}\" --api-key \"{apiKey}\" --source \"{source}\" --skip-duplicate",
            repo,
            log,
            token);
    }

    private static string? FindPackage(string output, PackableProject project)
    {
        if (!Directory.Exists(output))
            return null;

        var exact = Path.Combine(output, $"{project.PackageId}.{project.LocalVersion}.nupkg");
        if (File.Exists(exact))
            return exact;

        return Directory.EnumerateFiles(output, $"{project.PackageId}.*.nupkg")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static async Task<int> RunAsync(string file, string args, string work, Action<string> log, CancellationToken token)
    {
        var start = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = work,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var text = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                log(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                log(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(token);
        _ = text;
        return process.ExitCode;
    }
}
