using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

/// <summary>
/// Resolves the plan that was effective for a specific program date.
/// ProgramRecord.SelectedPlanCode remains the current plan. ProgramPlanChanges
/// preserve prior plan choices so historical recommendation dates keep their
/// original plan context after a mid-program plan change.
/// </summary>
public sealed class ProgramPlanService(RecompDbContext db)
{
    private readonly Dictionary<Guid, List<ProgramPlanChange>> _cache = [];

    public async Task<string> GetPlanCodeForDateAsync(
        ProgramRecord program,
        DateOnly actionDate,
        CancellationToken cancellationToken = default)
    {
        var history = await GetHistoryAsync(program.ProgramId, cancellationToken);
        if (history.Count == 0)
            return program.SelectedPlanCode;

        var applicable = history
            .Where(x => x.EffectiveDate <= actionDate)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.ChangedAtUtc)
            .FirstOrDefault();

        if (applicable is not null)
            return applicable.NewPlanCode;

        // Before the first recorded change, the plan in effect was the old plan
        // captured by that first change record.
        return history
            .OrderBy(x => x.EffectiveDate)
            .ThenBy(x => x.ChangedAtUtc)
            .First()
            .OldPlanCode;
    }

    private async Task<List<ProgramPlanChange>> GetHistoryAsync(
        Guid programId,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(programId, out var cached))
            return cached;

        var history = await db.ProgramPlanChanges
            .AsNoTracking()
            .Where(x => x.ProgramId == programId)
            .OrderBy(x => x.EffectiveDate)
            .ThenBy(x => x.ChangedAtUtc)
            .ToListAsync(cancellationToken);

        _cache[programId] = history;
        return history;
    }
}
