using WebRecomp.Web.Services.Scheduling;

namespace WebRecomp.Web.Services.Coaching;

public sealed class SimpleDeadlineJob(SimpleCoachingService coaching, IConfiguration configuration) : IScheduledJob
{
    public Task RunAsync(CancellationToken cancellationToken) =>
        SimpleCoaching.Enabled(configuration) ? coaching.PostDueAsync(cancellationToken) : Task.CompletedTask;
}
