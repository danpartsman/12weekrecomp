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
        THROW 51001, 'CFG-1007.0 must be DRAFT before model calibration settings can be seeded.', 1;

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
        ('ENGINE_RPE_INTENSITY_BASE',
         0.70, 'factor',
         N'Base value in the RPE-to-workout-intensity calculation.'),

        ('ENGINE_RPE_INTENSITY_PER_POINT',
         0.04, 'factor/RPE',
         N'Increase in modeled workout intensity for each reported RPE point.'),

        ('ENGINE_RPE_INTENSITY_MIN',
         0.70, 'factor',
         N'Minimum RPE-derived workout intensity factor.'),

        ('ENGINE_RPE_INTENSITY_MAX',
         1.20, 'factor',
         N'Maximum RPE-derived workout intensity factor.'),

        ('ENGINE_DENSITY_MIN_REST_SEC',
         15, 'seconds',
         N'Minimum rest interval accepted by the workout-density calculation.'),

        ('ENGINE_DENSITY_FACTOR_MIN',
         0.75, 'factor',
         N'Minimum workout-density adjustment factor.'),

        ('ENGINE_DENSITY_FACTOR_MAX',
         1.25, 'factor',
         N'Maximum workout-density adjustment factor.'),

        ('ENGINE_DURATION_FACTOR_MIN',
         0.50, 'factor',
         N'Minimum duration adjustment applied to calculated regional training load.'),

        ('ENGINE_DURATION_FACTOR_MAX',
         1.50, 'factor',
         N'Maximum duration adjustment applied to calculated regional training load.'),

        ('ENGINE_LOAD_RPE_FACTOR_MIN',
         0.75, 'factor',
         N'Minimum RPE adjustment applied to calculated regional training load.'),

        ('ENGINE_LOAD_RPE_FACTOR_MAX',
         1.25, 'factor',
         N'Maximum RPE adjustment applied to calculated regional training load.');

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
      AND setting_code IN
      (
          'ENGINE_RPE_INTENSITY_BASE',
          'ENGINE_RPE_INTENSITY_PER_POINT',
          'ENGINE_RPE_INTENSITY_MIN',
          'ENGINE_RPE_INTENSITY_MAX',
          'ENGINE_DENSITY_MIN_REST_SEC',
          'ENGINE_DENSITY_FACTOR_MIN',
          'ENGINE_DENSITY_FACTOR_MAX',
          'ENGINE_DURATION_FACTOR_MIN',
          'ENGINE_DURATION_FACTOR_MAX',
          'ENGINE_LOAD_RPE_FACTOR_MIN',
          'ENGINE_LOAD_RPE_FACTOR_MAX'
      )
    ORDER BY setting_code;

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
