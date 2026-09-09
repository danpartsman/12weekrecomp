using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

/// <summary>
/// User-authorized Engine 3.0 habitual-activity profile.
///
/// Occupation and lifestyle are evidence for ONE Habitual Activity bucket.
/// Formal workouts stay in the workout system and steps stay observational.
/// </summary>
public sealed class HabitualActivityController(
    RecompDbContext db,
    AppUserContextService userContext,
    Engine30ClientActivityProfileService activityProfiles) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var today = AppUserContextService.GetClientToday(context.Client);
        var profile = await activityProfiles.GetCurrentProfileAsync(
            context.Client.ClientId,
            today,
            context.Client.LifestyleActivityCode,
            cancellationToken);

        var rows = profile.Roles
            .Select(x => new HabitualOccupationRoleEditViewModel
            {
                OccupationName = x.OccupationName,
                OccupationActivityCode = x.OccupationActivityCode,
                TypicalHoursPerWeek = x.TypicalHoursPerWeek,
                IsPrimary = x.IsPrimary
            })
            .ToList();

        while (rows.Count < 3)
            rows.Add(new HabitualOccupationRoleEditViewModel());

        return View(new HabitualActivityViewModel
        {
            LifestyleActivityCode = profile.LifestyleActivityCode,
            Roles = rows.Take(3).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        HabitualActivityViewModel model,
        CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        model.Roles ??= [];
        while (model.Roles.Count < 3)
            model.Roles.Add(new HabitualOccupationRoleEditViewModel());

        Engine30LifestyleActivityOption lifestyle;
        try
        {
            lifestyle = Engine30ActivityProfileCatalog.ResolveLifestyle(model.LifestyleActivityCode);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(model.LifestyleActivityCode), ex.Message);
            lifestyle = Engine30ActivityProfileCatalog.ResolveLifestyle("MINIMAL");
        }

        var normalizedRoles = NormalizeRoles(model, ModelState);

        if (!ModelState.IsValid)
            return View(model);

        var client = context.Client;
        var today = AppUserContextService.GetClientToday(client);
        var now = DateTime.UtcNow;

        var current = await db.ClientOccupationalActivities
            .Where(x =>
                x.ClientId == client.ClientId
                && x.EffectiveFrom <= today
                && (x.EffectiveTo == null || x.EffectiveTo >= today))
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.OccupationName)
            .ToListAsync(cancellationToken);

        var rolesChanged = !SameRoles(current, normalizedRoles);
        var lifestyleChanged = !string.Equals(
            client.LifestyleActivityCode ?? "MINIMAL",
            lifestyle.Code,
            StringComparison.OrdinalIgnoreCase);

        if (!rolesChanged && !lifestyleChanged)
        {
            TempData["StatusMessage"] = "Habitual activity is already up to date.";
            return RedirectToAction("Index", "Profile");
        }

        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            client.LifestyleActivityCode = lifestyle.Code;
            client.UpdatedAtUtc = now;

            if (rolesChanged)
            {
                foreach (var oldRole in current)
                {
                    if (oldRole.EffectiveFrom >= today)
                    {
                        db.ClientOccupationalActivities.Remove(oldRole);
                    }
                    else
                    {
                        oldRole.EffectiveTo = today.AddDays(-1);
                        oldRole.UpdatedAtUtc = now;
                    }
                }

                // Flush closures/removals before inserting a new current primary
                // so the filtered unique index remains satisfied.
                await db.SaveChangesAsync(cancellationToken);

                foreach (var role in normalizedRoles)
                {
                    db.ClientOccupationalActivities.Add(
                        new ClientOccupationalActivity
                        {
                            ClientOccupationalActivityId = Guid.NewGuid(),
                            ClientId = client.ClientId,
                            OccupationName = role.OccupationName,
                            OccupationActivityCode = role.OccupationActivityCode,
                            TypicalHoursPerWeek = role.TypicalHoursPerWeek,
                            IsPrimary = role.IsPrimary,
                            EffectiveFrom = today,
                            EffectiveTo = null,
                            CreatedAtUtc = now,
                            UpdatedAtUtc = now
                        });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        TempData["StatusMessage"] = "Work and habitual activity updated for Engine 3.0.";
        return RedirectToAction("Index", "Profile");
    }

    private static List<NormalizedRole> NormalizeRoles(
        HabitualActivityViewModel model,
        Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState)
    {
        var normalized = new List<NormalizedRole>();

        for (var i = 0; i < model.Roles.Count && i < 3; i++)
        {
            var row = model.Roles[i];
            var hasAny =
                !string.IsNullOrWhiteSpace(row.OccupationName)
                || !string.IsNullOrWhiteSpace(row.OccupationActivityCode)
                || row.TypicalHoursPerWeek.GetValueOrDefault() > 0m
                || row.IsPrimary;

            if (!hasAny)
                continue;

            if (string.IsNullOrWhiteSpace(row.OccupationName))
                modelState.AddModelError($"Roles[{i}].OccupationName", "Enter an occupation or role name.");

            if (string.IsNullOrWhiteSpace(row.OccupationActivityCode))
                modelState.AddModelError($"Roles[{i}].OccupationActivityCode", "Choose the activity level for this role.");

            if (!row.TypicalHoursPerWeek.HasValue || row.TypicalHoursPerWeek <= 0m)
                modelState.AddModelError($"Roles[{i}].TypicalHoursPerWeek", "Enter typical hours per week greater than zero.");

            Engine30OccupationActivityOption? option = null;
            if (!string.IsNullOrWhiteSpace(row.OccupationActivityCode))
            {
                try
                {
                    option = Engine30ActivityProfileCatalog.ResolveOccupation(row.OccupationActivityCode);
                }
                catch (ArgumentException ex)
                {
                    modelState.AddModelError($"Roles[{i}].OccupationActivityCode", ex.Message);
                }
            }

            if (string.IsNullOrWhiteSpace(row.OccupationName)
                || option is null
                || !row.TypicalHoursPerWeek.HasValue
                || row.TypicalHoursPerWeek <= 0m)
            {
                continue;
            }

            normalized.Add(new NormalizedRole(
                row.OccupationName.Trim(),
                option.Code,
                Math.Round(row.TypicalHoursPerWeek.Value, 1, MidpointRounding.AwayFromZero),
                row.IsPrimary));
        }

        if (normalized.Sum(x => x.TypicalHoursPerWeek) > 168m)
            modelState.AddModelError(nameof(model.Roles), "Combined occupational hours cannot exceed 168 hours per week.");

        if (normalized.Count(x => x.IsPrimary) > 1)
            modelState.AddModelError(nameof(model.Roles), "Choose only one primary occupation.");

        if (normalized.Count > 0 && normalized.All(x => !x.IsPrimary))
            normalized[0] = normalized[0] with { IsPrimary = true };

        return normalized;
    }

    private static bool SameRoles(
        IReadOnlyCollection<ClientOccupationalActivity> current,
        IReadOnlyCollection<NormalizedRole> proposed)
    {
        if (current.Count != proposed.Count)
            return false;

        var a = current
            .Select(x => new NormalizedRole(
                x.OccupationName.Trim(),
                x.OccupationActivityCode.Trim().ToUpperInvariant(),
                Math.Round(x.TypicalHoursPerWeek, 1, MidpointRounding.AwayFromZero),
                x.IsPrimary))
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.OccupationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.OccupationActivityCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var b = proposed
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.OccupationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.OccupationActivityCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i].OccupationName, b[i].OccupationName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].OccupationActivityCode, b[i].OccupationActivityCode, StringComparison.OrdinalIgnoreCase)
                || a[i].TypicalHoursPerWeek != b[i].TypicalHoursPerWeek
                || a[i].IsPrimary != b[i].IsPrimary)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record NormalizedRole(
        string OccupationName,
        string OccupationActivityCode,
        decimal TypicalHoursPerWeek,
        bool IsPrimary);
}
