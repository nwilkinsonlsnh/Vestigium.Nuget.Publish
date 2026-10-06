using CommunityToolkit.Mvvm.Input;

namespace Vestigium.Nuget.Publish;

public sealed partial class MainViewModel
{
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (Busy)
            return;
        if (SelectedRepos.Count == 0)
        {
            Append("select a repo");
            return;
        }

        Busy = true;
        Status = "Clearing cache";
        var token = Token();
        try
        {
            foreach (var repo in SelectedRepos.ToArray())
            {
                Append(repo.Name);
                var code = await DotnetCli.ClearHttpCacheAndRestoreAsync(repo.Path, Append, token);
                if (code != 0)
                {
                    Status = $"Restore failed  {repo.Name}";
                    return;
                }
            }

            Status = "Cache cleared";
            Append(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
            Append(Status);
        }
        catch (Exception ex)
        {
            Status = "Failed";
            Append(ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }
}
