SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ConfigId uniqueidentifier;
    DECLARE @Status varchar(20);

    SELECT
        @ConfigId = config_version_id,
        @Status = status_code
    FROM config.ConfigVersions
    WHERE version_code = 'CFG-1008.0';

    IF @ConfigId IS NULL
        THROW 52000, 'CFG-1008.0 was not found.', 1;

    IF @Status <> 'DRAFT'
        THROW 52001, 'CFG-1008.0 must remain DRAFT while the Engine 2.1 energy model is being developed.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM app.Programs
        WHERE config_version_id = @ConfigId
    )
        THROW 52002, 'CFG-1008.0 is already assigned to a program and cannot be modified automatically.', 1;


    -------------------------------------------------------------------------
    -- Engine 2.1 dietary-fat model
    --
    -- Fat is protected but is not the primary workout-fueling variable.
    -- The minimum is body-weight based. The target percentage is evaluated
    -- against the baseline/average energy budget rather than independently
    -- inflating fat on every higher-calorie training day.
    -------------------------------------------------------------------------

    DECLARE @Settings TABLE
    (
        setting_code varchar(60) NOT NULL,
        decimal_value decimal(18,6) NOT NULL,
        unit_code varchar(30) NULL,
        description nvarchar(1000) NOT NULL
    );

    INSERT @Settings
    (
        setting_code,
        decimal_value,
        unit_code,
        description
    )
    VALUES
    (
        'FAT_MIN_G_PER_KG',
        0.500000,
        'G_PER_KG',
        N'Engine 2.1 evidence-backed dietary-fat floor. Fat grams should not normally fall below 0.5 g/kg body weight.'
    ),
    (
        'FAT_TARGET_ENERGY_PCT',
        0.200000,
        'RATIO',
        N'Engine 2.1 baseline dietary-fat target. Approximately 20 percent of baseline energy, subject to the body-weight fat floor. Higher training-day energy is preferentially allocated through carbohydrate.'
    ),
    (
        'FAT_ROUNDING_G',
        5.000000,
        'GRAMS',
        N'Engine 2.1 user-facing dietary-fat target rounding increment.'
    ),
    (
        'ENERGY_ROUNDING_KCAL',
        10.000000,
        'KCAL',
        N'Engine 2.1 user-facing calorie target rounding increment.'
    ),

    -------------------------------------------------------------------------
    -- Relative energy-periodization weights.
    --
    -- These are distribution weights, NOT direct TDEE multipliers.
    -- They will be normalized across the actual weekly schedule so that
    -- periodization does not accidentally change the intended weekly
    -- energy budget.
    -------------------------------------------------------------------------

    (
        'ENERGY_WEIGHT_MAJOR_STRENGTH',
        1.100000,
        'RATIO',
        N'Relative Engine 2.1 energy-allocation weight for Major Strength. Used within normalized weekly energy periodization; not multiplied directly against TDEE.'
    ),
    (
        'ENERGY_WEIGHT_ACCESSORY_STRENGTH',
        1.000000,
        'RATIO',
        N'Relative Engine 2.1 energy-allocation weight for Accessory Strength.'
    ),
    (
        'ENERGY_WEIGHT_CONDITIONING_CARDIO',
        0.950000,
        'RATIO',
        N'Relative Engine 2.1 energy-allocation weight for Conditioning/Cardio. Actual physiological cost can override the category starting point.'
    ),
    (
        'ENERGY_WEIGHT_REST_RECOVERY_OTHER',
        0.800000,
        'RATIO',
        N'Relative Engine 2.1 energy-allocation weight for Rest, Recovery and low-demand Other days.'
    );


    -------------------------------------------------------------------------
    -- Update existing Engine 2.1 settings when rerun.
    -------------------------------------------------------------------------

    UPDATE target
    SET
        target.decimal_value = source.decimal_value,
        target.text_value = NULL,
        target.unit_code = source.unit_code,
        target.description = source.description
    FROM config.GlobalSettings target
    INNER JOIN @Settings source
        ON source.setting_code = target.setting_code
    WHERE target.config_version_id = @ConfigId;


    -------------------------------------------------------------------------
    -- Insert settings not already present.
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
        @ConfigId,
        source.setting_code,
        source.decimal_value,
        NULL,
        source.unit_code,
        source.description
    FROM @Settings source
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM config.GlobalSettings target
        WHERE target.config_version_id = @ConfigId
          AND target.setting_code = source.setting_code
    );


    COMMIT TRANSACTION;


    -------------------------------------------------------------------------
    -- Verification
    -------------------------------------------------------------------------

    SELECT
        cv.version_code,
        cv.status_code,
        gs.setting_code,
        gs.decimal_value,
        gs.unit_code,
        gs.description
    FROM config.ConfigVersions cv
    INNER JOIN config.GlobalSettings gs
        ON gs.config_version_id = cv.config_version_id
    WHERE cv.version_code = 'CFG-1008.0'
      AND
      (
          gs.setting_code LIKE 'ENERGY_%'
          OR gs.setting_code LIKE 'FAT_%'
      )
    ORDER BY gs.setting_code;

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
