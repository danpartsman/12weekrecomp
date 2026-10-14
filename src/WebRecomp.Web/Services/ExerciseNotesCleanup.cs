using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Services;

// Notes are personal input. Bulk clearing is explicit, client-scoped and recoverable.
public static class ExerciseNotesCleanup
{
    public sealed record NoteBackup(Guid Id, bool Template, string Notes);

    private static string BackupPath(Guid clientId, IWebHostEnvironment environment)
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        var root = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"))
            && !string.IsNullOrWhiteSpace(home) ? Path.Combine(home, "data")
            : Path.Combine(environment.ContentRootPath, "App_Data");
        return Path.Combine(root, "exercise-notes-backups", clientId.ToString("N") + ".json");
    }

    public static async Task<int> ClearAsync(RecompDbContext db, Guid clientId, IWebHostEnvironment environment, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var sessions = db.WorkoutSessions.Where(x => x.ClientId == clientId).Select(x => x.WorkoutSessionId);
        var templates = db.WorkoutTemplates.Where(x => x.ClientId == clientId).Select(x => x.WorkoutTemplateId);
        var exercises = await db.DetailedWorkoutExercises.Where(x => sessions.Contains(x.WorkoutSessionId) && x.Notes != null).ToListAsync(ct);
        var saved = await db.WorkoutTemplateExercises.Where(x => templates.Contains(x.WorkoutTemplateId) && x.Notes != null).ToListAsync(ct);
        var backup = exercises.Select(x => new NoteBackup(x.DetailedWorkoutExerciseId, false, x.Notes!))
            .Concat(saved.Select(x => new NoteBackup(x.WorkoutTemplateExerciseId, true, x.Notes!))).ToArray();
        // A repeated clear of already empty fields must preserve the undo copy.
        if (backup.Length == 0) return 0;
        var path = BackupPath(clientId, environment);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(staging, JsonSerializer.Serialize(backup), ct);
        File.Move(staging, path, overwrite: true);
        var now = DateTime.UtcNow;
        foreach (var row in exercises) { row.Notes = null; row.UpdatedAtUtc = now; }
        foreach (var row in saved) { row.Notes = null; row.UpdatedAtUtc = now; }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return backup.Length;
    }

    public static async Task<int> RestoreAsync(RecompDbContext db, Guid clientId, IWebHostEnvironment environment, CancellationToken ct)
    {
        var path = BackupPath(clientId, environment);
        if (!File.Exists(path)) return 0;
        var backup = JsonSerializer.Deserialize<NoteBackup[]>(await File.ReadAllTextAsync(path, ct)) ?? [];
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var sessions = db.WorkoutSessions.Where(x => x.ClientId == clientId).Select(x => x.WorkoutSessionId);
        var templates = db.WorkoutTemplates.Where(x => x.ClientId == clientId).Select(x => x.WorkoutTemplateId);
        var exercises = await db.DetailedWorkoutExercises.Where(x => sessions.Contains(x.WorkoutSessionId) && x.Notes == null).ToListAsync(ct);
        var saved = await db.WorkoutTemplateExercises.Where(x => templates.Contains(x.WorkoutTemplateId) && x.Notes == null).ToListAsync(ct);
        var lookup = backup.ToDictionary(x => (x.Template, x.Id), x => x.Notes);
        var count = 0;
        var now = DateTime.UtcNow;
        foreach (var row in exercises)
            if (lookup.TryGetValue((false, row.DetailedWorkoutExerciseId), out var note))
            { row.Notes = note; row.UpdatedAtUtc = now; count++; }
        foreach (var row in saved)
            if (lookup.TryGetValue((true, row.WorkoutTemplateExerciseId), out var note))
            { row.Notes = note; row.UpdatedAtUtc = now; count++; }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return count;
    }
}
