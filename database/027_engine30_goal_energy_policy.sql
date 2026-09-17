SET XACT_ABORT ON;
SET NOCOUNT ON;

IF SCHEMA_ID(N'config') IS NULL
    EXEC(N'CREATE SCHEMA [config]');

IF OBJECT_ID(N'[config].[Engine30GoalEnergyPolicies]', N'U') IS NULL
BEGIN
    CREATE TABLE [config].[Engine30GoalEnergyPolicies]
    (
        [engine30_goal_energy_policy_id] uniqueidentifier NOT NULL
            CONSTRAINT [DF_Engine30GoalEnergyPolicies_Id]
            DEFAULT NEWID(),

        [config_version_code] nvarchar(30) NOT NULL,
        [plan_code] nvarchar(30) NOT NULL,
        [plan_display_name] nvarchar(120) NOT NULL,
        [workout_category_code] nvarchar(40) NOT NULL,

        [target_maintenance_fraction] decimal(6,4) NOT NULL,
        [minimum_maintenance_fraction] decimal(6,4) NOT NULL,
        [maximum_maintenance_fraction] decimal(6,4) NOT NULL,

        [protection_priority] smallint NOT NULL,
        [carryover_correction_cap_kcal] decimal(8,2) NOT NULL,
        [surplus_recovery_fraction] decimal(6,4) NOT NULL,

        [updated_at_utc] datetime2(0) NOT NULL
            CONSTRAINT [DF_Engine30GoalEnergyPolicies_Updated]
            DEFAULT SYSUTCDATETIME(),

        CONSTRAINT [PK_Engine30GoalEnergyPolicies]
            PRIMARY KEY ([engine30_goal_energy_policy_id]),

        CONSTRAINT [UQ_Engine30GoalEnergyPolicies_ConfigPlanCategory]
            UNIQUE
            (
                [config_version_code],
                [plan_code],
                [workout_category_code]
            ),

        CONSTRAINT [CK_Engine30GoalEnergyPolicies_Fractions]
            CHECK
            (
                [minimum_maintenance_fraction] >= 0.5000
                AND [maximum_maintenance_fraction] <= 1.2000
                AND [minimum_maintenance_fraction]
                    <= [target_maintenance_fraction]
                AND [target_maintenance_fraction]
                    <= [maximum_maintenance_fraction]
            ),

        CONSTRAINT [CK_Engine30GoalEnergyPolicies_Priority]
            CHECK ([protection_priority] BETWEEN 1 AND 100),

        CONSTRAINT [CK_Engine30GoalEnergyPolicies_CarryoverCap]
            CHECK ([carryover_correction_cap_kcal] BETWEEN 0 AND 1000),

        CONSTRAINT [CK_Engine30GoalEnergyPolicies_SurplusFraction]
            CHECK ([surplus_recovery_fraction] BETWEEN 0 AND 1)
    );
END;

DECLARE @Seed TABLE
(
    plan_code nvarchar(30),
    plan_display_name nvarchar(120),
    workout_category_code nvarchar(40),
    target_fraction decimal(6,4),
    minimum_fraction decimal(6,4),
    maximum_fraction decimal(6,4),
    protection_priority smallint,
    carryover_cap_kcal decimal(8,2),
    surplus_recovery_fraction decimal(6,4)
);

INSERT @Seed
VALUES
-- PLAN 1 - Health Reset
(N'PLAN_1', N'Health Reset', N'MAJOR_STRENGTH',       0.9600, 0.9200, 1.0000, 95,  50, 0.6500),
(N'PLAN_1', N'Health Reset', N'ACCESSORY_STRENGTH',   0.9000, 0.8300, 0.9600, 70, 125, 0.6500),
(N'PLAN_1', N'Health Reset', N'CONDITIONING_CARDIO',  0.8700, 0.7800, 0.9400, 55, 175, 0.6500),
(N'PLAN_1', N'Health Reset', N'REST_RECOVERY_OTHER',  0.8000, 0.7000, 0.9000, 35, 250, 0.6500),

-- PLAN 2 - Fat-Loss Dominant Recomp
-- Major lifting is explicitly protected. Lower-demand days are allowed
-- to carry more of the weekly deficit, subject to physiological macro floors.
(N'PLAN_2', N'Fat-Loss Dominant Recomp', N'MAJOR_STRENGTH',       0.9800, 0.9500, 1.0000, 100,   0, 0.7500),
(N'PLAN_2', N'Fat-Loss Dominant Recomp', N'ACCESSORY_STRENGTH',   0.8600, 0.7800, 0.9300,  65, 150, 0.7500),
(N'PLAN_2', N'Fat-Loss Dominant Recomp', N'CONDITIONING_CARDIO',  0.8200, 0.7200, 0.9000,  50, 200, 0.7500),
(N'PLAN_2', N'Fat-Loss Dominant Recomp', N'REST_RECOVERY_OTHER',  0.7400, 0.6500, 0.8400,  25, 300, 0.7500),

-- PLAN 3 - Muscle-Building Recomp
(N'PLAN_3', N'Muscle-Building Recomp', N'MAJOR_STRENGTH',       1.0000, 0.9800, 1.0300, 100,   0, 0.5000),
(N'PLAN_3', N'Muscle-Building Recomp', N'ACCESSORY_STRENGTH',   0.9500, 0.9000, 1.0000,  75,  75, 0.5000),
(N'PLAN_3', N'Muscle-Building Recomp', N'CONDITIONING_CARDIO',  0.9000, 0.8400, 0.9700,  55, 125, 0.5000),
(N'PLAN_3', N'Muscle-Building Recomp', N'REST_RECOVERY_OTHER',  0.8400, 0.7800, 0.9200,  35, 200, 0.5000),

-- PLAN 4 - Athletic Conditioning Recomp
(N'PLAN_4', N'Athletic Conditioning Recomp', N'MAJOR_STRENGTH',       0.9800, 0.9400, 1.0100, 95,  25, 0.6500),
(N'PLAN_4', N'Athletic Conditioning Recomp', N'ACCESSORY_STRENGTH',   0.9000, 0.8400, 0.9700, 65, 100, 0.6500),
(N'PLAN_4', N'Athletic Conditioning Recomp', N'CONDITIONING_CARDIO',  0.9500, 0.9000, 1.0000, 90,  75, 0.6500),
(N'PLAN_4', N'Athletic Conditioning Recomp', N'REST_RECOVERY_OTHER',  0.7800, 0.7000, 0.8800, 30, 250, 0.6500),

-- PLAN 5 - Pain-Limited / Mobility Recomp
(N'PLAN_5', N'Pain-Limited / Mobility Recomp', N'MAJOR_STRENGTH',       0.9500, 0.9000, 1.0000, 80,  50, 0.5000),
(N'PLAN_5', N'Pain-Limited / Mobility Recomp', N'ACCESSORY_STRENGTH',   0.9300, 0.8800, 0.9800, 90,  75, 0.5000),
(N'PLAN_5', N'Pain-Limited / Mobility Recomp', N'CONDITIONING_CARDIO',  0.8800, 0.8200, 0.9400, 60, 100, 0.5000),
(N'PLAN_5', N'Pain-Limited / Mobility Recomp', N'REST_RECOVERY_OTHER',  0.8500, 0.7800, 0.9200, 50, 150, 0.5000);

INSERT [config].[Engine30GoalEnergyPolicies]
(
    [engine30_goal_energy_policy_id],
    [config_version_code],
    [plan_code],
    [plan_display_name],
    [workout_category_code],
    [target_maintenance_fraction],
    [minimum_maintenance_fraction],
    [maximum_maintenance_fraction],
    [protection_priority],
    [carryover_correction_cap_kcal],
    [surplus_recovery_fraction],
    [updated_at_utc]
)
SELECT
    NEWID(),
    N'CFG-3000.0',
    s.plan_code,
    s.plan_display_name,
    s.workout_category_code,
    s.target_fraction,
    s.minimum_fraction,
    s.maximum_fraction,
    s.protection_priority,
    s.carryover_cap_kcal,
    s.surplus_recovery_fraction,
    SYSUTCDATETIME()
FROM @Seed s
WHERE NOT EXISTS
(
    SELECT 1
    FROM [config].[Engine30GoalEnergyPolicies] p
    WHERE
        p.[config_version_code] = N'CFG-3000.0'
        AND p.[plan_code] = s.plan_code
        AND p.[workout_category_code] = s.workout_category_code
);

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
WHERE [config_version_code] = N'CFG-3000.0'
ORDER BY [plan_code], [protection_priority] DESC;
