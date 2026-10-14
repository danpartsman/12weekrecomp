using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Tests;

public sealed class ExerciseNotesCleanupTests
{
    [Fact]
    public async Task ClearAndUndoAreScopedAndPreserveWorkoutDataAndNewNotes()
    {
        using var db = new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var client = Guid.NewGuid();
        var other = Guid.NewGuid();
        var session = new WorkoutSession { WorkoutSessionId = Guid.NewGuid(), ClientId = client,
            WorkoutTypeCode = "ACCESSORY", Notes = "Session note", DurationMin = 30, CompletedFlag = true };
        var otherSession = new WorkoutSession { WorkoutSessionId = Guid.NewGuid(), ClientId = other, WorkoutTypeCode = "ACCESSORY" };
        var template = new WorkoutTemplateRecord { WorkoutTemplateId = Guid.NewGuid(), ClientId = client,
            TemplateName = "Saved", WorkoutTypeCode = "ACCESSORY", Notes = "Template note" };
        var exercise = new DetailedWorkoutExerciseRecord { DetailedWorkoutExerciseId = Guid.NewGuid(),
            WorkoutSessionId = session.WorkoutSessionId, ExerciseNameSnapshot = "Squat", Notes = "Generated", MaxWeight = 100 };
        var otherExercise = new DetailedWorkoutExerciseRecord { DetailedWorkoutExerciseId = Guid.NewGuid(),
            WorkoutSessionId = otherSession.WorkoutSessionId, ExerciseNameSnapshot = "Squat", Notes = "Other client" };
        var saved = new WorkoutTemplateExerciseRecord { WorkoutTemplateExerciseId = Guid.NewGuid(),
            WorkoutTemplateId = template.WorkoutTemplateId, ExerciseNameSnapshot = "Squat", Notes = "Saved note", Sets = 3, Reps = 10 };
        db.AddRange(session, otherSession, template, exercise, otherExercise, saved);
        await db.SaveChangesAsync();
        var environment = new TestEnvironment();
        try
        {
            Assert.Equal(2, await ExerciseNotesCleanup.ClearAsync(db, client, environment, default));
            Assert.Null(exercise.Notes);
            Assert.Null(saved.Notes);
            Assert.Equal("Other client", otherExercise.Notes);
            Assert.Equal("Session note", session.Notes);
            Assert.Equal("Template note", template.Notes);
            Assert.Equal(100m, exercise.MaxWeight);
            Assert.True(session.CompletedFlag);
            Assert.Equal(3, saved.Sets);
            Assert.Equal(0, await ExerciseNotesCleanup.ClearAsync(db, client, environment, default));
            exercise.Notes = "New personal note";
            await db.SaveChangesAsync();
            Assert.Equal(1, await ExerciseNotesCleanup.RestoreAsync(db, client, environment, default));
            Assert.Equal("New personal note", exercise.Notes);
            Assert.Equal("Saved note", saved.Notes);
            Assert.Equal(0, await ExerciseNotesCleanup.RestoreAsync(db, other, environment, default));
        }
        finally { if (Directory.Exists(environment.ContentRootPath)) Directory.Delete(environment.ContentRootPath, true); }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
