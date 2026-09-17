using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Data;

public sealed class RecompDbContext(DbContextOptions<RecompDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<UserClientAccess> UserClientAccess => Set<UserClientAccess>();
    public DbSet<ConfigVersion> ConfigVersions => Set<ConfigVersion>();
    public DbSet<PlanDefinition> PlanDefinitions => Set<PlanDefinition>();
    public DbSet<WorkoutTypeDefinition> WorkoutTypeDefinitions => Set<WorkoutTypeDefinition>();
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<ProgramRecord> Programs => Set<ProgramRecord>();
    public DbSet<ProgramPlanChange> ProgramPlanChanges => Set<ProgramPlanChange>();
    public DbSet<ProgramGoal> ProgramGoals => Set<ProgramGoal>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<DailyCheckin> DailyCheckins => Set<DailyCheckin>();
    public DbSet<PlannedWorkout> PlannedWorkouts => Set<PlannedWorkout>();
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();
    public DbSet<NutritionDay> NutritionDays => Set<NutritionDay>();
    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();
    public DbSet<WeeklyStrategySnapshot> WeeklyStrategySnapshots => Set<WeeklyStrategySnapshot>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<RecommendationAlert> RecommendationAlerts => Set<RecommendationAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Map<UserAccount>(modelBuilder, "UserAccounts", "app", nameof(UserAccount.UserId));
        Map<Client>(modelBuilder, "Clients", "app", nameof(Client.ClientId));
        Map<UserClientAccess>(modelBuilder, "UserClientAccess", "app", nameof(WebRecomp.Web.Models.UserClientAccess.UserClientAccessId));
        Map<ConfigVersion>(modelBuilder, "ConfigVersions", "config", nameof(ConfigVersion.ConfigVersionId));
        Map<PlanDefinition>(modelBuilder, "PlanDefinitions", "config", nameof(PlanDefinition.PlanDefinitionId));
        Map<WorkoutTypeDefinition>(modelBuilder, "WorkoutTypeDefinitions", "config", nameof(WorkoutTypeDefinition.WorkoutTypeDefinitionId));
        Map<GlobalSetting>(modelBuilder, "GlobalSettings", "config", nameof(GlobalSetting.GlobalSettingId));
        Map<ProgramRecord>(modelBuilder, "Programs", "app", nameof(ProgramRecord.ProgramId));
        Map<ProgramPlanChange>(modelBuilder, "ProgramPlanChanges", "app", nameof(ProgramPlanChange.ProgramPlanChangeId));
        Map<ProgramGoal>(modelBuilder, "ProgramGoals", "app", nameof(ProgramGoal.ProgramGoalId));
        Map<Measurement>(modelBuilder, "Measurements", "app", nameof(Measurement.MeasurementId));
        Map<DailyCheckin>(modelBuilder, "DailyCheckins", "app", nameof(DailyCheckin.CheckinId));
        Map<PlannedWorkout>(modelBuilder, "PlannedWorkouts", "app", nameof(PlannedWorkout.PlannedWorkoutId));
        Map<WorkoutSession>(modelBuilder, "WorkoutSessions", "app", nameof(WorkoutSession.WorkoutSessionId));
        Map<NutritionDay>(modelBuilder, "NutritionDays", "app", nameof(NutritionDay.NutritionDayId));
        Map<AnalysisRun>(modelBuilder, "AnalysisRuns", "app", nameof(AnalysisRun.AnalysisRunId));
        Map<WeeklyStrategySnapshot>(modelBuilder, "WeeklyStrategySnapshots", "app", nameof(WeeklyStrategySnapshot.WeeklyStrategySnapshotId));
        Map<Recommendation>(modelBuilder, "Recommendations", "app", nameof(Recommendation.RecommendationId));
        Map<RecommendationAlert>(modelBuilder, "RecommendationAlerts", "app", nameof(RecommendationAlert.RecommendationAlertId));

        modelBuilder.Entity<Client>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<ProgramRecord>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<DailyCheckin>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<PlannedWorkout>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<WorkoutSession>().Property(x => x.RowVersion).IsRowVersion();
        modelBuilder.Entity<NutritionDay>().Property(x => x.RowVersion).IsRowVersion();

        modelBuilder.Entity<Client>().Property(x => x.HeightIn).HasPrecision(5, 2);
        modelBuilder.Entity<Client>().Property(x => x.BaselineSleepHours).HasPrecision(4, 2);

        modelBuilder.Entity<Measurement>().Property(x => x.BodyFatPct).HasPrecision(7, 5);
        modelBuilder.Entity<Measurement>().Property(x => x.SubcutaneousFatPct).HasPrecision(7, 5);

        modelBuilder.Entity<WorkoutSession>().Property(x => x.SessionRpe).HasPrecision(3, 1);

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

        // Model the recommendation graph foreign keys so EF Core knows the required
        // insert order. This is especially important when a recommendation creates
        // one or more RecommendationAlerts (for example, pain/illness overrides).
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

        // The SQL scripts deliberately use snake_case column names.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
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
