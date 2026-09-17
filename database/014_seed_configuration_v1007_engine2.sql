SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @SourceConfigId uniqueidentifier;
DECLARE @NewConfigId uniqueidentifier;

SELECT @SourceConfigId = config_version_id
FROM config.ConfigVersions
WHERE version_code = 'CFG-1001.0';

IF @SourceConfigId IS NULL
BEGIN
    THROW 50001, 'Source configuration CFG-1001.0 was not found.', 1;
END;

SELECT @NewConfigId = config_version_id
FROM config.ConfigVersions
WHERE version_code = 'CFG-1007.0';

IF @NewConfigId IS NULL
BEGIN
    SET @NewConfigId = NEWID();

    INSERT config.ConfigVersions
    (
        config_version_id,
        version_code,
        status_code,
        description,
        published_at_utc,
        effective_from_utc
    )
    VALUES
    (
        @NewConfigId,
        'CFG-1007.0',
        'DRAFT',
        N'12WR v1007 Recomp Engine 2.0 draft configuration.',
        NULL,
        NULL
    );
END
ELSE
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM config.ConfigVersions
        WHERE config_version_id = @NewConfigId
          AND status_code = 'PUBLISHED'
    )
    BEGIN
        THROW 50002, 'CFG-1007.0 is already published and cannot be reseeded.', 1;
    END;

    DELETE FROM config.PlanDefinitions
    WHERE config_version_id = @NewConfigId;

    DELETE FROM config.WorkoutTypeDefinitions
    WHERE config_version_id = @NewConfigId;

    DELETE FROM config.GlobalSettings
    WHERE config_version_id = @NewConfigId;
END;

INSERT config.PlanDefinitions
(
    plan_definition_id,
    config_version_id,
    plan_code,
    display_name,
    base_rest_carbs_g,
    plan_category_code,
    sort_order,
    is_active
)
SELECT
    NEWID(),
    @NewConfigId,
    plan_code,
    display_name,
    base_rest_carbs_g,
    plan_category_code,
    sort_order,
    is_active
FROM config.PlanDefinitions
WHERE config_version_id = @SourceConfigId;

INSERT config.WorkoutTypeDefinitions
(
    workout_type_definition_id,
    config_version_id,
    workout_type_code,
    display_name,
    weekly_role_code,
    region_code,
    counts_dedicated_heavy,
    counts_light_strength,
    counts_primary_conditioning,
    counts_recovery,
    fuel_priority_code,
    schedule_principle_code,
    strategy_note,
    macro_class_code,
    primary_focus_code,
    fuel_score,
    default_minutes,
    raw_glycogen_g_min,
    default_intensity_factor,
    default_rest_sec,
    reference_workout_cal_per_min,
    glycogen_carb_share,
    push_load,
    pull_load,
    leg_load,
    cardio_load,
    strength_stress,
    planned_restoration_pct,
    protein_adjustment_g,
    target_fat_g,
    typical_recovery_hours,
    planning_note,
    is_active
)
SELECT
    NEWID(),
    @NewConfigId,
    workout_type_code,
    display_name,
    weekly_role_code,
    region_code,
    counts_dedicated_heavy,
    counts_light_strength,
    counts_primary_conditioning,
    counts_recovery,
    fuel_priority_code,
    schedule_principle_code,
    strategy_note,
    macro_class_code,
    primary_focus_code,
    fuel_score,
    default_minutes,
    raw_glycogen_g_min,
    default_intensity_factor,
    default_rest_sec,
    reference_workout_cal_per_min,
    glycogen_carb_share,
    push_load,
    pull_load,
    leg_load,
    cardio_load,
    strength_stress,
    planned_restoration_pct,
    protein_adjustment_g,
    target_fat_g,
    typical_recovery_hours,
    planning_note,
    is_active
FROM config.WorkoutTypeDefinitions
WHERE config_version_id = @SourceConfigId;

INSERT config.GlobalSettings
(
    global_setting_id,
    config_version_id,
    setting_code,
    decimal_value,
    text_value,
    unit_code,
    description
)
SELECT
    NEWID(),
    @NewConfigId,
    setting_code,
    decimal_value,
    text_value,
    unit_code,
    description
FROM config.GlobalSettings
WHERE config_version_id = @SourceConfigId;

INSERT config.GlobalSettings
(
    global_setting_id,
    config_version_id,
    setting_code,
    decimal_value,
    text_value,
    unit_code,
    description
)
VALUES
(NEWID(), @NewConfigId, 'ENGINE_ALLOCATION_CAP_FUEL5', 90, NULL, 'g', N'Engine 2.0 maximum daily carryover carbohydrate allocation for Fuel Score 5.'),
(NEWID(), @NewConfigId, 'ENGINE_ALLOCATION_CAP_FUEL4', 75, NULL, 'g', N'Engine 2.0 maximum daily carryover carbohydrate allocation for Fuel Score 4.'),
(NEWID(), @NewConfigId, 'ENGINE_ALLOCATION_CAP_FUEL3', 65, NULL, 'g', N'Engine 2.0 maximum daily carryover carbohydrate allocation for Fuel Score 3.'),
(NEWID(), @NewConfigId, 'ENGINE_ALLOCATION_CAP_FUEL25', 55, NULL, 'g', N'Engine 2.0 maximum daily carryover carbohydrate allocation for Fuel Score 2.5.'),
(NEWID(), @NewConfigId, 'ENGINE_ALLOCATION_CAP_FUEL2', 50, NULL, 'g', N'Engine 2.0 maximum daily carryover carbohydrate allocation for Fuel Score 2.'),
(NEWID(), @NewConfigId, 'ENGINE_ALLOCATION_CAP_FUEL1', 40, NULL, 'g', N'Engine 2.0 maximum daily carryover carbohydrate allocation for Fuel Score 1.'),
(NEWID(), @NewConfigId, 'ENGINE_ALLOCATION_CAP_DEFAULT', 35, NULL, 'g', N'Engine 2.0 fallback daily carryover carbohydrate allocation cap.'),

(NEWID(), @NewConfigId, 'ENGINE_PREWORKOUT_MIN_FUEL_SCORE', 2, NULL, 'score', N'Minimum Fuel Score eligible for an explicit Engine 2.0 pre-workout carbohydrate rescue.'),
(NEWID(), @NewConfigId, 'ENGINE_PREWORKOUT_CAP_FUEL5', 55, NULL, 'g', N'Engine 2.0 maximum carbohydrate amount credited before a Fuel Score 5 workout.'),
(NEWID(), @NewConfigId, 'ENGINE_PREWORKOUT_CAP_FUEL4', 45, NULL, 'g', N'Engine 2.0 maximum carbohydrate amount credited before a Fuel Score 4 workout.'),
(NEWID(), @NewConfigId, 'ENGINE_PREWORKOUT_CAP_FUEL3', 35, NULL, 'g', N'Engine 2.0 maximum carbohydrate amount credited before a Fuel Score 3 workout.'),
(NEWID(), @NewConfigId, 'ENGINE_PREWORKOUT_CAP_DEFAULT', 25, NULL, 'g', N'Engine 2.0 fallback maximum pre-workout carbohydrate rescue.');

COMMIT TRANSACTION;

SELECT
    version_code,
    status_code,
    description,
    published_at_utc,
    effective_from_utc
FROM config.ConfigVersions
WHERE version_code = 'CFG-1007.0';

SELECT
    setting_code,
    decimal_value,
    unit_code
FROM config.GlobalSettings
WHERE config_version_id = @NewConfigId
  AND setting_code LIKE 'ENGINE_%'
ORDER BY setting_code;
