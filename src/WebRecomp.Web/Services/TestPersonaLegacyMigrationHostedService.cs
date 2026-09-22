using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Services;

/// <summary>
/// TEST-only one-time compatibility migration for renamed synthetic persona identities.
/// This runs before the persona seeders so existing synthetic history is preserved.
/// </summary>
public sealed class TestPersonaLegacyMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<TestPersonaLegacyMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<TestPersonaSessionService>();

        if (!sessions.IsEnabled)
            return;

        var db = scope.ServiceProvider.GetRequiredService<RecompDbContext>();
        var oldEmail = TestPersonaCatalog.LegacyF160Email;
        const string newEmail = "testf160@test.com";
        var oldSubject = TestPersonaCatalog.ExternalSubject(oldEmail);
        var newSubject = TestPersonaCatalog.ExternalSubject(newEmail);

        var newUser = await db.UserAccounts
            .SingleOrDefaultAsync(x => x.ExternalSubject == newSubject, cancellationToken);

        if (newUser is not null)
            return;

        var legacyUser = await db.UserAccounts
            .SingleOrDefaultAsync(x => x.ExternalSubject == oldSubject, cancellationToken);

        if (legacyUser is null)
            return;

        legacyUser.ExternalSubject = newSubject;
        legacyUser.EmailNormalized = newEmail;
        legacyUser.ContactEmail = newEmail;
        legacyUser.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Renamed synthetic TEST persona {OldEmail} to {NewEmail} while preserving its existing client/program history.",
            oldEmail,
            newEmail);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
