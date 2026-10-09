using WebRecomp.Web.Models;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Tests;

public sealed class CompletedWorkoutSummaryCorrectionTests
{
    [Fact]
    public void CorrectionPreservesActualIdentitySourceAndCompletionWhileClearingStaleCalculations()
    {
        var session = Session();
        var id = session.WorkoutSessionId;
        var templateId = session.WorkoutTemplateId;
        var date = session.WorkoutDate;
        new WorkoutActualService(null!).CorrectCompletedSummary(session, 47.5m, 8.5m);
        Assert.Equal(id, session.WorkoutSessionId);
        Assert.Equal(templateId, session.WorkoutTemplateId);
        Assert.Equal(date, session.WorkoutDate);
        Assert.Equal("DETAILED_WORKOUT", session.EntrySourceCode);
        Assert.Equal("source", session.SourceRecordId);
        Assert.True(session.CompletedFlag);
        Assert.Equal(47.5m, session.DurationMin);
        Assert.Equal(8.5m, session.SessionRpe);
        Assert.Null(session.CalculatedWorkoutCalories);
        Assert.Equal(WorkoutActualService.ActiveEngineVersionCode, session.CalculationEngineVersion);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(361, 5)]
    [InlineData(40, 0)]
    [InlineData(40, 11)]
    public void InvalidCorrectionDoesNotMutateActual(int duration, int rpe)
    {
        var session = Session();
        Assert.Throws<ArgumentException>(() => new WorkoutActualService(null!).CorrectCompletedSummary(session, duration, rpe));
        Assert.Equal(40m, session.DurationMin);
        Assert.Equal(7m, session.SessionRpe);
        Assert.Equal(999m, session.CalculatedWorkoutCalories);
    }

    [Fact]
    public void InProgressWorkoutCannotBeCompletedThroughCorrection()
    {
        var session = Session();
        session.CompletedFlag = false;
        Assert.Throws<ArgumentException>(() => new WorkoutActualService(null!).CorrectCompletedSummary(session, 40m, 8m));
        Assert.False(session.CompletedFlag);
    }

    [Fact]
    public void UnknownRpeCanStayBlank()
    {
        var session = Session();
        new WorkoutActualService(null!).CorrectCompletedSummary(session, 40m, null);
        Assert.Null(session.SessionRpe);
    }

    [Fact]
    public void RestCannotAcquireArtificialWorkoutMinutesOrRpe()
    {
        var session = Session();
        session.WorkoutTypeCode = "REST";
        var service = new WorkoutActualService(null!);
        Assert.Throws<ArgumentException>(() => service.CorrectCompletedSummary(session, 30m, 5m));
        service.CorrectCompletedSummary(session, 0m, null);
        Assert.Equal(0m, session.DurationMin);
        Assert.Null(session.SessionRpe);
    }

    private static WorkoutSession Session() => new()
    {
        WorkoutSessionId = Guid.NewGuid(), ClientId = Guid.NewGuid(), ProgramId = Guid.NewGuid(),
        WorkoutTemplateId = Guid.NewGuid(), WorkoutTypeCode = "MAJOR_LEGS", WorkoutName = "Legs",
        WorkoutDate = new DateOnly(2026, 10, 7), CompletedFlag = true, DurationMin = 40m, SessionRpe = 7m,
        EntrySourceCode = "DETAILED_WORKOUT", SourceRecordId = "source", CalculatedWorkoutCalories = 999m
    };
}
