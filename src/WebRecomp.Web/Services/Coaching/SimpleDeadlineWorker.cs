namespace WebRecomp.Web.Services.Coaching;
public sealed class SimpleDeadlineWorker(IServiceScopeFactory scopes,IConfiguration configuration,ILogger<SimpleDeadlineWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if(SimpleCoaching.Enabled(configuration)){using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<SimpleCoachingService>().PostDueAsync(stoppingToken);}
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogWarning("Snap deadline checks will retry after {FailureType}.",e.GetType().Name);}
            await Task.Delay(TimeSpan.FromSeconds(20),stoppingToken);
        }
    }
}
