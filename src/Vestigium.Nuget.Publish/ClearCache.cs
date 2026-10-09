using CommunityToolkit.Mvvm.Input;

namespace Vestigium.Nuget.Publish;

public sealed partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task ClearCacheAsync()
    {
        if (SelectedRepos.Count == 0)
            return;

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
            LoadProjects();
        }
    }
}
