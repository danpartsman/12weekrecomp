using WebRecomp.CatalogSync;

namespace WebRecomp.Web.Services.CatalogSync;

public sealed class CatalogSyncHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<CatalogSyncHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<CatalogSyncCoordinator>();
                var result = await coordinator.RunIfDueAsync(stoppingToken);

                if (result.StatusCode is not "NOT_DUE")
                {
                    logger.LogInformation(
                        "Exercise catalog scheduled sync result: {StatusCode}, changed={Changed}, active={ActiveCount}",
                        result.StatusCode,
                        result.Changed,
                        result.ActiveCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A catalog refresh must never prevent the 12WR web application from running.
                logger.LogError(ex, "Exercise catalog scheduled synchronization failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
