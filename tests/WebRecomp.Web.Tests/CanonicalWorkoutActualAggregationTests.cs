using WebRecomp.Web.Models;
using WebRecomp.Web.Services.WorkoutIntelligence;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class CanonicalWorkoutActualAggregationTests
{
    [Fact]
    public async Task CanonicalCollectionPreservesMultipleLegitimateSameDaySessions()
    {
        var clientId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 5);

        var strength = new WorkoutSession
        {
            WorkoutSessionId = Guid.NewGuid(),
            ClientId = clientId,
            ProgramId = programId,
            WorkoutDate = date,
            WorkoutTypeCode = "MAJOR_PUSH",
            DurationMin = 55m,
            SessionRpe = 8m,
            CompletedFlag = true,
            EntrySourceCode = "DETAILED_WORKOUT",
            CreatedAtUtc = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 10, 5, 16, 0, 0, DateTimeKind.Utc)
        };

        var cardio = new WorkoutSession
        {
            WorkoutSessionId = Guid.NewGuid(),
            ClientId = clientId,
            ProgramId = programId,
            WorkoutDate = date,
            WorkoutTypeCode = "ZONE2_CARDIO",
            DurationMin = 30m,
            SessionRpe = 4m,
            CompletedFlag = true,
            EntrySourceCode = "MANUAL",
            CreatedAtUtc = new DateTime(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 10, 5, 20, 30, 0, DateTimeKind.Utc)
        };

        var draft = new WorkoutSession
        {
            WorkoutSessionId = Guid.NewGuid(),
            ClientId = clientId,
            ProgramId = programId,
            WorkoutDate = date,
            WorkoutTypeCode = "ACCESSORY",
            DurationMin = 20m,
            CompletedFlag = false,
            EntrySourceCode = "DETAILED_WORKOUT",
            CreatedAtUtc = new DateTime(2026, 10, 5, 21, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 10, 5, 21, 5, 0, DateTimeKind.Utc)
        };

        var actuals =
            await DetailedWorkoutEngine30Adapter.CanonicalCompletedSessionsAsync(
                null!,
                [cardio, draft, strength]);

        Assert.Equal(2, actuals.Count);
        Assert.Equal(strength.WorkoutSessionId, actuals[0].WorkoutSessionId);
        Assert.Equal(cardio.WorkoutSessionId, actuals[1].WorkoutSessionId);
    }
}
