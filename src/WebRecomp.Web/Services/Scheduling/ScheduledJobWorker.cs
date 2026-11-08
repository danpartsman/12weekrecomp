namespace WebRecomp.Web.Services.Scheduling;

public interface IScheduledJob
{
    Task RunAsync(CancellationToken cancellationToken);
}

public static class ScheduledJobRegistration
{
    public static IServiceCollection AddScheduledJob<TJob>(this IServiceCollection services, TimeSpan interval)
        where TJob : class, IScheduledJob
    {
        if (interval <= TimeSpan.Zero || interval > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(interval));

        services.AddScoped<TJob>();
        services.AddHostedService<ScheduledJobWorker<TJob>>(provider => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            interval,
            provider.GetRequiredService<ILogger<ScheduledJobWorker<TJob>>>()));
        return services;
    }
}

internal sealed class ScheduledJobWorker<TJob>(
    IServiceScopeFactory scopes,
    TimeSpan interval,
    ILogger<ScheduledJobWorker<TJob>> logger) : BackgroundService
    where TJob : class, IScheduledJob
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The job owns its due-state and deduplication. Running immediately
        // lets it pick up any work that became due while the app was stopped.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TJob>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning("Scheduled job {JobName} will retry after {FailureType}.",
                    typeof(TJob).Name, error.GetType().Name);
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
