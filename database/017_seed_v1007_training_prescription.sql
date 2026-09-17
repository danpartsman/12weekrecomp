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
    WHERE version_code = 'CFG-1007.0';

    IF @ConfigVersionId IS NULL
        THROW 51000, 'CFG-1007.0 does not exist.', 1;

    IF @StatusCode <> 'DRAFT'
        THROW 51001, 'CFG-1007.0 must be DRAFT before Training Prescription settings can be seeded.', 1;

    DECLARE @Settings TABLE
    (
        setting_code varchar(80) NOT NULL,
        decimal_value decimal(18,6) NOT NULL,
        unit_code varchar(30) NULL,
        description nvarchar(1200) NOT NULL
    );

    INSERT @Settings
        (setting_code, decimal_value, unit_code, description)
    VALUES
        ('ENGINE_TRAINING_SET_EXECUTION_SEC',
         35, 'seconds',
         N'Estimated time spent actually performing one resistance-training working set.'),

        ('ENGINE_TRAINING_SET_SETUP_RESET_SEC',
         15, 'seconds',
         N'Average setup, positioning, loading and reset time associated with each resistance-training working set.'),

        ('ENGINE_TRAINING_HEAVY_WARMUP_MIN',
         6, 'minutes',
         N'Warm-up time reserved before the main working volume of a major strength session.'),

        ('ENGINE_TRAINING_LIGHT_WARMUP_MIN',
         5, 'minutes',
         N'Warm-up time reserved before the main working volume of a lighter strength session.'),

        ('ENGINE_TRAINING_HEAVY_TRANSITION_PCT',
         0.10, 'ratio',
         N'Fraction of a major strength session reserved for exercise changes, equipment setup and other transitions.'),

        ('ENGINE_TRAINING_LIGHT_TRANSITION_PCT',
         0.10, 'ratio',
         N'Fraction of a lighter strength session reserved for exercise changes, equipment setup and other transitions.'),

        ('ENGINE_TRAINING_HEAVY_VOLUME_FACTOR',
         1.00, 'factor',
         N'Calibration factor applied to calculated working-set capacity for a major strength prescription.'),

        ('ENGINE_TRAINING_LIGHT_VOLUME_FACTOR',
         0.75, 'factor',
         N'Calibration factor applied to calculated working-set capacity for a lighter strength prescription.'),

        ('ENGINE_TRAINING_HEAVY_RPE_MIN',
         7, 'RPE',
         N'Lower end of the recommended working-set Rate of Perceived Exertion range for major strength sessions.'),

        ('ENGINE_TRAINING_HEAVY_RPE_MAX',
         8, 'RPE',
         N'Upper end of the recommended working-set Rate of Perceived Exertion range for major strength sessions.'),

        ('ENGINE_TRAINING_LIGHT_RPE_MIN',
         5, 'RPE',
         N'Lower end of the recommended working-set Rate of Perceived Exertion range for lighter strength sessions.'),

        ('ENGINE_TRAINING_LIGHT_RPE_MAX',
         7, 'RPE',
         N'Upper end of the recommended working-set Rate of Perceived Exertion range for lighter strength sessions.'),

        ('ENGINE_ZONE2_RPE_MIN',
         3, 'RPE',
         N'Lower end of the whole-session Rate of Perceived Exertion range for Zone 2 aerobic work.'),

        ('ENGINE_ZONE2_RPE_MAX',
         4, 'RPE',
         N'Upper end of the whole-session Rate of Perceived Exertion range for Zone 2 aerobic work.'),

        ('ENGINE_CONDITIONING_RPE_MIN',
         7, 'RPE',
         N'Lower end of the whole-session Rate of Perceived Exertion range for hard conditioning.'),

        ('ENGINE_CONDITIONING_RPE_MAX',
         9, 'RPE',
         N'Upper end of the whole-session Rate of Perceived Exertion range for hard conditioning.');

    INSERT config.GlobalSettings
    (
        config_version_id,
        setting_code,
        decimal_value,
        text_value,
        unit_code,
        description
    )
    SELECT
        @ConfigVersionId,
        s.setting_code,
        s.decimal_value,
        NULL,
        s.unit_code,
        s.description
    FROM @Settings s
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM config.GlobalSettings existing
        WHERE existing.config_version_id = @ConfigVersionId
          AND existing.setting_code = s.setting_code
    );

    COMMIT TRANSACTION;

    SELECT
        cv.version_code,
        cv.status_code,
        COUNT(gs.global_setting_id) AS total_settings
    FROM config.ConfigVersions cv
    LEFT JOIN config.GlobalSettings gs
        ON gs.config_version_id = cv.config_version_id
    WHERE cv.config_version_id = @ConfigVersionId
    GROUP BY
        cv.version_code,
        cv.status_code;

    SELECT
        setting_code,
        decimal_value,
        unit_code
    FROM config.GlobalSettings
    WHERE config_version_id = @ConfigVersionId
      AND
      (
          setting_code LIKE 'ENGINE_TRAINING_%'
          OR setting_code LIKE 'ENGINE_ZONE2_%'
          OR setting_code LIKE 'ENGINE_CONDITIONING_%'
      )
    ORDER BY setting_code;

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
