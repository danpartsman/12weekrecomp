using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/workout-types")]
public sealed class AdminWorkoutTypesController(
    RecompDbContext db,
    Engine30WorkoutProfileService engine30Profiles,
    Engine30AdminFuelComparisonService fuelComparison) : Controller
{
    // Existing v1008 workout-definition catalog remains the storage surface
    // while Engine 3.0 develops in shadow. Engine 2.0 calculation logic is
    // not modified by this controller.
    private const string TargetVersionCode = "CFG-3000.0";

    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var config =
            await GetTargetConfigAsync(cancellationToken);

        if (config is null)
            return NotFound();

        var definitions =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .Where(x =>
                    x.ConfigVersionId ==
                    config.ConfigVersionId)
                .ToListAsync(cancellationToken);

        var ids =
            definitions
                .Select(x => x.WorkoutTypeDefinitionId)
                .ToArray();

        var activeProfiles =
            await db.Engine30WorkoutProfileVersions
                .AsNoTracking()
                .Where(x =>
                    ids.Contains(x.WorkoutTypeDefinitionId)
                    && x.IsActive)
                .ToListAsync(cancellationToken);

        var byWorkout =
            activeProfiles.ToDictionary(
                x => x.WorkoutTypeDefinitionId);

        var rows =
            new List<AdminWorkoutTypeRowViewModel>();

        foreach (var workout in definitions
            .OrderBy(x =>
                WorkoutCategoryCatalog.SortOrder(
                    x.WorkoutCategoryCode))
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.DisplayName))
        {
            Engine30WorkoutAdminProfile profile;
            Engine30WorkoutProfileVersion? version = null;

            if (byWorkout.TryGetValue(
                workout.WorkoutTypeDefinitionId,
                out var active))
            {
                version = active;
                profile =
                    engine30Profiles.ReadProfileJson(
                        active.ProfileJson);
            }
            else
            {
                profile =
                    engine30Profiles.BuildBaselineProfile(
                        workout);
            }

            rows.Add(
                new AdminWorkoutTypeRowViewModel
                {
                    WorkoutTypeDefinitionId =
                        workout.WorkoutTypeDefinitionId,

                    WorkoutTypeCode =
                        workout.WorkoutTypeCode,

                    DisplayName =
                        workout.DisplayName,

                    WorkoutCategoryCode =
                        workout.WorkoutCategoryCode,

                    WorkoutCategoryName =
                        WorkoutCategoryCatalog.DisplayName(
                            workout.WorkoutCategoryCode),

                    FuelScore = workout.FuelScore,

                    Engine30FuelGlycogen60MinuteG =
                        fuelComparison.Calculate60MinuteGlycogen(profile),
                    DefaultMinutes =
                        profile.DefaultDurationMinutes,

                    PushLoad = workout.PushLoad,
                    PullLoad = workout.PullLoad,
                    LegLoad = workout.LegLoad,
                    CardioLoad = workout.CardioLoad,

                    IsActive = workout.IsActive,
                    IsSelectable = workout.IsSelectable,
                    DisplayOrder = workout.DisplayOrder,

                    Engine30FamilyCode =
                        profile.WorkoutFamilyCode,

                    Engine30ModalityCode =
                        profile.DefaultModalityCode,

                    Engine30DefaultRpe =
                        profile.DefaultRpe,

                    Engine30VersionCode =
                        version?.VersionCode,

                    HasEngine30Profile =
                        version is not null
                });
        }

        return View(
            new AdminWorkoutTypesIndexViewModel
            {
                ConfigVersionCode = config.VersionCode,
                StatusCode = config.StatusCode,
                Workouts = rows
            });
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(
        CancellationToken cancellationToken)
    {
        var config =
            await GetEditableConfigAsync(
                cancellationToken);

        if (config is null)
            return NotFound();

        var seedWorkout =
            new WorkoutTypeDefinition
            {
                WorkoutTypeDefinitionId = Guid.Empty,
                ConfigVersionId = config.ConfigVersionId,
                WorkoutTypeCode = "NEW_WORKOUT",
                DisplayName = "New Workout",
                WorkoutCategoryCode =
                    WorkoutCategoryCatalog.AccessoryStrength,
                IsSelectable = false,
                DisplayOrder = 100,
                WeeklyRoleCode = "OTHER_LOAD",
                FuelPriorityCode = "LOW",
                SchedulePrincipleCode = "SCHEDULE_NORMALLY",
                DefaultMinutes = 45m,
                FuelScore = 2m,
                IsActive = true
            };

        var profile =
            engine30Profiles.BuildBaselineProfile(
                seedWorkout);

        return View(
            "Edit",
            CreateEditModel(
                seedWorkout,
                config,
                MapProfileToViewModel(profile),
                state: null));
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        AdminWorkoutTypeEditViewModel model,
        CancellationToken cancellationToken)
    {
        var config =
            await GetEditableConfigAsync(
                cancellationToken);

        if (config is null)
            return NotFound();

        model.WorkoutTypeDefinitionId = Guid.Empty;
        model.ConfigVersionCode = config.VersionCode;
        model.StatusCode = config.StatusCode;

        RemoveServerControlledFieldsFromModelState();

        model.WorkoutTypeCode =
            (model.WorkoutTypeCode ?? "")
                .Trim()
                .ToUpperInvariant();

        ValidateCommonModel(
            model,
            isCreate: true);

        var duplicate =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ConfigVersionId ==
                            config.ConfigVersionId
                        &&
                        x.WorkoutTypeCode ==
                            model.WorkoutTypeCode,
                    cancellationToken);

        if (duplicate)
        {
            ModelState.AddModelError(
                nameof(model.WorkoutTypeCode),
                "That workout code already exists.");
        }

        var profile =
            ToProfile(model.Engine30);

        ValidateEngine30Profile(
            profile);

        if (!ModelState.IsValid)
            return View("Edit", model);

        var workout =
            new WorkoutTypeDefinition
            {
                WorkoutTypeDefinitionId = Guid.NewGuid(),
                ConfigVersionId = config.ConfigVersionId,

                WorkoutTypeCode =
                    model.WorkoutTypeCode
                        .Trim()
                        .ToUpperInvariant(),

                DisplayName =
                    model.DisplayName.Trim(),

                WorkoutCategoryCode =
                    model.WorkoutCategoryCode
                        .Trim()
                        .ToUpperInvariant(),

                WeeklyRoleCode =
                    model.WeeklyRoleCode
                        .Trim()
                        .ToUpperInvariant(),

                FuelPriorityCode =
                    model.FuelPriorityCode
                        .Trim()
                        .ToUpperInvariant(),

                SchedulePrincipleCode =
                    model.SchedulePrincipleCode
                        .Trim()
                        .ToUpperInvariant(),

                IsActive = true,
                IsSelectable = model.IsSelectable
            };

        ApplyLegacyWorkoutFields(
            workout,
            model,
            profile);

        await using var transaction =
            await db.Database.BeginTransactionAsync(
                cancellationToken);

        db.WorkoutTypeDefinitions.Add(workout);

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            await engine30Profiles.CreateBaselineAsync(
                workout,
                profile,
                User.Identity?.Name,
                string.IsNullOrWhiteSpace(model.Engine30ChangeNotes)
                    ? "Initial Engine 3.0 profile created with workout."
                    : model.Engine30ChangeNotes,
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            TempData["WorkoutError"] =
                $"Create failed: {ex.GetBaseException().Message}";

            return RedirectToAction(nameof(Index));
        }

        TempData["WorkoutSuccess"] =
            $"{workout.DisplayName} was created with an Engine 3.0 baseline profile. The workout remains hidden until you make it selectable.";

        return RedirectToAction(
            nameof(Edit),
            new { id = workout.WorkoutTypeDefinitionId });
    }

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(
        Guid id,
        CancellationToken cancellationToken)
    {
        var config =
            await GetEditableConfigAsync(
                cancellationToken);

        if (config is null)
            return NotFound();

        var workout =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutTypeDefinitionId == id
                        &&
                        x.ConfigVersionId ==
                            config.ConfigVersionId,
                    cancellationToken);

        if (workout is null)
            return NotFound();

        var state =
            await engine30Profiles.GetOrCreateStateAsync(
                workout,
                User.Identity?.Name,
                cancellationToken);

        return View(
            CreateEditModel(
                workout,
                config,
                MapProfileToViewModel(
                    state.ActiveProfile),
                state));
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        AdminWorkoutTypeEditViewModel model,
        CancellationToken cancellationToken)
    {
        var config =
            await GetEditableConfigAsync(
                cancellationToken);

        if (config is null)
            return NotFound();

        var workout =
            await db.WorkoutTypeDefinitions
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutTypeDefinitionId == id
                        &&
                        x.ConfigVersionId ==
                            config.ConfigVersionId,
                    cancellationToken);

        if (workout is null)
            return NotFound();

        model.WorkoutTypeDefinitionId = id;
        model.WorkoutTypeCode = workout.WorkoutTypeCode;
        model.ConfigVersionCode = config.VersionCode;
        model.StatusCode = config.StatusCode;

        RemoveServerControlledFieldsFromModelState();

        ValidateCommonModel(
            model,
            isCreate: false);

        var profile =
            ToProfile(model.Engine30);

        ValidateEngine30Profile(
            profile);

        RemoveUnusedMuscleRegionModelStateErrors(
            model.Engine30);

        RemoveUnusedSegmentModelStateErrors(
            model.Engine30);

        if (!ModelState.IsValid)
        {
            ModelState.AddModelError(
                string.Empty,
                "Save was not completed because one or more fields failed validation. Review the messages below.");

            var state =
                await engine30Profiles.GetOrCreateStateAsync(
                    workout,
                    User.Identity?.Name,
                    cancellationToken);

            ApplyProfileStateToModel(
                model,
                profile,
                state);

            return View(model);
        }

        string? savedVersionCode = null;
        string? savedDisplayName = null;

        try
        {
            var strategy =
                db.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(
                async () =>
                {
                    // A retry must start from database truth rather than
                    // entity states left over from a failed prior attempt.
                    db.ChangeTracker.Clear();

                    var retryWorkout =
                        await db.WorkoutTypeDefinitions
                            .SingleAsync(
                                x =>
                                    x.WorkoutTypeDefinitionId == id
                                    &&
                                    x.ConfigVersionId ==
                                        config.ConfigVersionId,
                                cancellationToken);

                    ApplyLegacyWorkoutFields(
                        retryWorkout,
                        model,
                        profile);

                    await using var transaction =
                        await db.Database.BeginTransactionAsync(
                            cancellationToken);

                    try
                    {
                        await db.SaveChangesAsync(
                            cancellationToken);

                        var state =
                            await engine30Profiles.SaveNewVersionAsync(
                                retryWorkout,
                                profile,
                                User.Identity?.Name,
                                model.Engine30ChangeNotes,
                                cancellationToken);

                        await transaction.CommitAsync(
                            cancellationToken);

                        savedVersionCode =
                            state.ActiveVersion.VersionCode;

                        savedDisplayName =
                            retryWorkout.DisplayName;
                    }
                    catch
                    {
                        await transaction.RollbackAsync(
                            cancellationToken);

                        throw;
                    }
                });

            if (string.IsNullOrWhiteSpace(
                    savedVersionCode))
            {
                throw new InvalidOperationException(
                    "Engine 3.0 profile save completed without returning an active version.");
            }

            TempData["WorkoutSuccess"] =
                $"{savedDisplayName ?? model.DisplayName} was updated. Active Engine 3.0 profile: {savedVersionCode}.";
        }
        catch (Exception ex)
        {
            TempData["WorkoutError"] =
                $"Save failed: {ex.GetBaseException().Message}";
        }

        return RedirectToAction(
            nameof(Edit),
            new { id });
    }

    [HttpPost("{id:guid}/restore/{profileVersionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreProfileVersion(
        Guid id,
        Guid profileVersionId,
        CancellationToken cancellationToken)
    {
        var config =
            await GetEditableConfigAsync(
                cancellationToken);

        if (config is null)
            return NotFound();

        var workout =
            await db.WorkoutTypeDefinitions
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutTypeDefinitionId == id
                        &&
                        x.ConfigVersionId ==
                            config.ConfigVersionId,
                    cancellationToken);

        if (workout is null)
            return NotFound();

        try
        {
            var state =
                await engine30Profiles.RestoreVersionAsync(
                    workout,
                    profileVersionId,
                    User.Identity?.Name,
                    cancellationToken);

            TempData["WorkoutSuccess"] =
                $"Restored Engine 3.0 settings into new active version {state.ActiveVersion.VersionCode}. Historical versions were preserved.";
        }
        catch (Exception ex)
        {
            TempData["WorkoutError"] =
                $"Restore failed: {ex.GetBaseException().Message}";
        }

        return RedirectToAction(
            nameof(Edit),
            new { id });
    }

    [HttpPost("{id:guid}/reset-baseline")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetEngine30ToBaseline(
        Guid id,
        CancellationToken cancellationToken)
    {
        var config =
            await GetEditableConfigAsync(
                cancellationToken);

        if (config is null)
            return NotFound();

        var workout =
            await db.WorkoutTypeDefinitions
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutTypeDefinitionId == id
                        &&
                        x.ConfigVersionId ==
                            config.ConfigVersionId,
                    cancellationToken);

        if (workout is null)
            return NotFound();

        try
        {
            var state =
                await engine30Profiles.RestoreBaselineAsync(
                    workout,
                    User.Identity?.Name,
                    cancellationToken);

            TempData["WorkoutSuccess"] =
                $"Engine 3.0 settings were reset from the immutable baseline into new active version {state.ActiveVersion.VersionCode}.";
        }
        catch (Exception ex)
        {
            TempData["WorkoutError"] =
                $"Reset failed: {ex.GetBaseException().Message}";
        }

        return RedirectToAction(
            nameof(Edit),
            new { id });
    }

    [HttpPost("{id:guid}/clone")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clone(
        Guid id,
        CancellationToken cancellationToken)
    {
        var config =
            await GetEditableConfigAsync(
                cancellationToken);

        if (config is null)
            return NotFound();

        var source =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutTypeDefinitionId == id
                        &&
                        x.ConfigVersionId ==
                            config.ConfigVersionId,
                    cancellationToken);

        if (source is null)
            return NotFound();

        var sourceState =
            await engine30Profiles.GetOrCreateStateAsync(
                source,
                User.Identity?.Name,
                cancellationToken);

        var baseCode =
            source.WorkoutTypeCode + "_COPY";

        var newCode = baseCode;
        var copyNumber = 2;

        while (await db.WorkoutTypeDefinitions
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.ConfigVersionId ==
                        config.ConfigVersionId
                    &&
                    x.WorkoutTypeCode ==
                        newCode,
                cancellationToken))
        {
            newCode =
                $"{baseCode}_{copyNumber}";

            copyNumber++;
        }

        var clone =
            CloneLegacyWorkout(
                source,
                newCode);

        db.WorkoutTypeDefinitions.Add(clone);

        await using var transaction =
            await db.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            await engine30Profiles.CreateBaselineAsync(
                clone,
                sourceState.ActiveProfile,
                User.Identity?.Name,
                $"Cloned from {source.WorkoutTypeCode} profile {sourceState.ActiveVersion.VersionCode}.",
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            TempData["WorkoutError"] =
                $"Clone failed: {ex.GetBaseException().Message}";

            return RedirectToAction(nameof(Index));
        }

        TempData["WorkoutSuccess"] =
            $"{source.DisplayName} was cloned with its Engine 3.0 profile. The copy is hidden until you make it selectable.";

        return RedirectToAction(
            nameof(Edit),
            new { id = clone.WorkoutTypeDefinitionId });
    }

    [HttpPost("{id:guid}/toggle-selectable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSelectable(
        Guid id,
        CancellationToken cancellationToken)
    {
        var config =
            await GetEditableConfigAsync(
                cancellationToken);

        if (config is null)
            return NotFound();

        var workout =
            await db.WorkoutTypeDefinitions
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutTypeDefinitionId == id
                        &&
                        x.ConfigVersionId ==
                            config.ConfigVersionId,
                    cancellationToken);

        if (workout is null)
            return NotFound();

        var makeSelectable =
            !workout.IsSelectable;

        if (makeSelectable
            &&
            !workout.IsActive)
        {
            TempData["WorkoutError"] =
                "An inactive workout cannot be made selectable.";

            return RedirectToAction(nameof(Index));
        }

        workout.IsSelectable =
            makeSelectable;

        try
        {
            await db.SaveChangesAsync(
                cancellationToken);
        }
        catch (Exception ex)
        {
            TempData["WorkoutError"] =
                $"Update failed: {ex.GetBaseException().Message}";

            return RedirectToAction(nameof(Index));
        }

        TempData["WorkoutSuccess"] =
            workout.IsSelectable
                ? $"{workout.DisplayName} is now selectable."
                : $"{workout.DisplayName} is now hidden from user selection.";

        return RedirectToAction(nameof(Index));
    }

    private async Task<ConfigVersion?> GetTargetConfigAsync(
        CancellationToken cancellationToken)
        => await db.ConfigVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.VersionCode ==
                    TargetVersionCode,
                cancellationToken);

    private async Task<ConfigVersion?> GetEditableConfigAsync(
        CancellationToken cancellationToken)
    {
        var config =
            await GetTargetConfigAsync(
                cancellationToken);

        if (config is not null
            &&
            !string.Equals(
                config.StatusCode,
                "DRAFT",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only a DRAFT workout configuration can be edited.");
        }

        return config;
    }

    private void ValidateCommonModel(
        AdminWorkoutTypeEditViewModel model,
        bool isCreate)
    {
        if (isCreate
            &&
            string.IsNullOrWhiteSpace(
                model.WorkoutTypeCode))
        {
            ModelState.AddModelError(
                nameof(model.WorkoutTypeCode),
                "Workout code is required.");
        }

        if (string.IsNullOrWhiteSpace(
            model.DisplayName))
        {
            ModelState.AddModelError(
                nameof(model.DisplayName),
                "Display name is required.");
        }

        if (!WorkoutCategoryCatalog.IsValid(
            model.WorkoutCategoryCode))
        {
            ModelState.AddModelError(
                nameof(model.WorkoutCategoryCode),
                "Select a valid workout category.");
        }

        if (string.IsNullOrWhiteSpace(
            model.WeeklyRoleCode))
        {
            ModelState.AddModelError(
                nameof(model.WeeklyRoleCode),
                "Weekly role code is required.");
        }

        if (string.IsNullOrWhiteSpace(
            model.FuelPriorityCode))
        {
            ModelState.AddModelError(
                nameof(model.FuelPriorityCode),
                "Fuel priority code is required.");
        }

        if (string.IsNullOrWhiteSpace(
            model.SchedulePrincipleCode))
        {
            ModelState.AddModelError(
                nameof(model.SchedulePrincipleCode),
                "Schedule principle code is required.");
        }
    }

    private void ValidateEngine30Profile(
        Engine30WorkoutAdminProfile profile)
    {
        try
        {
            Engine30WorkoutProfileService.Validate(
                profile);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(
                nameof(AdminWorkoutTypeEditViewModel.Engine30),
                ex.Message);
        }
    }

    private static void ApplyLegacyWorkoutFields(
        WorkoutTypeDefinition workout,
        AdminWorkoutTypeEditViewModel model,
        Engine30WorkoutAdminProfile profile)
    {
        workout.DisplayName =
            model.DisplayName.Trim();

        workout.WorkoutCategoryCode =
            model.WorkoutCategoryCode
                .Trim()
                .ToUpperInvariant();

        workout.Description =
            Normalize(model.Description);

        // Keep legacy Engine 2 duration synchronized with the Engine 3
        // planning default. Engine 2 math itself remains untouched.
        workout.DefaultMinutes =
            profile.DefaultDurationMinutes;

        workout.FuelScore = model.FuelScore;

        workout.PushLoad = model.PushLoad;
        workout.PullLoad = model.PullLoad;
        workout.LegLoad = model.LegLoad;
        workout.CardioLoad = model.CardioLoad;

        workout.Instructions =
            Normalize(model.Instructions);

        workout.OverviewVideoUrl =
            Normalize(model.OverviewVideoUrl);

        workout.WeeklyRoleCode =
            model.WeeklyRoleCode
                .Trim()
                .ToUpperInvariant();

        workout.RegionCode =
            Normalize(model.RegionCode)?
                .ToUpperInvariant();

        workout.CountsDedicatedHeavy =
            model.CountsDedicatedHeavy;

        workout.CountsLightStrength =
            model.CountsLightStrength;

        workout.CountsPrimaryConditioning =
            model.CountsPrimaryConditioning;

        workout.CountsRecovery =
            model.CountsRecovery;

        workout.FuelPriorityCode =
            model.FuelPriorityCode
                .Trim()
                .ToUpperInvariant();

        workout.SchedulePrincipleCode =
            model.SchedulePrincipleCode
                .Trim()
                .ToUpperInvariant();

        workout.StrategyNote =
            Normalize(model.StrategyNote);

        workout.MacroClassCode =
            Normalize(model.MacroClassCode);

        workout.PrimaryFocusCode =
            Normalize(model.PrimaryFocusCode);

        workout.RawGlycogenGMin =
            model.RawGlycogenGMin;

        workout.DefaultIntensityFactor =
            model.DefaultIntensityFactor;

        workout.DefaultRestSec =
            model.DefaultRestSec;

        workout.ReferenceWorkoutCalPerMin =
            model.ReferenceWorkoutCalPerMin;

        workout.GlycogenCarbShare =
            model.GlycogenCarbShare;

        workout.StrengthStress =
            model.StrengthStress;

        workout.PlannedRestorationPct =
            model.PlannedRestorationPct;

        workout.ProteinAdjustmentG =
            model.ProteinAdjustmentG;

        workout.TargetFatG =
            model.TargetFatG;

        workout.TypicalRecoveryHours =
            model.TypicalRecoveryHours;

        workout.PlanningNote =
            Normalize(model.PlanningNote);

        workout.IsSelectable =
            model.IsSelectable;

        workout.DisplayOrder =
            model.DisplayOrder;
    }

    private static WorkoutTypeDefinition CloneLegacyWorkout(
        WorkoutTypeDefinition source,
        string newCode)
        => new()
        {
            WorkoutTypeDefinitionId = Guid.NewGuid(),
            ConfigVersionId = source.ConfigVersionId,
            WorkoutTypeCode = newCode,
            DisplayName = source.DisplayName + " Copy",

            WorkoutCategoryCode = source.WorkoutCategoryCode,
            IsSelectable = false,
            DisplayOrder = source.DisplayOrder,

            Description = source.Description,
            Instructions = source.Instructions,
            OverviewVideoUrl = source.OverviewVideoUrl,

            WeeklyRoleCode = source.WeeklyRoleCode,
            RegionCode = source.RegionCode,
            CountsDedicatedHeavy = source.CountsDedicatedHeavy,
            CountsLightStrength = source.CountsLightStrength,
            CountsPrimaryConditioning = source.CountsPrimaryConditioning,
            CountsRecovery = source.CountsRecovery,
            FuelPriorityCode = source.FuelPriorityCode,
            SchedulePrincipleCode = source.SchedulePrincipleCode,
            StrategyNote = source.StrategyNote,

            MacroClassCode = source.MacroClassCode,
            PrimaryFocusCode = source.PrimaryFocusCode,
            FuelScore = source.FuelScore,
            DefaultMinutes = source.DefaultMinutes,
            RawGlycogenGMin = source.RawGlycogenGMin,
            DefaultIntensityFactor = source.DefaultIntensityFactor,
            DefaultRestSec = source.DefaultRestSec,
            ReferenceWorkoutCalPerMin = source.ReferenceWorkoutCalPerMin,
            GlycogenCarbShare = source.GlycogenCarbShare,

            PushLoad = source.PushLoad,
            PullLoad = source.PullLoad,
            LegLoad = source.LegLoad,
            CardioLoad = source.CardioLoad,
            StrengthStress = source.StrengthStress,
            PlannedRestorationPct = source.PlannedRestorationPct,
            ProteinAdjustmentG = source.ProteinAdjustmentG,
            TargetFatG = source.TargetFatG,
            TypicalRecoveryHours = source.TypicalRecoveryHours,
            PlanningNote = source.PlanningNote,

            IsActive = true
        };

    private static AdminWorkoutTypeEditViewModel CreateEditModel(
        WorkoutTypeDefinition workout,
        ConfigVersion config,
        AdminEngine30WorkoutProfileViewModel profile,
        Engine30WorkoutProfileState? state)
    {
        var model =
            new AdminWorkoutTypeEditViewModel
            {
                WorkoutTypeDefinitionId =
                    workout.WorkoutTypeDefinitionId,

                WorkoutTypeCode =
                    workout.WorkoutTypeDefinitionId == Guid.Empty
                        ? ""
                        : workout.WorkoutTypeCode,

                DisplayName =
                    workout.WorkoutTypeDefinitionId == Guid.Empty
                        ? ""
                        : workout.DisplayName,

                WorkoutCategoryCode =
                    workout.WorkoutCategoryCode,

                Description = workout.Description,

                DefaultMinutes =
                    profile.DefaultDurationMinutes,

                FuelScore = workout.FuelScore,

                PushLoad = workout.PushLoad,
                PullLoad = workout.PullLoad,
                LegLoad = workout.LegLoad,
                CardioLoad = workout.CardioLoad,

                Instructions = workout.Instructions,
                OverviewVideoUrl = workout.OverviewVideoUrl,

                WeeklyRoleCode = workout.WeeklyRoleCode,
                RegionCode = workout.RegionCode,

                CountsDedicatedHeavy =
                    workout.CountsDedicatedHeavy,

                CountsLightStrength =
                    workout.CountsLightStrength,

                CountsPrimaryConditioning =
                    workout.CountsPrimaryConditioning,

                CountsRecovery =
                    workout.CountsRecovery,

                FuelPriorityCode =
                    workout.FuelPriorityCode,

                SchedulePrincipleCode =
                    workout.SchedulePrincipleCode,

                StrategyNote = workout.StrategyNote,
                MacroClassCode = workout.MacroClassCode,
                PrimaryFocusCode = workout.PrimaryFocusCode,

                RawGlycogenGMin =
                    workout.RawGlycogenGMin,

                DefaultIntensityFactor =
                    workout.DefaultIntensityFactor,

                DefaultRestSec =
                    workout.DefaultRestSec,

                ReferenceWorkoutCalPerMin =
                    workout.ReferenceWorkoutCalPerMin,

                GlycogenCarbShare =
                    workout.GlycogenCarbShare,

                StrengthStress =
                    workout.StrengthStress,

                PlannedRestorationPct =
                    workout.PlannedRestorationPct,

                ProteinAdjustmentG =
                    workout.ProteinAdjustmentG,

                TargetFatG =
                    workout.TargetFatG,

                TypicalRecoveryHours =
                    workout.TypicalRecoveryHours,

                PlanningNote =
                    workout.PlanningNote,

                IsSelectable =
                    workout.IsSelectable,

                DisplayOrder =
                    workout.DisplayOrder,

                ConfigVersionCode =
                    config.VersionCode,

                StatusCode =
                    config.StatusCode,

                Engine30 =
                    profile
            };

        if (state is not null)
        {
            ApplyProfileStateToModel(
                model,
                state.ActiveProfile,
                state);
        }
        else
        {
            model.Engine30ImpliedMetAt210Lb =
                Engine30WorkoutProfileService
                    .ImpliedMetAt210Lb(
                        ToProfile(profile));
        }

        return model;
    }

    private static void ApplyProfileStateToModel(
        AdminWorkoutTypeEditViewModel model,
        Engine30WorkoutAdminProfile profile,
        Engine30WorkoutProfileState state)
    {
        model.Engine30 =
            MapProfileToViewModel(
                profile);

        model.Engine30ActiveVersionCode =
            state.ActiveVersion.VersionCode;

        model.Engine30BaselineVersionCode =
            state.BaselineVersion.VersionCode;

        model.Engine30History =
            state.History
                .Select(x =>
                    new AdminEngine30WorkoutVersionViewModel
                    {
                        Engine30WorkoutProfileVersionId =
                            x.Engine30WorkoutProfileVersionId,

                        VersionNumber =
                            x.VersionNumber,

                        VersionCode =
                            x.VersionCode,

                        IsBaseline =
                            x.IsBaseline,

                        IsActive =
                            x.IsActive,

                        RestoredFromProfileVersionId =
                            x.RestoredFromProfileVersionId,

                        ChangeNotes =
                            x.ChangeNotes,

                        CreatedBy =
                            x.CreatedBy,

                        CreatedAtUtc =
                            x.CreatedAtUtc
                    })
                .ToList();

        model.Engine30ImpliedMetAt210Lb =
            Engine30WorkoutProfileService
                .ImpliedMetAt210Lb(
                    profile);
    }

    private static AdminEngine30WorkoutProfileViewModel MapProfileToViewModel(
        Engine30WorkoutAdminProfile profile)
    {
        var result =
            new AdminEngine30WorkoutProfileViewModel
            {
                WorkoutFamilyCode =
                    profile.WorkoutFamilyCode,

                DefaultModalityCode =
                    profile.DefaultModalityCode,

                TrainingPatternCode =
                    profile.TrainingPatternCode,

                SessionStructureCode =
                    profile.SessionStructureCode,

                DefaultDurationMinutes =
                    profile.DefaultDurationMinutes,

                DefaultRpe =
                    profile.DefaultRpe,

                SupportsObjectiveWorkload =
                    profile.SupportsObjectiveWorkload,

                SupportsResistanceDetails =
                    profile.SupportsResistanceDetails,

                SupportsIntervalDetails =
                    profile.SupportsIntervalDetails,

                SupportsSegments =
                    profile.SupportsSegments,

                ReferenceWorkoutCaloriesPerMinute =
                    profile.ReferenceWorkoutCaloriesPerMinute,

                ReferenceRpe =
                    profile.ReferenceRpe,

                ReferenceWeightLb =
                    profile.ReferenceWeightLb,

                ScalingBasisCode =
                    profile.ScalingBasisCode,

                ReferenceFatFreeMassLb =
                    profile.ReferenceFatFreeMassLb,

                DefaultRestSeconds =
                    profile.DefaultRestSeconds,

                ResistanceReferenceDurationMinutes =
                    profile.ResistanceReferenceDurationMinutes,

                ResistanceReferenceHardSets =
                    profile.ResistanceReferenceHardSets,

                ResistanceHardSetDensitySensitivity =
                    profile.ResistanceHardSetDensitySensitivity,

                ResistanceRestFallbackSensitivity =
                    profile.ResistanceRestFallbackSensitivity,

                ResistanceDensityMinimumFactor =
                    profile.ResistanceDensityMinimumFactor,

                ResistanceDensityMaximumFactor =
                    profile.ResistanceDensityMaximumFactor,

                GlycogenModelCode =
                    profile.GlycogenModelCode,

                GlycogenReferenceDurationMinutes =
                    profile.GlycogenReferenceDurationMinutes,

                GlycogenReferenceDepletionFraction =
                    profile.GlycogenReferenceDepletionFraction,

                GlycogenIntensitySensitivity =
                    profile.GlycogenIntensitySensitivity,

                GlycogenMinimumDepletionFraction =
                    profile.GlycogenMinimumDepletionFraction,

                GlycogenMaximumDepletionFraction =
                    profile.GlycogenMaximumDepletionFraction,

                TargetRestorationFraction =
                    profile.TargetRestorationFraction,

                CarbohydratePerGlycogenG =
                    profile.CarbohydratePerGlycogenG,

                IntervalWorkSeconds =
                    profile.IntervalWorkSeconds,

                IntervalRecoverySeconds =
                    profile.IntervalRecoverySeconds,

                IntervalRepetitions =
                    profile.IntervalRepetitions,

                IntervalWorkRpe =
                    profile.IntervalWorkRpe,

                IntervalRecoveryRpe =
                    profile.IntervalRecoveryRpe,

                IntervalRecoveryContributionWeight =
                    profile.IntervalRecoveryContributionWeight,

                IntervalWorkIntensitySensitivity =
                    profile.IntervalWorkIntensitySensitivity,

                IntervalRecoveryIntensitySensitivity =
                    profile.IntervalRecoveryIntensitySensitivity
            };

        result.MuscleRegions =
            profile.MuscleRegions
                .Select(x =>
                    new AdminEngine30MuscleRegionViewModel
                    {
                        MuscleRegionCode =
                            x.MuscleRegionCode,

                        WholeBodyMuscleFraction =
                            x.WholeBodyMuscleFraction,

                        RecruitmentFraction =
                            x.RecruitmentFraction,

                        HardSets =
                            x.HardSets
                    })
                .ToList();

        while (result.MuscleRegions.Count < 4)
        {
            result.MuscleRegions.Add(
                new AdminEngine30MuscleRegionViewModel());
        }

        result.Segments =
            profile.Segments
                .OrderBy(x => x.DisplayOrder)
                .Select(x =>
                    new AdminEngine30SegmentViewModel
                    {
                        Enabled = x.Enabled,
                        DisplayOrder = x.DisplayOrder,
                        SegmentCode = x.SegmentCode,
                        DisplayName = x.DisplayName,
                        ModalityCode = x.ModalityCode,
                        TrainingPatternCode = x.TrainingPatternCode,
                        SessionStructureCode = x.SessionStructureCode,
                        DurationMinutes = x.DurationMinutes,
                        DefaultRpe = x.DefaultRpe,
                        ReferenceWorkoutCaloriesPerMinute =
                            x.ReferenceWorkoutCaloriesPerMinute,
                        ReferenceRpe = x.ReferenceRpe,
                        ScalingBasisCode = x.ScalingBasisCode,
                        ReferenceFatFreeMassLb =
                            x.ReferenceFatFreeMassLb,
                        DefaultRestSeconds =
                            x.DefaultRestSeconds,
                        GlycogenModelCode =
                            x.GlycogenModelCode,
                        GlycogenReferenceDepletionFraction =
                            x.GlycogenReferenceDepletionFraction,
                        LowerBodyMuscleFraction =
                            x.LowerBodyMuscleFraction,
                        LowerBodyRecruitmentFraction =
                            x.LowerBodyRecruitmentFraction,
                        UpperBodyMuscleFraction =
                            x.UpperBodyMuscleFraction,
                        UpperBodyRecruitmentFraction =
                            x.UpperBodyRecruitmentFraction,
                        HardSetsPerRegion =
                            x.HardSetsPerRegion
                    })
                .ToList();

        while (result.Segments.Count < 6)
        {
            result.Segments.Add(
                new AdminEngine30SegmentViewModel
                {
                    DisplayOrder =
                        (result.Segments.Count + 1) * 10
                });
        }

        return result;
    }

    private static Engine30WorkoutAdminProfile ToProfile(
        AdminEngine30WorkoutProfileViewModel model)
        => new()
        {
            WorkoutFamilyCode =
                NormalizeCode(
                    model.WorkoutFamilyCode),

            DefaultModalityCode =
                NormalizeCode(
                    model.DefaultModalityCode),

            TrainingPatternCode =
                NormalizeCode(
                    model.TrainingPatternCode),

            SessionStructureCode =
                NormalizeCode(
                    model.SessionStructureCode),

            DefaultDurationMinutes =
                model.DefaultDurationMinutes,

            DefaultRpe =
                model.DefaultRpe,

            SupportsObjectiveWorkload =
                model.SupportsObjectiveWorkload,

            SupportsResistanceDetails =
                model.SupportsResistanceDetails,

            SupportsIntervalDetails =
                model.SupportsIntervalDetails,

            SupportsSegments =
                model.SupportsSegments,

            ReferenceWorkoutCaloriesPerMinute =
                model.ReferenceWorkoutCaloriesPerMinute,

            ReferenceRpe =
                model.ReferenceRpe,

            ReferenceWeightLb =
                model.ReferenceWeightLb,

            ScalingBasisCode =
                NormalizeCode(
                    model.ScalingBasisCode),

            ReferenceFatFreeMassLb =
                model.ReferenceFatFreeMassLb,

            DefaultRestSeconds =
                model.DefaultRestSeconds,

            ResistanceReferenceDurationMinutes =
                model.ResistanceReferenceDurationMinutes,

            ResistanceReferenceHardSets =
                model.ResistanceReferenceHardSets,

            ResistanceHardSetDensitySensitivity =
                model.ResistanceHardSetDensitySensitivity,

            ResistanceRestFallbackSensitivity =
                model.ResistanceRestFallbackSensitivity,

            ResistanceDensityMinimumFactor =
                model.ResistanceDensityMinimumFactor,

            ResistanceDensityMaximumFactor =
                model.ResistanceDensityMaximumFactor,

            GlycogenModelCode =
                NormalizeCode(
                    model.GlycogenModelCode),

            GlycogenReferenceDurationMinutes =
                model.GlycogenReferenceDurationMinutes,

            GlycogenReferenceDepletionFraction =
                model.GlycogenReferenceDepletionFraction,

            GlycogenIntensitySensitivity =
                model.GlycogenIntensitySensitivity,

            GlycogenMinimumDepletionFraction =
                model.GlycogenMinimumDepletionFraction,

            GlycogenMaximumDepletionFraction =
                model.GlycogenMaximumDepletionFraction,

            TargetRestorationFraction =
                model.TargetRestorationFraction,

            CarbohydratePerGlycogenG =
                model.CarbohydratePerGlycogenG,

            IntervalWorkSeconds =
                model.IntervalWorkSeconds,

            IntervalRecoverySeconds =
                model.IntervalRecoverySeconds,

            IntervalRepetitions =
                model.IntervalRepetitions,

            IntervalWorkRpe =
                model.IntervalWorkRpe,

            IntervalRecoveryRpe =
                model.IntervalRecoveryRpe,

            IntervalRecoveryContributionWeight =
                model.IntervalRecoveryContributionWeight,

            IntervalWorkIntensitySensitivity =
                model.IntervalWorkIntensitySensitivity,

            IntervalRecoveryIntensitySensitivity =
                model.IntervalRecoveryIntensitySensitivity,

            MuscleRegions =
                model.MuscleRegions
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.MuscleRegionCode))
                    .Select(x =>
                        new Engine30WorkoutAdminMuscleRegion
                        {
                            MuscleRegionCode =
                                NormalizeCode(
                                    x.MuscleRegionCode),

                            WholeBodyMuscleFraction =
                                x.WholeBodyMuscleFraction,

                            RecruitmentFraction =
                                x.RecruitmentFraction,

                            HardSets =
                                x.HardSets
                        })
                    .ToList(),

            Segments =
                model.Segments
                    .Where(x =>
                        x.Enabled
                        ||
                        !string.IsNullOrWhiteSpace(
                            x.SegmentCode))
                    .Select(x =>
                        new Engine30WorkoutAdminSegment
                        {
                            Enabled = x.Enabled,
                            DisplayOrder = x.DisplayOrder,
                            SegmentCode =
                                NormalizeCode(
                                    x.SegmentCode),
                            DisplayName =
                                x.DisplayName?.Trim()
                                ?? "",
                            ModalityCode =
                                NormalizeCode(
                                    x.ModalityCode),
                            TrainingPatternCode =
                                NormalizeCode(
                                    x.TrainingPatternCode),
                            SessionStructureCode =
                                NormalizeCode(
                                    x.SessionStructureCode),
                            DurationMinutes =
                                x.DurationMinutes,
                            DefaultRpe =
                                x.DefaultRpe,
                            ReferenceWorkoutCaloriesPerMinute =
                                x.ReferenceWorkoutCaloriesPerMinute,
                            ReferenceRpe =
                                x.ReferenceRpe,
                            ScalingBasisCode =
                                NormalizeCode(
                                    x.ScalingBasisCode),
                            ReferenceFatFreeMassLb =
                                x.ReferenceFatFreeMassLb,
                            DefaultRestSeconds =
                                x.DefaultRestSeconds,
                            GlycogenModelCode =
                                NormalizeCode(
                                    x.GlycogenModelCode),
                            GlycogenReferenceDepletionFraction =
                                x.GlycogenReferenceDepletionFraction,
                            LowerBodyMuscleFraction =
                                x.LowerBodyMuscleFraction,
                            LowerBodyRecruitmentFraction =
                                x.LowerBodyRecruitmentFraction,
                            UpperBodyMuscleFraction =
                                x.UpperBodyMuscleFraction,
                            UpperBodyRecruitmentFraction =
                                x.UpperBodyRecruitmentFraction,
                            HardSetsPerRegion =
                                x.HardSetsPerRegion
                        })
                    .OrderBy(x =>
                        x.DisplayOrder)
                    .ToList()
        };

    private static string NormalizeCode(
        string? value)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim().ToUpperInvariant();

    private static string? Normalize(
        string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private void RemoveUnusedMuscleRegionModelStateErrors(
        AdminEngine30WorkoutProfileViewModel model)
    {
        for (var i = 0; i < model.MuscleRegions.Count; i++)
        {
            var muscle = model.MuscleRegions[i];

            if (!string.IsNullOrWhiteSpace(
                    muscle.MuscleRegionCode))
            {
                continue;
            }

            var prefix =
                $"Engine30.MuscleRegions[{i}].";

            var keys =
                ModelState.Keys
                    .Where(x =>
                        x.StartsWith(
                            prefix,
                            StringComparison.Ordinal))
                    .ToArray();

            foreach (var key in keys)
            {
                ModelState.Remove(key);
            }
        }
    }
    private void RemoveUnusedSegmentModelStateErrors(
        AdminEngine30WorkoutProfileViewModel model)
    {
        for (var i = 0; i < model.Segments.Count; i++)
        {
            var segment = model.Segments[i];

            if (segment.Enabled
                || !string.IsNullOrWhiteSpace(
                    segment.SegmentCode))
            {
                continue;
            }

            var prefix =
                $"Engine30.Segments[{i}].";

            var keys =
                ModelState.Keys
                    .Where(x =>
                        x.StartsWith(
                            prefix,
                            StringComparison.Ordinal))
                    .ToArray();

            foreach (var key in keys)
            {
                ModelState.Remove(key);
            }
        }
    }

    private void RemoveServerControlledFieldsFromModelState()
    {
        ModelState.Remove(
            nameof(AdminWorkoutTypeEditViewModel.WorkoutTypeDefinitionId));

        ModelState.Remove(
            nameof(AdminWorkoutTypeEditViewModel.ConfigVersionCode));

        ModelState.Remove(
            nameof(AdminWorkoutTypeEditViewModel.StatusCode));

        ModelState.Remove(
            nameof(AdminWorkoutTypeEditViewModel.Engine30ActiveVersionCode));

        ModelState.Remove(
            nameof(AdminWorkoutTypeEditViewModel.Engine30BaselineVersionCode));

        ModelState.Remove(
            nameof(AdminWorkoutTypeEditViewModel.Engine30History));

        ModelState.Remove(
            nameof(AdminWorkoutTypeEditViewModel.Engine30ImpliedMetAt210Lb));
    }
}



