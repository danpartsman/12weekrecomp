using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Data;

public sealed class RecompDbContext(DbContextOptions<RecompDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<LearningCoachPreference> LearningCoachPreferences => Set<LearningCoachPreference>();
    public DbSet<LearningSeriesProgress> LearningSeriesProgress => Set<LearningSeriesProgress>();
    public DbSet<LearningInteraction> LearningInteractions => Set<LearningInteraction>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientOccupationalActivity> ClientOccupationalActivities =>
        Set<ClientOccupationalActivity>();
    public DbSet<UserClientAccess> UserClientAccess => Set<UserClientAccess>();
    public DbSet<ConfigVersion> ConfigVersions => Set<ConfigVersion>();
    public DbSet<PlanDefinition> PlanDefinitions => Set<PlanDefinition>();
    public DbSet<WorkoutTypeDefinition> WorkoutTypeDefinitions => Set<WorkoutTypeDefinition>();
    public DbSet<Engine30WorkoutProfileVersion> Engine30WorkoutProfileVersions => Set<Engine30WorkoutProfileVersion>();
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<ProgramRecord> Programs => Set<ProgramRecord>();
    public DbSet<ProgramPlanChange> ProgramPlanChanges => Set<ProgramPlanChange>();
    public DbSet<ProgramGoal> ProgramGoals => Set<ProgramGoal>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<DailyCheckin> DailyCheckins => Set<DailyCheckin>();
    public DbSet<PlannedWorkout> PlannedWorkouts => Set<PlannedWorkout>();
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();
    public DbSet<ExerciseCatalogImportRecord> ExerciseCatalogImports => Set<ExerciseCatalogImportRecord>();
    public DbSet<ExerciseCatalogItemRecord> ExerciseCatalogItems => Set<ExerciseCatalogItemRecord>();
    public DbSet<ExerciseMuscleTargetRecord> ExerciseMuscleTargets => Set<ExerciseMuscleTargetRecord>();
    public DbSet<ExerciseEquipmentRequirementRecord> ExerciseEquipmentRequirements => Set<ExerciseEquipmentRequirementRecord>();
    public DbSet<DetailedWorkoutExerciseRecord> DetailedWorkoutExercises => Set<DetailedWorkoutExerciseRecord>();
    public DbSet<DetailedWorkoutSetRecord> DetailedWorkoutSets => Set<DetailedWorkoutSetRecord>();
    public DbSet<ClientExercisePreferenceRecord> ClientExercisePreferences => Set<ClientExercisePreferenceRecord>();
    public DbSet<WorkoutTemplateRecord> WorkoutTemplates => Set<WorkoutTemplateRecord>();
    public DbSet<WorkoutTemplateExerciseRecord> WorkoutTemplateExercises => Set<WorkoutTemplateExerciseRecord>();
    public DbSet<ExerciseCatalogSyncSettingRecord> ExerciseCatalogSyncSettings => Set<ExerciseCatalogSyncSettingRecord>();
    public DbSet<ExerciseCatalogSyncRunRecord> ExerciseCatalogSyncRuns => Set<ExerciseCatalogSyncRunRecord>();
    public DbSet<ExerciseMediaAssetRecord> ExerciseMediaAssets => Set<ExerciseMediaAssetRecord>();
    public DbSet<NutritionDay> NutritionDays => Set<NutritionDay>();
    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();
    public DbSet<WeeklyStrategySnapshot> WeeklyStrategySnapshots => Set<WeeklyStrategySnapshot>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<RecommendationAlert> RecommendationAlerts => Set<RecommendationAlert>();

    public DbSet<DemoInvite> DemoInvites => Set<DemoInvite>();
    public DbSet<DemoWorkspace> DemoWorkspaces => Set<DemoWorkspace>();
    public DbSet<DemoSession> DemoSessions => Set<DemoSession>();

    public DbSet<GiveawayCodeBatch> GiveawayCodeBatches => Set<GiveawayCodeBatch>();
    public DbSet<GiveawayCode> GiveawayCodes => Set<GiveawayCode>();
    public DbSet<UserEntitlement> UserEntitlements => Set<UserEntitlement>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<PromoCodeRedemption> PromoCodeRedemptions => Set<PromoCodeRedemption>();

    public DbSet<ContactSetting> ContactSettings => Set<ContactSetting>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Map<UserAccount>(modelBuilder, "UserAccounts", "app", nameof(UserAccount.UserId));
        Map<LearningSeriesProgress>(
            modelBuilder,
            "LearningSeriesProgress",
            "app",
            nameof(WebRecomp.Web.Models.LearningSeriesProgress.LearningSeriesProgressId));
        Map<LearningInteraction>(modelBuilder, "LearningInteractions", "app", nameof(LearningInteraction.LearningInteractionId));
        Map<Client>(modelBuilder, "Clients", "app", nameof(Client.ClientId));
        Map<ClientOccupationalActivity>(
            modelBuilder,
            "ClientOccupationalActivities",
            "app",
            nameof(ClientOccupationalActivity.ClientOccupationalActivityId));
        Map<UserClientAccess>(modelBuilder, "UserClientAccess", "app", nameof(WebRecomp.Web.Models.UserClientAccess.UserClientAccessId));
        Map<ConfigVersion>(modelBuilder, "ConfigVersions", "config", nameof(ConfigVersion.ConfigVersionId));
        Map<PlanDefinition>(modelBuilder, "PlanDefinitions", "config", nameof(PlanDefinition.PlanDefinitionId));
        Map<WorkoutTypeDefinition>(modelBuilder, "WorkoutTypeDefinitions", "config", nameof(WorkoutTypeDefinition.WorkoutTypeDefinitionId));
        Map<Engine30WorkoutProfileVersion>(modelBuilder, "Engine30WorkoutProfileVersions", "config", nameof(Engine30WorkoutProfileVersion.Engine30WorkoutProfileVersionId));
        Map<GlobalSetting>(modelBuilder, "GlobalSettings", "config", nameof(GlobalSetting.GlobalSettingId));
        Map<ProgramRecord>(modelBuilder, "Programs", "app", nameof(ProgramRecord.ProgramId));
        Map<ProgramPlanChange>(modelBuilder, "ProgramPlanChanges", "app", nameof(ProgramPlanChange.ProgramPlanChangeId));
        Map<ProgramGoal>(modelBuilder, "ProgramGoals", "app", nameof(ProgramGoal.ProgramGoalId));
        Map<Measurement>(modelBuilder, "Measurements", "app", nameof(Measurement.MeasurementId));
        Map<DailyCheckin>(modelBuilder, "DailyCheckins", "app", nameof(DailyCheckin.CheckinId));
        Map<PlannedWorkout>(modelBuilder, "PlannedWorkouts", "app", nameof(PlannedWorkout.PlannedWorkoutId));
        Map<WorkoutSession>(modelBuilder, "WorkoutSessions", "app", nameof(WorkoutSession.WorkoutSessionId));
        Map<ExerciseCatalogImportRecord>(modelBuilder, "ExerciseCatalogImports", "app", nameof(ExerciseCatalogImportRecord.ExerciseCatalogImportId));
        Map<ExerciseCatalogItemRecord>(modelBuilder, "ExerciseCatalogItems", "app", nameof(ExerciseCatalogItemRecord.ExerciseCatalogItemId));
        Map<ExerciseMuscleTargetRecord>(modelBuilder, "ExerciseMuscleTargets", "app", nameof(ExerciseMuscleTargetRecord.ExerciseMuscleTargetId));
        Map<ExerciseEquipmentRequirementRecord>(modelBuilder, "ExerciseEquipmentRequirements", "app", nameof(ExerciseEquipmentRequirementRecord.ExerciseEquipmentRequirementId));
        Map<DetailedWorkoutExerciseRecord>(modelBuilder, "DetailedWorkoutExercises", "app", nameof(DetailedWorkoutExerciseRecord.DetailedWorkoutExerciseId));
        Map<DetailedWorkoutSetRecord>(modelBuilder, "DetailedWorkoutSets", "app", nameof(DetailedWorkoutSetRecord.DetailedWorkoutSetId));
        Map<ClientExercisePreferenceRecord>(modelBuilder, "ClientExercisePreferences", "app", nameof(ClientExercisePreferenceRecord.ClientExercisePreferenceId));
        Map<WorkoutTemplateRecord>(modelBuilder, "WorkoutTemplates", "app", nameof(WorkoutTemplateRecord.WorkoutTemplateId));
        Map<WorkoutTemplateExerciseRecord>(modelBuilder, "WorkoutTemplateExercises", "app", nameof(WorkoutTemplateExerciseRecord.WorkoutTemplateExerciseId));
        Map<ExerciseCatalogSyncSettingRecord>(modelBuilder, "ExerciseCatalogSyncSettings", "app", nameof(ExerciseCatalogSyncSettingRecord.ExerciseCatalogSyncSettingId));
        Map<ExerciseCatalogSyncRunRecord>(modelBuilder, "ExerciseCatalogSyncRuns", "app", nameof(ExerciseCatalogSyncRunRecord.ExerciseCatalogSyncRunId));
        Map<ExerciseMediaAssetRecord>(modelBuilder, "ExerciseMediaAssets", "app", nameof(ExerciseMediaAssetRecord.ExerciseMediaAssetId));
        Map<NutritionDay>(modelBuilder, "NutritionDays", "app", nameof(NutritionDay.NutritionDayId));
        Map<AnalysisRun>(modelBuilder, "AnalysisRuns", "app", nameof(AnalysisRun.AnalysisRunId));
        Map<WeeklyStrategySnapshot>(modelBuilder, "WeeklyStrategySnapshots", "app", nameof(WeeklyStrategySnapshot.WeeklyStrategySnapshotId));
        Map<Recommendation>(modelBuilder, "Recommendations", "app", nameof(Recommendation.RecommendationId));
        Map<RecommendationAlert>(modelBuilder, "RecommendationAlerts", "app", nameof(RecommendationAlert.RecommendationAlertId));

        Map<DemoInvite>(modelBuilder, "DemoInvites", "app", nameof(DemoInvite.DemoInviteId));
        Map<DemoWorkspace>(modelBuilder, "DemoWorkspaces", "app", nameof(DemoWorkspace.DemoWorkspaceId));
        Map<DemoSession>(modelBuilder, "DemoSessions", "app", nameof(DemoSession.DemoSessionId));

        Map<GiveawayCodeBatch>(
            modelBuilder,
            "GiveawayCodeBatches",
            "app",
            nameof(GiveawayCodeBatch.GiveawayCodeBatchId));

        Map<GiveawayCode>(
            modelBuilder,
            "GiveawayCodes",
            "app",
            nameof(GiveawayCode.GiveawayCodeId));

        Map<UserEntitlement>(
            modelBuilder,
            "UserEntitlements",
            "app",
            nameof(UserEntitlement.UserEntitlementId));

        Map<PromoCode>(
            modelBuilder,
            "PromoCodes",
            "app",
            nameof(PromoCode.PromoCodeId));

        Map<PromoCodeRedemption>(
            modelBuilder,
            "PromoCodeRedemptions",
            "app",
            nameof(PromoCodeRedemption.PromoCodeRedemptionId));

        Map<ContactSetting>(
            modelBuilder,
            "ContactSettings",
            "app",
            nameof(ContactSetting.ContactSettingsId));

        Map<ContactMessage>(
            modelBuilder,
            "ContactMessages",
            "app",
            nameof(ContactMessage.ContactMessageId));

        modelBuilder.Entity<LearningCoachPreference>()
            .Property(x => x.PreferredCoachCode)
            .HasMaxLength(30)
            .IsUnicode(false);

        modelBuilder.Entity<LearningCoachPreference>()
            .HasOne<UserAccount>()
            .WithOne()
            .HasForeignKey<LearningCoachPreference>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LearningSeriesProgress>()
            .Property(x => x.SeriesCode)
            .HasMaxLength(60)
            .IsUnicode(false);

        modelBuilder.Entity<LearningSeriesProgress>()
            .Property(x => x.StatusCode)
            .HasMaxLength(20)
            .IsUnicode(false);

        modelBuilder.Entity<LearningSeriesProgress>()
            .HasIndex(x => new { x.UserId, x.SeriesCode })
            .IsUnique();

        modelBuilder.Entity<LearningSeriesProgress>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LearningInteraction>()
            .Property(x => x.ContentCode)
            .HasMaxLength(100)
            .IsUnicode(false);

        modelBuilder.Entity<LearningInteraction>()
            .Property(x => x.SeriesCode)
            .HasMaxLength(60)
            .IsUnicode(false);

        modelBuilder.Entity<LearningInteraction>()
            .Property(x => x.SurfaceCode)
            .HasMaxLength(30)
            .IsUnicode(false);

        modelBuilder.Entity<LearningInteraction>()
            .Property(x => x.InteractionCode)
            .HasMaxLength(30)
            .IsUnicode(false);

        modelBuilder.Entity<LearningInteraction>()
            .Property(x => x.TargetUrl)
            .HasMaxLength(1000);

        modelBuilder.Entity<LearningInteraction>()
            .HasIndex(x => new { x.UserId, x.OccurredAtUtc });

        modelBuilder.Entity<LearningInteraction>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Client>().Property(x => x.RowVersion).IsRowVersion();

        modelBuilder.Entity<ClientOccupationalActivity>()
            .Property(x => x.RowVersion)
            .IsRowVersion();

        modelBuilder.Entity<ClientOccupationalActivity>()
            .Property(x => x.OccupationName)
            .HasMaxLength(100);

        modelBuilder.Entity<ClientOccupationalActivity>()
            .Property(x => x.OccupationActivityCode)
            .HasMaxLength(40)
            .IsUnicode(false);

        modelBuilder.Entity<ClientOccupationalActivity>()
            .Property(x => x.TypicalHoursPerWeek)
            .HasPrecision(5, 1);
        modelBuilder.Entity<ProgramRecord>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<DailyCheckin>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<DailyCheckin>().Property(x => x.DailyActivityHours).HasPrecision(4, 1);
        modelBuilder.Entity<DailyCheckin>().Property(x => x.DailyActivityEffortCode).HasMaxLength(12).IsUnicode(false);
        modelBuilder.Entity<PlannedWorkout>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<PlannedWorkout>().Property(x => x.SystemWorkoutCode).HasMaxLength(80).IsUnicode(false);
        modelBuilder.Entity<PlannedWorkout>().Property(x => x.PlannedDurationMin).HasPrecision(8, 2);
        modelBuilder.Entity<PlannedWorkout>().Property(x => x.PlannedRpe).HasPrecision(3, 1);
        modelBuilder.Entity<PlannedWorkout>()
            .HasOne<WorkoutTemplateRecord>()
            .WithMany()
            .HasForeignKey(x => x.WorkoutTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<PlannedWorkout>()
            .HasIndex(x => x.WorkoutTemplateId);

        modelBuilder.Entity<WorkoutSession>().Property(x => x.WorkoutName).HasMaxLength(120);
        modelBuilder.Entity<WorkoutSession>().Property(x => x.SystemWorkoutCode).HasMaxLength(80).IsUnicode(false);
        modelBuilder.Entity<WorkoutSession>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<ExerciseCatalogImportRecord>().Property(x => x.SourceCode).HasMaxLength(40).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogImportRecord>().Property(x => x.LicenseCode).HasMaxLength(40).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogImportRecord>().Property(x => x.SourceVersion).HasMaxLength(200);
        modelBuilder.Entity<ExerciseCatalogImportRecord>().Property(x => x.ManifestSha256).HasMaxLength(64).IsUnicode(false);

        modelBuilder.Entity<ExerciseCatalogItemRecord>().Property(x => x.SourceCode).HasMaxLength(40).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogItemRecord>().Property(x => x.SourceExternalId).HasMaxLength(100);
        modelBuilder.Entity<ExerciseCatalogItemRecord>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<ExerciseCatalogItemRecord>().Property(x => x.MeasurementType).HasMaxLength(30).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogItemRecord>().Property(x => x.DifficultyLevel).HasMaxLength(50).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogItemRecord>().Property(x => x.ForceType).HasMaxLength(30).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogItemRecord>().Property(x => x.Mechanics).HasMaxLength(30).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogItemRecord>().Property(x => x.Category).HasMaxLength(60).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogItemRecord>().HasIndex(x => new { x.SourceCode, x.SourceExternalId }).IsUnique();
        modelBuilder.Entity<ExerciseCatalogItemRecord>().HasIndex(x => new { x.IsActive, x.Name });

        modelBuilder.Entity<ExerciseMuscleTargetRecord>().Property(x => x.ExternalId).HasMaxLength(100);
        modelBuilder.Entity<ExerciseMuscleTargetRecord>().Property(x => x.MuscleSlug).HasMaxLength(100).IsUnicode(false);
        modelBuilder.Entity<ExerciseMuscleTargetRecord>().Property(x => x.MuscleName).HasMaxLength(150);
        modelBuilder.Entity<ExerciseMuscleTargetRecord>().Property(x => x.RoleCode).HasMaxLength(30).IsUnicode(false);

        modelBuilder.Entity<ExerciseEquipmentRequirementRecord>().Property(x => x.ExternalId).HasMaxLength(100);
        modelBuilder.Entity<ExerciseEquipmentRequirementRecord>().Property(x => x.EquipmentName).HasMaxLength(150);
        modelBuilder.Entity<ExerciseEquipmentRequirementRecord>().Property(x => x.EquipmentType).HasMaxLength(80).IsUnicode(false);
        modelBuilder.Entity<ExerciseEquipmentRequirementRecord>().Property(x => x.UsageType).HasMaxLength(80).IsUnicode(false);

        modelBuilder.Entity<DetailedWorkoutExerciseRecord>().Property(x => x.ExerciseNameSnapshot).HasMaxLength(200);
        modelBuilder.Entity<DetailedWorkoutExerciseRecord>().Property(x => x.MechanicsSnapshot).HasMaxLength(30).IsUnicode(false);
        modelBuilder.Entity<DetailedWorkoutExerciseRecord>().Property(x => x.Notes).HasMaxLength(1000);
        modelBuilder.Entity<DetailedWorkoutExerciseRecord>().Property(x => x.MaxWeight).HasPrecision(10, 2);
        modelBuilder.Entity<DetailedWorkoutExerciseRecord>().Property(x => x.LoadUnitCode).HasMaxLength(10).IsUnicode(false);

        modelBuilder.Entity<WorkoutTemplateRecord>().Property(x => x.TemplateName).HasMaxLength(120);
        modelBuilder.Entity<WorkoutTemplateRecord>().Property(x => x.WorkoutTypeCode).HasMaxLength(50).IsUnicode(false);
        modelBuilder.Entity<WorkoutTemplateRecord>().Property(x => x.DefaultDurationMin).HasPrecision(8, 2);
        modelBuilder.Entity<WorkoutTemplateRecord>().Property(x => x.Notes).HasMaxLength(1000);
        modelBuilder.Entity<WorkoutTemplateRecord>()
            .HasIndex(x => new { x.ClientId, x.TemplateName }).IsUnique();

        modelBuilder.Entity<WorkoutTemplateExerciseRecord>().Property(x => x.ExerciseNameSnapshot).HasMaxLength(200);
        modelBuilder.Entity<WorkoutTemplateExerciseRecord>().Property(x => x.MechanicsSnapshot).HasMaxLength(30).IsUnicode(false);
        // B2.35.16 TEMPLATE PRESCRIPTION TEXT
        modelBuilder.Entity<WorkoutTemplateExerciseRecord>().Property(x => x.SetsPrescription).HasMaxLength(100);
        modelBuilder.Entity<WorkoutTemplateExerciseRecord>().Property(x => x.RepsPrescription).HasMaxLength(100);
        modelBuilder.Entity<WorkoutTemplateExerciseRecord>().Property(x => x.WeightPrescription).HasMaxLength(100);
        modelBuilder.Entity<WorkoutTemplateExerciseRecord>().Property(x => x.MaxWeight).HasPrecision(10, 2);
        modelBuilder.Entity<WorkoutTemplateExerciseRecord>().Property(x => x.LoadUnitCode).HasMaxLength(10).IsUnicode(false);
        modelBuilder.Entity<WorkoutTemplateExerciseRecord>().Property(x => x.Notes).HasMaxLength(1000);
        modelBuilder.Entity<WorkoutTemplateExerciseRecord>()
            .HasOne<WorkoutTemplateRecord>()
            .WithMany(x => x.Exercises)
            .HasForeignKey(x => x.WorkoutTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WorkoutTemplateExerciseRecord>()
            .HasOne<ExerciseCatalogItemRecord>()
            .WithMany()
            .HasForeignKey(x => x.ExerciseCatalogItemId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<WorkoutTemplateExerciseRecord>()
            .HasIndex(x => new { x.WorkoutTemplateId, x.ExerciseOrder }).IsUnique();

        modelBuilder.Entity<WorkoutSession>()
            .HasOne<WorkoutTemplateRecord>()
            .WithMany()
            .HasForeignKey(x => x.WorkoutTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ClientExercisePreferenceRecord>().Property(x => x.DefaultMaxWeight).HasPrecision(10, 2);
        modelBuilder.Entity<ClientExercisePreferenceRecord>().Property(x => x.LoadUnitCode).HasMaxLength(10).IsUnicode(false);
        modelBuilder.Entity<ClientExercisePreferenceRecord>()
            .HasIndex(x => new { x.ClientId, x.ExerciseCatalogItemId }).IsUnique();
        modelBuilder.Entity<ClientExercisePreferenceRecord>()
            .HasIndex(x => new { x.ClientId, x.IsFavorite });
        modelBuilder.Entity<ClientExercisePreferenceRecord>()
            .HasOne<ExerciseCatalogItemRecord>().WithMany()
            .HasForeignKey(x => x.ExerciseCatalogItemId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DetailedWorkoutSetRecord>().Property(x => x.Load).HasPrecision(10, 2);
        modelBuilder.Entity<DetailedWorkoutSetRecord>().Property(x => x.LoadUnitCode).HasMaxLength(10).IsUnicode(false);
        modelBuilder.Entity<DetailedWorkoutSetRecord>().Property(x => x.Rpe).HasPrecision(3, 1);
        modelBuilder.Entity<DetailedWorkoutSetRecord>().Property(x => x.Rir).HasPrecision(3, 1);
        modelBuilder.Entity<DetailedWorkoutSetRecord>().Property(x => x.Notes).HasMaxLength(500);

        modelBuilder.Entity<ExerciseCatalogItemRecord>()
            .HasOne<ExerciseCatalogImportRecord>().WithMany()
            .HasForeignKey(x => x.LastImportId).OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ExerciseMuscleTargetRecord>()
            .HasOne(x => x.Exercise).WithMany(x => x.MuscleTargets)
            .HasForeignKey(x => x.ExerciseCatalogItemId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExerciseEquipmentRequirementRecord>()
            .HasOne(x => x.Exercise).WithMany(x => x.Equipment)
            .HasForeignKey(x => x.ExerciseCatalogItemId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DetailedWorkoutExerciseRecord>()
            .HasOne<WorkoutSession>().WithMany()
            .HasForeignKey(x => x.WorkoutSessionId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DetailedWorkoutExerciseRecord>()
            .HasOne<ExerciseCatalogItemRecord>().WithMany()
            .HasForeignKey(x => x.ExerciseCatalogItemId).OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DetailedWorkoutExerciseRecord>()
            .HasIndex(x => new { x.WorkoutSessionId, x.ExerciseOrder }).IsUnique();

        modelBuilder.Entity<DetailedWorkoutSetRecord>()
            .HasOne<DetailedWorkoutExerciseRecord>().WithMany(x => x.Sets)
            .HasForeignKey(x => x.DetailedWorkoutExerciseId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DetailedWorkoutSetRecord>()
            .HasIndex(x => new { x.DetailedWorkoutExerciseId, x.SetOrder }).IsUnique();
        modelBuilder.Entity<Engine30WorkoutProfileVersion>()
            .HasIndex(x => new
            {
                x.WorkoutTypeDefinitionId,
                x.VersionNumber
            })
            .IsUnique();

        modelBuilder.Entity<Engine30WorkoutProfileVersion>()
            .HasIndex(x => new
            {
                x.WorkoutTypeDefinitionId,
                x.IsActive
            });

        modelBuilder.Entity<Engine30WorkoutProfileVersion>()
            .Property(x => x.VersionCode)
            .HasMaxLength(80);

        modelBuilder.Entity<Engine30WorkoutProfileVersion>()
            .Property(x => x.CreatedBy)
            .HasMaxLength(256);

        modelBuilder.Entity<Engine30WorkoutProfileVersion>()
            .Property(x => x.ChangeNotes)
            .HasMaxLength(1000);

        modelBuilder.Entity<Engine30WorkoutProfileVersion>()
            .HasOne<WorkoutTypeDefinition>()
            .WithMany()
            .HasForeignKey(x => x.WorkoutTypeDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<ExerciseCatalogSyncSettingRecord>().Property(x => x.SettingsKey).HasMaxLength(32);
        modelBuilder.Entity<ExerciseCatalogSyncSettingRecord>().Property(x => x.ActiveProviderCode).HasMaxLength(40).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogSyncSettingRecord>().Property(x => x.MediaProviderCode).HasMaxLength(40).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogSyncSettingRecord>().Property(x => x.LastDatasetSha256).HasMaxLength(64).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogSyncSettingRecord>().Property(x => x.LastProviderVersion).HasMaxLength(200);
        modelBuilder.Entity<ExerciseCatalogSyncSettingRecord>().Property(x => x.LastProviderEtag).HasMaxLength(300);
        modelBuilder.Entity<ExerciseCatalogSyncSettingRecord>().Property(x => x.LastError).HasMaxLength(4000);
        modelBuilder.Entity<ExerciseCatalogSyncSettingRecord>().HasIndex(x => x.SettingsKey).IsUnique();

        modelBuilder.Entity<ExerciseCatalogSyncRunRecord>().Property(x => x.ProviderCode).HasMaxLength(40).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogSyncRunRecord>().Property(x => x.TriggerCode).HasMaxLength(20).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogSyncRunRecord>().Property(x => x.StatusCode).HasMaxLength(20).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogSyncRunRecord>().Property(x => x.DatasetSha256).HasMaxLength(64).IsUnicode(false);
        modelBuilder.Entity<ExerciseCatalogSyncRunRecord>().Property(x => x.SourceVersion).HasMaxLength(200);
        modelBuilder.Entity<ExerciseCatalogSyncRunRecord>().Property(x => x.ErrorMessage).HasMaxLength(4000);
        modelBuilder.Entity<ExerciseCatalogSyncRunRecord>().HasIndex(x => x.StartedAtUtc);

        modelBuilder.Entity<ExerciseMediaAssetRecord>().Property(x => x.ProviderCode).HasMaxLength(40).IsUnicode(false);
        modelBuilder.Entity<ExerciseMediaAssetRecord>().Property(x => x.SourceExternalId).HasMaxLength(200);
        modelBuilder.Entity<ExerciseMediaAssetRecord>().Property(x => x.MediaTypeCode).HasMaxLength(30).IsUnicode(false);
        modelBuilder.Entity<ExerciseMediaAssetRecord>().Property(x => x.MediaUrl).HasMaxLength(2000);
        modelBuilder.Entity<ExerciseMediaAssetRecord>().Property(x => x.LocalPath).HasMaxLength(1000);
        modelBuilder.Entity<ExerciseMediaAssetRecord>().Property(x => x.LicenseCode).HasMaxLength(80);
        modelBuilder.Entity<ExerciseMediaAssetRecord>().Property(x => x.Attribution).HasMaxLength(1000);
        modelBuilder.Entity<ExerciseMediaAssetRecord>()
            .HasOne(x => x.Exercise)
            .WithMany()
            .HasForeignKey(x => x.ExerciseCatalogItemId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ExerciseMediaAssetRecord>()
            .HasIndex(x => new { x.ExerciseCatalogItemId, x.ProviderCode, x.SourceExternalId })
            .IsUnique();

        modelBuilder.Entity<ExerciseCatalogSyncSettingRecord>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<NutritionDay>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<DemoInvite>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<GiveawayCode>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<UserEntitlement>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<PromoCode>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<ContactSetting>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<ContactMessage>().Property(x => x.RowVersion).IsRowVersion();

        modelBuilder.Entity<Client>().Property(x => x.HeightIn).HasPrecision(5, 2);

        modelBuilder.Entity<Client>()
            .Property(x => x.LifestyleActivityCode)
            .HasMaxLength(20)
            .IsUnicode(false);
        modelBuilder.Entity<Client>().Property(x => x.BaselineSleepHours).HasPrecision(4, 2);
        modelBuilder.Entity<Measurement>().Property(x => x.BodyFatPct).HasPrecision(7, 5);
        modelBuilder.Entity<Measurement>().Property(x => x.SubcutaneousFatPct).HasPrecision(7, 5);
        modelBuilder.Entity<WorkoutSession>().Property(x => x.SessionRpe).HasPrecision(3, 1);

        modelBuilder.Entity<ClientOccupationalActivity>()
            .HasIndex(x => new
            {
                x.ClientId,
                x.EffectiveFrom
            });

        modelBuilder.Entity<ClientOccupationalActivity>()
            .HasIndex(x => new
            {
                x.ClientId,
                x.OccupationActivityCode
            });

        modelBuilder.Entity<UserAccount>()
            .HasIndex(x => x.ExternalSubject)
            .IsUnique();

        modelBuilder.Entity<ProgramPlanChange>()
            .HasIndex(x => new { x.ProgramId, x.EffectiveDate });

        modelBuilder.Entity<DailyCheckin>()
            .HasIndex(x => new { x.ProgramId, x.CheckinDate })
            .IsUnique();

        modelBuilder.Entity<NutritionDay>()
            .HasIndex(x => new { x.ProgramId, x.NutritionDate })
            .IsUnique();

        modelBuilder.Entity<PlannedWorkout>()
            .HasIndex(x => new { x.ProgramId, x.PlannedDate })
            .IsUnique();

        modelBuilder.Entity<DemoInvite>()
            .HasIndex(x => x.InviteCode)
            .IsUnique();

        modelBuilder.Entity<DemoInvite>()
            .HasIndex(x => new { x.StatusCode, x.ExpiresAtUtc });

        modelBuilder.Entity<DemoWorkspace>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        modelBuilder.Entity<DemoWorkspace>()
            .HasIndex(x => new { x.WorkspaceStatusCode, x.LastActivityAtUtc });

        modelBuilder.Entity<DemoWorkspace>()
            .HasIndex(x => new { x.DemoInviteId, x.WorkspaceStatusCode });

        modelBuilder.Entity<DemoSession>()
            .HasIndex(x => x.SessionTokenHash)
            .IsUnique();

        modelBuilder.Entity<DemoSession>()
            .HasIndex(x => new { x.DemoWorkspaceId, x.RevokedAtUtc, x.ExpiresAtUtc });

        modelBuilder.Entity<DemoInvite>().Property(x => x.InviteCode).HasMaxLength(96);
        modelBuilder.Entity<DemoInvite>().Property(x => x.InviteTypeCode).HasMaxLength(16);
        modelBuilder.Entity<DemoInvite>().Property(x => x.GuidedScenarioCode).HasMaxLength(64);
        modelBuilder.Entity<DemoInvite>().Property(x => x.StatusCode).HasMaxLength(16);
        modelBuilder.Entity<DemoInvite>().Property(x => x.Notes).HasMaxLength(1000);

        modelBuilder.Entity<DemoWorkspace>().Property(x => x.DemoTypeCode).HasMaxLength(16);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.WorkspaceStatusCode).HasMaxLength(16);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.RecoveryCodeHash).HasMaxLength(128);

        modelBuilder.Entity<DemoSession>().Property(x => x.SessionTokenHash).HasMaxLength(128);

        modelBuilder.Entity<DemoWorkspace>().Property(x => x.UtmSource).HasMaxLength(100);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.UtmMedium).HasMaxLength(100);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.UtmCampaign).HasMaxLength(150);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.UtmContent).HasMaxLength(150);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.ReferrerHost).HasMaxLength(255);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.ReferrerPath).HasMaxLength(500);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.LandingPath).HasMaxLength(500);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.CountryCode).HasMaxLength(2);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.RegionName).HasMaxLength(100);
        modelBuilder.Entity<DemoWorkspace>().Property(x => x.CityName).HasMaxLength(100);

        modelBuilder.Entity<DemoSession>().Property(x => x.IpAddress).HasMaxLength(64);
        modelBuilder.Entity<DemoSession>().Property(x => x.DeviceTypeCode).HasMaxLength(16);
        modelBuilder.Entity<DemoSession>().Property(x => x.OperatingSystem).HasMaxLength(64);
        modelBuilder.Entity<DemoSession>().Property(x => x.BrowserName).HasMaxLength(64);
        modelBuilder.Entity<DemoSession>().Property(x => x.UserAgent).HasMaxLength(1000);
        modelBuilder.Entity<DemoSession>().Property(x => x.LastPagePath).HasMaxLength(500);

        modelBuilder.Entity<Recommendation>()
            .HasOne<AnalysisRun>()
            .WithMany()
            .HasForeignKey(x => x.AnalysisRunId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<WeeklyStrategySnapshot>()
            .HasOne<AnalysisRun>()
            .WithMany()
            .HasForeignKey(x => x.AnalysisRunId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<RecommendationAlert>()
            .HasOne<Recommendation>()
            .WithMany()
            .HasForeignKey(x => x.RecommendationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DemoWorkspace>()
            .HasOne<DemoInvite>()
            .WithMany()
            .HasForeignKey(x => x.DemoInviteId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DemoWorkspace>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DemoWorkspace>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.ConvertedToUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DemoInvite>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<DemoSession>()
            .HasOne<DemoWorkspace>()
            .WithMany()
            .HasForeignKey(x => x.DemoWorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GiveawayCodeBatch>()
            .Property(x => x.BatchName)
            .HasMaxLength(120);

        modelBuilder.Entity<GiveawayCodeBatch>()
            .Property(x => x.Notes)
            .HasMaxLength(1000);

        modelBuilder.Entity<GiveawayCode>()
            .Property(x => x.Code)
            .HasMaxLength(64);

        modelBuilder.Entity<GiveawayCode>()
            .Property(x => x.StatusCode)
            .HasMaxLength(16);

        modelBuilder.Entity<UserEntitlement>()
            .Property(x => x.EntitlementTypeCode)
            .HasMaxLength(32);

        modelBuilder.Entity<UserEntitlement>()
            .Property(x => x.SourceTypeCode)
            .HasMaxLength(32);

        modelBuilder.Entity<GiveawayCodeBatch>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<GiveawayCode>()
            .HasOne<GiveawayCodeBatch>()
            .WithMany()
            .HasForeignKey(x => x.GiveawayCodeBatchId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<GiveawayCode>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.RedeemedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<GiveawayCode>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.RevokedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<UserEntitlement>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<GiveawayCode>()
            .HasIndex(x => x.Code)
            .IsUnique();

        modelBuilder.Entity<GiveawayCode>()
            .HasIndex(x => new { x.StatusCode, x.RedeemByUtc });

        modelBuilder.Entity<UserEntitlement>()
            .HasIndex(x => new
            {
                x.UserId,
                x.StartsAtUtc,
                x.EndsAtUtc,
                x.RevokedAtUtc
            });
        modelBuilder.Entity<PromoCode>()
            .Property(x => x.PromotionName)
            .HasMaxLength(120);

        modelBuilder.Entity<PromoCode>()
            .Property(x => x.Code)
            .HasMaxLength(64);

        modelBuilder.Entity<PromoCode>()
            .Property(x => x.StatusCode)
            .HasMaxLength(16);

        modelBuilder.Entity<PromoCode>()
            .Property(x => x.Notes)
            .HasMaxLength(1000);

        modelBuilder.Entity<PromoCode>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<PromoCodeRedemption>()
            .HasOne<PromoCode>()
            .WithMany()
            .HasForeignKey(x => x.PromoCodeId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<PromoCodeRedemption>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<PromoCodeRedemption>()
            .HasOne<UserEntitlement>()
            .WithMany()
            .HasForeignKey(x => x.UserEntitlementId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<PromoCode>()
            .HasIndex(x => x.Code)
            .IsUnique();

        modelBuilder.Entity<PromoCode>()
            .HasIndex(x => new
            {
                x.StatusCode,
                x.RedeemByUtc
            });

        modelBuilder.Entity<PromoCodeRedemption>()
            .HasIndex(x => new
            {
                x.PromoCodeId,
                x.UserId
            })
            .IsUnique();
        modelBuilder.Entity<ContactSetting>()
            .Property(x => x.SettingsKey)
            .HasMaxLength(32);

        modelBuilder.Entity<ContactSetting>()
            .Property(x => x.DestinationEmail)
            .HasMaxLength(320);

        modelBuilder.Entity<ContactMessage>()
            .Property(x => x.SenderName)
            .HasMaxLength(120);

        modelBuilder.Entity<ContactMessage>()
            .Property(x => x.SenderEmail)
            .HasMaxLength(320);

        modelBuilder.Entity<ContactMessage>()
            .Property(x => x.Subject)
            .HasMaxLength(200);

        modelBuilder.Entity<ContactMessage>()
            .Property(x => x.StatusCode)
            .HasMaxLength(16);

        modelBuilder.Entity<ContactMessage>()
            .Property(x => x.EmailNotificationStatus)
            .HasMaxLength(24);

        modelBuilder.Entity<ContactMessage>()
            .Property(x => x.EmailError)
            .HasMaxLength(2000);

        modelBuilder.Entity<ContactSetting>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ContactMessage>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.ReadByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ContactMessage>()
            .HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.ArchivedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ContactSetting>()
            .HasIndex(x => x.SettingsKey)
            .IsUnique();

        modelBuilder.Entity<ContactMessage>()
            .HasIndex(x => new
            {
                x.StatusCode,
                x.SubmittedAtUtc
            });

        modelBuilder.Entity<ContactMessage>()
            .HasIndex(x => new
            {
                x.EmailNotificationStatus,
                x.SubmittedAtUtc
            });
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
        // Migration 030 uses manifest_sha256. Apply this override AFTER the
        // global snake_case convention so EF does not generate manifest_sha_256.
        modelBuilder.Entity<ExerciseCatalogImportRecord>()
            .Property(x => x.ManifestSha256)
            .HasColumnName("manifest_sha256");
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var previous = i > 0 ? value[i - 1] : '\0';

            if (i > 0 &&
                (char.IsUpper(c) ||
                 (char.IsDigit(c) && !char.IsDigit(previous) && previous != '_')))
            {
                chars.Add('_');
            }

            chars.Add(char.ToLowerInvariant(c));
        }

        return new string(chars.ToArray());
    }

    private static void Map<TEntity>(
        ModelBuilder modelBuilder,
        string table,
        string schema,
        string keyProperty)
        where TEntity : class
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.ToTable(table, schema);
        entity.HasKey(keyProperty);
    }
}


