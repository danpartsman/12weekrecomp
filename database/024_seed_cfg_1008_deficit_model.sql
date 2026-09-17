SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ConfigVersionId uniqueidentifier;
    DECLARE @StatusCode varchar(20);

    SELECT
        @ConfigVersionId = config_version_id,
        @StatusCode = status_code
    FROM config.ConfigVersions
    WHERE version_code = 'CFG-1008.0';

    IF @ConfigVersionId IS NULL
        THROW 51024, 'CFG-1008.0 was not found.', 1;

    IF @StatusCode <> 'DRAFT'
        THROW 51025, 'CFG-1008.0 must remain DRAFT while Engine 2.1 is under development.', 1;

    -------------------------------------------------------------------------
    -- Engine 2.1 deficit-selection configuration
    -------------------------------------------------------------------------

    DELETE FROM config.GlobalSettings
    WHERE config_version_id = @ConfigVersionId
      AND setting_code IN
      (
          'DEFICIT_PCT_PLAN_1',
          'DEFICIT_PCT_PLAN_2',
          'DEFICIT_PCT_PLAN_3',
          'DEFICIT_PCT_PLAN_4',
          'DEFICIT_PCT_PLAN_5',
          'DEFICIT_MAX_DAILY_KCAL',
          'DEFICIT_MAX_PCT',
          'DEFICIT_MIN_BMI'
      );

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

    (
        NEWID(),
        @ConfigVersionId,
        'DEFICIT_PCT_PLAN_1',
        0.100000,
        NULL,
        'RATIO',
        N'Engine 2.1 starting energy-deficit percentage for Health Reset. Conservative consistency-first energy restriction.'
    ),

    (
        NEWID(),
        @ConfigVersionId,
        'DEFICIT_PCT_PLAN_2',
        0.150000,
        NULL,
        'RATIO',
        N'Engine 2.1 starting energy-deficit percentage for Fat-Loss Dominant Recomp. Subject to absolute and percentage safety caps.'
    ),

    (
        NEWID(),
        @ConfigVersionId,
        'DEFICIT_PCT_PLAN_3',
        0.075000,
        NULL,
        'RATIO',
        N'Engine 2.1 starting energy-deficit percentage for Muscle-Building Recomp. Smaller deficit protects training and lean-tissue support.'
    ),

    (
        NEWID(),
        @ConfigVersionId,
        'DEFICIT_PCT_PLAN_4',
        0.100000,
        NULL,
        'RATIO',
        N'Engine 2.1 starting energy-deficit percentage for Athletic Reconditioning. Moderate deficit balances body-composition and performance goals.'
    ),

    (
        NEWID(),
        @ConfigVersionId,
        'DEFICIT_PCT_PLAN_5',
        0.100000,
        NULL,
        'RATIO',
        N'Engine 2.1 starting energy-deficit percentage for Pain-Limited or Mobility-focused Recomp. Conservative starting restriction.'
    ),

    (
        NEWID(),
        @ConfigVersionId,
        'DEFICIT_MAX_DAILY_KCAL',
        500.000000,
        NULL,
        'KCAL_PER_DAY',
        N'Engine 2.1 initial maximum average daily energy deficit for recomp-focused programming.'
    ),

    (
        NEWID(),
        @ConfigVersionId,
        'DEFICIT_MAX_PCT',
        0.200000,
        NULL,
        'RATIO',
        N'Engine 2.1 hard upper bound on starting percentage energy deficit.'
    ),

    (
        NEWID(),
        @ConfigVersionId,
        'DEFICIT_MIN_BMI',
        18.500000,
        NULL,
        'BMI',
        N'BMI safety/context threshold below which Engine 2.1 does not prescribe a weight-loss energy deficit. BMI is a screening/context value, not a body-composition diagnosis.'
    );

    COMMIT TRANSACTION;

    -------------------------------------------------------------------------
    -- Verification
    -------------------------------------------------------------------------

    SELECT
        c.version_code,
        c.status_code,
        g.setting_code,
        g.decimal_value,
        g.unit_code,
        g.description
    FROM config.GlobalSettings g
    JOIN config.ConfigVersions c
        ON c.config_version_id = g.config_version_id
    WHERE c.version_code = 'CFG-1008.0'
      AND g.setting_code LIKE 'DEFICIT_%'
    ORDER BY g.setting_code;

END TRY
BEGIN CATCH

    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;

END CATCH;
