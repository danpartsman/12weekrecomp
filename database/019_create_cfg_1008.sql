SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @SourceConfigId uniqueidentifier;
    DECLARE @TargetConfigId uniqueidentifier;
    DECLARE @TargetStatus varchar(20);

    -------------------------------------------------------------------------
    -- Source: frozen Engine 2.0 configuration
    -------------------------------------------------------------------------

    SELECT @SourceConfigId = config_version_id
    FROM config.ConfigVersions
    WHERE version_code = 'CFG-1007.0';

    IF @SourceConfigId IS NULL
        THROW 51000, 'Source configuration CFG-1007.0 was not found.', 1;

    -------------------------------------------------------------------------
    -- Target: v1008 / Engine 2.1 working configuration
    -------------------------------------------------------------------------

    SELECT
        @TargetConfigId = config_version_id,
        @TargetStatus = status_code
    FROM config.ConfigVersions
    WHERE version_code = 'CFG-1008.0';

    IF @TargetConfigId IS NULL
    BEGIN
        SET @TargetConfigId = NEWID();

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
            @TargetConfigId,
            'CFG-1008.0',
            'DRAFT',
            N'12WR v1008 Recomp Engine 2.1 Energy Periodization and administrator-managed workout catalog draft configuration.',
            NULL,
            NULL
        );
    END
    ELSE
    BEGIN
        IF @TargetStatus <> 'DRAFT'
            THROW 51001, 'CFG-1008.0 exists but is not DRAFT and cannot be reseeded.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM app.Programs
            WHERE config_version_id = @TargetConfigId
        )
            THROW 51002, 'CFG-1008.0 is already assigned to one or more programs and cannot be automatically reseeded.', 1;

        DELETE FROM config.PlanDefinitions
        WHERE config_version_id = @TargetConfigId;

        DELETE FROM config.WorkoutTypeDefinitions
        WHERE config_version_id = @TargetConfigId;

        DELETE FROM config.GlobalSettings
        WHERE config_version_id = @TargetConfigId;

        UPDATE config.ConfigVersions
        SET
            description = N'12WR v1008 Recomp Engine 2.1 Energy Periodization and administrator-managed workout catalog draft configuration.',
            published_at_utc = NULL,
            effective_from_utc = NULL
        WHERE config_version_id = @TargetConfigId;
    END;

    -------------------------------------------------------------------------
    -- Clone plan definitions
    -------------------------------------------------------------------------

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
        @TargetConfigId,
        plan_code,
        display_name,
        base_rest_carbs_g,
        plan_category_code,
        sort_order,
        is_active
    FROM config.PlanDefinitions
    WHERE config_version_id = @SourceConfigId;

    -------------------------------------------------------------------------
    -- Clone complete workout catalog, including v1008-A catalog metadata
    -------------------------------------------------------------------------

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
        is_active,

        workout_category_code,
        is_selectable,
        display_order,
        description,
        instructions,
        overview_video_url
    )
    SELECT
        NEWID(),
        @TargetConfigId,
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
        is_active,

        workout_category_code,
        is_selectable,
        display_order,
        description,
        instructions,
        overview_video_url
    FROM config.WorkoutTypeDefinitions
    WHERE config_version_id = @SourceConfigId;

    -------------------------------------------------------------------------
    -- Clone the COMPLETE Engine 2.0 settings state.
    -- This includes all additions through migration 017.
    -------------------------------------------------------------------------

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
        @TargetConfigId,
        setting_code,
        decimal_value,
        text_value,
        unit_code,
        description
    FROM config.GlobalSettings
    WHERE config_version_id = @SourceConfigId;

    -------------------------------------------------------------------------
    -- Sanity validation before commit
    -------------------------------------------------------------------------

    DECLARE @SourcePlanCount int;
    DECLARE @TargetPlanCount int;
    DECLARE @SourceWorkoutCount int;
    DECLARE @TargetWorkoutCount int;
    DECLARE @SourceSettingCount int;
    DECLARE @TargetSettingCount int;

    SELECT @SourcePlanCount = COUNT(*)
    FROM config.PlanDefinitions
    WHERE config_version_id = @SourceConfigId;

    SELECT @TargetPlanCount = COUNT(*)
    FROM config.PlanDefinitions
    WHERE config_version_id = @TargetConfigId;

    SELECT @SourceWorkoutCount = COUNT(*)
    FROM config.WorkoutTypeDefinitions
    WHERE config_version_id = @SourceConfigId;

    SELECT @TargetWorkoutCount = COUNT(*)
    FROM config.WorkoutTypeDefinitions
    WHERE config_version_id = @TargetConfigId;

    SELECT @SourceSettingCount = COUNT(*)
    FROM config.GlobalSettings
    WHERE config_version_id = @SourceConfigId;

    SELECT @TargetSettingCount = COUNT(*)
    FROM config.GlobalSettings
    WHERE config_version_id = @TargetConfigId;

    IF @SourcePlanCount <> @TargetPlanCount
        THROW 51003, 'CFG-1008.0 PlanDefinition clone count does not match CFG-1007.0.', 1;

    IF @SourceWorkoutCount <> @TargetWorkoutCount
        THROW 51004, 'CFG-1008.0 WorkoutTypeDefinition clone count does not match CFG-1007.0.', 1;

    IF @SourceSettingCount <> @TargetSettingCount
        THROW 51005, 'CFG-1008.0 GlobalSetting clone count does not match CFG-1007.0.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM config.WorkoutTypeDefinitions
        WHERE config_version_id = @TargetConfigId
          AND workout_category_code NOT IN
          (
              'MAJOR_STRENGTH',
              'ACCESSORY_STRENGTH',
              'CONDITIONING_CARDIO',
              'REST_RECOVERY_OTHER'
          )
    )
        THROW 51006, 'CFG-1008.0 contains an invalid workout category.', 1;

    COMMIT TRANSACTION;

    -------------------------------------------------------------------------
    -- Verification
    -------------------------------------------------------------------------

    SELECT
        version_code,
        status_code,
        description,
        published_at_utc,
        effective_from_utc
    FROM config.ConfigVersions
    WHERE config_version_id = @TargetConfigId;

    SELECT
        @SourcePlanCount AS source_plan_count,
        @TargetPlanCount AS target_plan_count,
        @SourceWorkoutCount AS source_workout_count,
        @TargetWorkoutCount AS target_workout_count,
        @SourceSettingCount AS source_setting_count,
        @TargetSettingCount AS target_setting_count;

    SELECT
        workout_category_code,
        COUNT(*) AS workout_count
    FROM config.WorkoutTypeDefinitions
    WHERE config_version_id = @TargetConfigId
    GROUP BY workout_category_code
    ORDER BY workout_category_code;

    SELECT
        workout_type_code,
        display_name,
        workout_category_code,
        fuel_score,
        push_load,
        pull_load,
        leg_load,
        cardio_load,
        is_active,
        is_selectable,
        display_order
    FROM config.WorkoutTypeDefinitions
    WHERE config_version_id = @TargetConfigId
    ORDER BY
        CASE workout_category_code
            WHEN 'MAJOR_STRENGTH' THEN 10
            WHEN 'ACCESSORY_STRENGTH' THEN 20
            WHEN 'CONDITIONING_CARDIO' THEN 30
            WHEN 'REST_RECOVERY_OTHER' THEN 40
            ELSE 999
        END,
        display_order,
        display_name;

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
