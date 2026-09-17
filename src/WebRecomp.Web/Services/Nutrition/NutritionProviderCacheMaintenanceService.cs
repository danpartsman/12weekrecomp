using WebRecomp.NutritionIntelligence.Persistence;

namespace WebRecomp.Web.Services.Nutrition;

public sealed class NutritionProviderCacheMaintenanceService(
    IServiceScopeFactory scopeFactory,
    ILogger<NutritionProviderCacheMaintenanceService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await PurgeAsync(stoppingToken);

        using var timer =
            new PeriodicTimer(
                TimeSpan.FromMinutes(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PurgeAsync(stoppingToken);
    }

    private async Task PurgeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope =
                scopeFactory.CreateScope();

            var store =
                scope.ServiceProvider
                    .GetRequiredService<INutritionFoodLogStore>();

            var expired =
                await store.PurgeExpiredProviderCacheAsync(
                    cancellationToken);

            if (expired > 0)
            {
                logger.LogInformation(
                    "Expired {Count} temporary nutrition provider cache rows.",
                    expired);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Nutrition provider cache maintenance failed.");
        }
    }
}
