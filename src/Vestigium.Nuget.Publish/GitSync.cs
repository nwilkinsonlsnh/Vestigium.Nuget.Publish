using System.Diagnostics;
using System.IO;
using System.Text;

namespace Vestigium.Nuget.Publish;

public static class GitSync
{
    public static async Task<int> SyncAsync(string repoPath, Action<string> log, CancellationToken token)
    {
        if (!Directory.Exists(Path.Combine(repoPath, ".git")))
        {
            log($"not a git repo {repoPath}");
            return 1;
        }

        log($"git fetch {Path.GetFileName(repoPath)}");
        var fetch = await RunAsync("git", "fetch --prune", repoPath, log, token);
        if (fetch != 0)
            return fetch;

        var pull = await PullSafelyAsync(repoPath, log, token);
        if (pull != 0)
            return pull;

        log($"synced {await DescribeAsync(repoPath, token)}");
        return 0;
    }

    public static async Task<string> DescribeAsync(string repoPath, CancellationToken token)
    {
        var branch = (await ReadAsync("git", "rev-parse --abbrev-ref HEAD", repoPath, token)).Trim();
        var sha = (await ReadAsync("git", "rev-parse --short HEAD", repoPath, token)).Trim();
        if (string.IsNullOrWhiteSpace(sha))
            return "—";
        return string.IsNullOrWhiteSpace(branch) ? sha : $"{branch}  {sha}";
    }

    public static async Task<int> EnsureCurrentAsync(string repoPath, Action<string> log, CancellationToken token)
    {
        if (!Directory.Exists(Path.Combine(repoPath, ".git")))
        {
            log($"not a git repo {repoPath}");
            return 1;
        }

        log($"git fetch {Path.GetFileName(repoPath)}");
        var fetch = await RunAsync("git", "fetch --prune", repoPath, log, token);
        if (fetch != 0)
            return fetch;

        var synced = await PullSafelyAsync(repoPath, log, token);
        if (synced != 0)
            return synced;

        var counts = (await ReadAsync("git", "rev-list --left-right --count @{u}...HEAD", repoPath, token)).Trim();
        if (counts.Contains("no upstream", StringComparison.OrdinalIgnoreCase)
            || counts.Contains("unknown revision", StringComparison.OrdinalIgnoreCase))
        {
            log("no upstream — pack proceeds on local HEAD");
            return 0;
        }

        var parts = counts.Split('\t', ' ');
        var behind = parts.Length > 0 && int.TryParse(parts[0], out var b) ? b : 0;
        var ahead = parts.Length > 1 && int.TryParse(parts[1], out var a) ? a : 0;
        if (behind > 0)
        {
            log($"behind {behind} — pull --ff-only");
            var pull = await PullSafelyAsync(repoPath, log, token);
            if (pull != 0)
                return pull;
        }

        if (ahead > 0)
        {
            log($"ahead {ahead} — push");
            var push = await RunAsync("git", "push", repoPath, log, token);
            if (push != 0)
                return push;
        }

        log("git current");
        return 0;
    }

    public static async Task<int> CommitAndPushAsync(
        string repoPath,
        IReadOnlyList<string> filePaths,
        string message,
        Action<string> log,
        CancellationToken token)
    {
        foreach (var filePath in filePaths.Where(File.Exists))
        {
            var add = await RunAsync("git", $"add -- \"{filePath}\"", repoPath, log, token);
            if (add != 0)
                return add;
        }

        var staged = await ReadAsync("git", "diff --cached --name-only", repoPath, token);
        if (string.IsNullOrWhiteSpace(staged))
        {
            log("version already committed — pack continues");
            return 0;
        }

        var commit = await RunAsync("git", $"commit -m \"{message}\"", repoPath, log, token);
        if (commit != 0)
        {
            var status = await ReadAsync("git", "status --porcelain", repoPath, token);
            if (string.IsNullOrWhiteSpace(status))
            {
                log("version already committed — pack continues");
                return 0;
            }

            return commit;
        }

        return await RunAsync("git", "push", repoPath, log, token);
    }

    private static async Task<int> PullSafelyAsync(string repoPath, Action<string> log, CancellationToken token)
    {
        var porcelain = (await ReadAsync("git", "status --porcelain", repoPath, token)).Trim();
        var stashed = false;
        if (porcelain.Length > 0)
        {
            log("local changes — stashing before pull");
            log(porcelain);
            var stash = await RunAsync("git", "stash push -u -m nuget-publish", repoPath, log, token);
            if (stash != 0)
                return stash;
            stashed = true;
        }

        var pull = await RunAsync("git", "pull --ff-only", repoPath, log, token);
        if (pull != 0)
        {
            if (stashed)
                await RunAsync("git", "stash pop", repoPath, log, token);
            return pull;
        }

        if (!stashed)
            return 0;
        var pop = await RunTextAsync("git", "stash pop", repoPath, log, token);
        if (pop.Code == 0)
        {
            log("local changes restored");
            return 0;
        }

        var detail = pop.Text;
        var alreadyThere = detail.Contains("already exists, no checkout", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("could not restore untracked files", StringComparison.OrdinalIgnoreCase);
        if (alreadyThere)
        {
            log("pulled files kept — the local stub was already on the remote");
            await RunAsync("git", "stash drop", repoPath, log, token);
            return 0;
        }

        log("local changes conflict with the pull — pack stopped");
        return pop.Code;

    }

    private static async Task<(int Code, string Text)> RunTextAsync(string file, string args, string work, Action<string> log, CancellationToken token)
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
        using var process = new Process { StartInfo = start };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(token);
        var error = await process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var text = (output + error).Trim();
        if (text.Length > 0)
            log(text);
        return (process.ExitCode, text);
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
        using var process = new Process { StartInfo = start };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(token);
        var error = await process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (!string.IsNullOrWhiteSpace(output))
            log(output.TrimEnd());
        if (!string.IsNullOrWhiteSpace(error))
            log(error.TrimEnd());
        return process.ExitCode;
    }

    private static async Task<string> ReadAsync(string file, string args, string work, CancellationToken token)
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
        using var process = new Process { StartInfo = start };
        process.Start();
        var text = new StringBuilder();
        text.Append(await process.StandardOutput.ReadToEndAsync(token));
        text.Append(await process.StandardError.ReadToEndAsync(token));
        await process.WaitForExitAsync(token);
        return text.ToString();
    }
}
