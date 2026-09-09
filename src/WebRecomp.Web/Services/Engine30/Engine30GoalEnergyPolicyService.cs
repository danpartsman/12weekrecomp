using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Services.Engine30;

public sealed record Engine30CategoryEnergyPolicy(
    string WorkoutCategoryCode,
    decimal TargetMaintenanceFraction,
    decimal MinimumMaintenanceFraction,
    decimal MaximumMaintenanceFraction,
    int ProtectionPriority,
    decimal CarryoverCorrectionCapKcal);

public sealed record Engine30GoalEnergyPolicy(
    string ConfigVersionCode,
    string PlanCode,
    string PlanDisplayName,
    decimal SurplusRecoveryFraction,
    IReadOnlyDictionary<string, Engine30CategoryEnergyPolicy> Categories)
{
    public Engine30CategoryEnergyPolicy Category(string categoryCode)
    {
        var normalized =
            Engine30RecompEnergyPolicyAllocator.NormalizeCategory(categoryCode);

        return Categories.TryGetValue(normalized, out var policy)
            ? policy
            : throw new KeyNotFoundException(
                $"Engine 3.0 goal energy policy '{PlanCode}' does not contain category '{normalized}'.");
    }
}

/// <summary>
/// Reads and writes the administrator-controlled Engine 3.0 recomp energy policy.
/// A policy is keyed by recomp PLAN and broad workout CATEGORY, never by an
/// individual workout preset.
///
/// Storage intentionally lives in CFG-3000.0 and is isolated from frozen
/// Engine 2.0 / CFG-1008 behavior.
/// </summary>
public sealed class Engine30GoalEnergyPolicyService(
    RecompDbContext db)
{
    public const string ConfigVersionCode = "CFG-3000.0";

    public async Task<IReadOnlyList<Engine30GoalEnergyPolicy>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        var rows =
            new List<Row>();

        await WithConnectionAsync(
            async connection =>
            {
                using var command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    SELECT
                        [plan_code],
                        [plan_display_name],
                        [workout_category_code],
                        [target_maintenance_fraction],
                        [minimum_maintenance_fraction],
                        [maximum_maintenance_fraction],
                        [protection_priority],
                        [carryover_correction_cap_kcal],
                        [surplus_recovery_fraction]
                    FROM [config].[Engine30GoalEnergyPolicies]
                    WHERE [config_version_code] = @config
                    ORDER BY [plan_code], [protection_priority] DESC;
                    """;

                AddParameter(
                    command,
                    "@config",
                    ConfigVersionCode);

                using var reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                while (
                    await reader.ReadAsync(
                        cancellationToken))
                {
                    rows.Add(
                        new Row(
                            PlanCode:
                                reader.GetString(0),
                            PlanDisplayName:
                                reader.GetString(1),
                            WorkoutCategoryCode:
                                reader.GetString(2),
                            TargetMaintenanceFraction:
                                reader.GetDecimal(3),
                            MinimumMaintenanceFraction:
                                reader.GetDecimal(4),
                            MaximumMaintenanceFraction:
                                reader.GetDecimal(5),
                            ProtectionPriority:
                                reader.GetInt16(6),
                            CarryoverCorrectionCapKcal:
                                reader.GetDecimal(7),
                            SurplusRecoveryFraction:
                                reader.GetDecimal(8)));
                }
            },
            cancellationToken);

        return rows
            .GroupBy(
                x => x.PlanCode,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first =
                    group.First();

                var surplusFractions =
                    group
                        .Select(x => x.SurplusRecoveryFraction)
                        .Distinct()
                        .ToArray();

                if (surplusFractions.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"Engine 3.0 goal energy policy '{group.Key}' has inconsistent surplus-recovery fractions.");
                }

                var categories =
                    group.ToDictionary(
                        x =>
                            Engine30RecompEnergyPolicyAllocator.NormalizeCategory(
                                x.WorkoutCategoryCode),
                        x =>
                            new Engine30CategoryEnergyPolicy(
                                WorkoutCategoryCode:
                                    Engine30RecompEnergyPolicyAllocator.NormalizeCategory(
                                        x.WorkoutCategoryCode),
                                TargetMaintenanceFraction:
                                    x.TargetMaintenanceFraction,
                                MinimumMaintenanceFraction:
                                    x.MinimumMaintenanceFraction,
                                MaximumMaintenanceFraction:
                                    x.MaximumMaintenanceFraction,
                                ProtectionPriority:
                                    x.ProtectionPriority,
                                CarryoverCorrectionCapKcal:
                                    x.CarryoverCorrectionCapKcal),
                        StringComparer.OrdinalIgnoreCase);

                var policy =
                    new Engine30GoalEnergyPolicy(
                        ConfigVersionCode:
                            ConfigVersionCode,
                        PlanCode:
                            first.PlanCode,
                        PlanDisplayName:
                            first.PlanDisplayName,
                        SurplusRecoveryFraction:
                            first.SurplusRecoveryFraction,
                        Categories:
                            categories);

                Validate(policy);
                return policy;
            })
            .OrderBy(x => x.PlanCode)
            .ToArray();
    }

    public async Task<Engine30GoalEnergyPolicy> LoadAsync(
        string planCode,
        CancellationToken cancellationToken = default)
    {
        var normalized =
            Engine30RecompEnergyPolicyAllocator.NormalizePlan(
                planCode);

        var all =
            await LoadAllAsync(
                cancellationToken);

        return all.SingleOrDefault(
                   x => x.PlanCode.Equals(
                       normalized,
                       StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException(
                   $"Engine 3.0 goal energy policy '{normalized}' was not found in {ConfigVersionCode}.");
    }

    public async Task SaveAsync(
        Engine30GoalEnergyPolicy policy,
        CancellationToken cancellationToken = default)
    {
        Validate(policy);

        await WithConnectionAsync(
            async connection =>
            {
                await using var transaction =
                    await connection.BeginTransactionAsync(
                        cancellationToken);

                try
                {
                    foreach (
                        var category in
                        policy.Categories.Values)
                    {
                        using var command =
                            connection.CreateCommand();

                        command.Transaction =
                            transaction;

                        command.CommandText =
                            """
                            UPDATE [config].[Engine30GoalEnergyPolicies]
                            SET
                                [plan_display_name] = @display,
                                [target_maintenance_fraction] = @target,
                                [minimum_maintenance_fraction] = @minimum,
                                [maximum_maintenance_fraction] = @maximum,
                                [protection_priority] = @priority,
                                [carryover_correction_cap_kcal] = @carryoverCap,
                                [surplus_recovery_fraction] = @surplusFraction,
                                [updated_at_utc] = SYSUTCDATETIME()
                            WHERE
                                [config_version_code] = @config
                                AND [plan_code] = @plan
                                AND [workout_category_code] = @category;

                            IF @@ROWCOUNT = 0
                                THROW 51000, 'Engine 3.0 goal energy policy row was not found.', 1;
                            """;

                        AddParameter(
                            command,
                            "@display",
                            policy.PlanDisplayName);

                        AddParameter(
                            command,
                            "@target",
                            category.TargetMaintenanceFraction);

                        AddParameter(
                            command,
                            "@minimum",
                            category.MinimumMaintenanceFraction);

                        AddParameter(
                            command,
                            "@maximum",
                            category.MaximumMaintenanceFraction);

                        AddParameter(
                            command,
                            "@priority",
                            category.ProtectionPriority);

                        AddParameter(
                            command,
                            "@carryoverCap",
                            category.CarryoverCorrectionCapKcal);

                        AddParameter(
                            command,
                            "@surplusFraction",
                            policy.SurplusRecoveryFraction);

                        AddParameter(
                            command,
                            "@config",
                            ConfigVersionCode);

                        AddParameter(
                            command,
                            "@plan",
                            policy.PlanCode);

                        AddParameter(
                            command,
                            "@category",
                            category.WorkoutCategoryCode);

                        await command.ExecuteNonQueryAsync(
                            cancellationToken);
                    }

                    await transaction.CommitAsync(
                        cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    throw;
                }
            },
            cancellationToken);
    }

    public static void Validate(
        Engine30GoalEnergyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var normalizedPlan =
            Engine30RecompEnergyPolicyAllocator.NormalizePlan(
                policy.PlanCode);

        if (!normalizedPlan.Equals(
                policy.PlanCode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "PlanCode must already be normalized.");
        }

        if (string.IsNullOrWhiteSpace(
                policy.PlanDisplayName))
        {
            throw new ArgumentException(
                "Plan display name is required.");
        }

        if (
            policy.SurplusRecoveryFraction < 0m
            || policy.SurplusRecoveryFraction > 1m
        ) {
            throw new ArgumentOutOfRangeException(
                nameof(policy.SurplusRecoveryFraction));
        }

        var expected =
            new[]
            {
                Engine30PrescriptionCategoryCodes.MajorStrength,
                Engine30PrescriptionCategoryCodes.AccessoryStrength,
                Engine30PrescriptionCategoryCodes.ConditioningCardio,
                Engine30PrescriptionCategoryCodes.RestRecoveryOther
            };

        foreach (var categoryCode in expected)
        {
            if (!policy.Categories.TryGetValue(
                    categoryCode,
                    out var category))
            {
                throw new ArgumentException(
                    $"Policy '{policy.PlanCode}' is missing '{categoryCode}'.");
            }

            if (
                category.MinimumMaintenanceFraction < 0.50m
                || category.MaximumMaintenanceFraction > 1.20m
                || category.MinimumMaintenanceFraction
                    > category.TargetMaintenanceFraction
                || category.TargetMaintenanceFraction
                    > category.MaximumMaintenanceFraction
            ) {
                throw new ArgumentException(
                    $"Invalid maintenance band for {policy.PlanCode}/{categoryCode}.");
            }

            if (
                category.ProtectionPriority < 1
                || category.ProtectionPriority > 100
            ) {
                throw new ArgumentOutOfRangeException(
                    nameof(category.ProtectionPriority));
            }

            if (
                category.CarryoverCorrectionCapKcal < 0m
                || category.CarryoverCorrectionCapKcal > 1000m
            ) {
                throw new ArgumentOutOfRangeException(
                    nameof(category.CarryoverCorrectionCapKcal));
            }
        }
    }

    private async Task WithConnectionAsync(
        Func<DbConnection, Task> action,
        CancellationToken cancellationToken)
    {
        var connection =
            db.Database.GetDbConnection();

        var shouldClose =
            connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(
                cancellationToken);
        }

        try
        {
            await action(connection);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        var parameter =
            command.CreateParameter();

        parameter.ParameterName =
            name;

        parameter.Value =
            value;

        command.Parameters.Add(
            parameter);
    }

    private sealed record Row(
        string PlanCode,
        string PlanDisplayName,
        string WorkoutCategoryCode,
        decimal TargetMaintenanceFraction,
        decimal MinimumMaintenanceFraction,
        decimal MaximumMaintenanceFraction,
        int ProtectionPriority,
        decimal CarryoverCorrectionCapKcal,
        decimal SurplusRecoveryFraction);
}
