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
    {
        var repo = project.RepoPath;
        var output = Path.IsPathRooted(outputFolder)
            ? outputFolder
            : Path.Combine(repo, outputFolder);
        Directory.CreateDirectory(output);
        log($"restore {project.ProjectName}");
        var restore = await RunAsync("dotnet", $"restore \"{project.ProjectPath}\"", repo, log, token);
        if (restore != 0)
            return restore;
        log($"pack {project.ProjectName} {project.Version}");
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
        var id = project.ProjectName;
        var nupkg = Directory.Exists(output)
            ? Directory.EnumerateFiles(output, $"{id}.{project.Version}.nupkg").FirstOrDefault()
              ?? Directory.EnumerateFiles(output, $"{id}.*.nupkg").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (nupkg is null)
        {
            log($"no nupkg for {id} in {output}");
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
