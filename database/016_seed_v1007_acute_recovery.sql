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
        THROW 51001, 'CFG-1007.0 must be DRAFT before Acute Recovery settings can be seeded.', 1;

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
        ('ENGINE_SLEEP_REFERENCE_HOURS',
         7.50, 'hours',
         N'Fallback sleep reference when the client does not yet have a personal baseline sleep value.'),

        ('ENGINE_SLEEP_REDUCED_RATIO',
         0.85, 'ratio',
         N'Sleep-to-reference ratio below which acute sleep recovery is classified as reduced.'),

        ('ENGINE_SLEEP_SEVERE_RATIO',
         0.70, 'ratio',
         N'Sleep-to-reference ratio below which acute sleep recovery is classified as severely reduced.'),

        ('ENGINE_ENERGY_REDUCED_MAX',
         5, 'score',
         N'Highest 1-to-10 energy score classified as reduced acute recovery.'),

        ('ENGINE_ENERGY_SEVERE_MAX',
         3, 'score',
         N'Highest 1-to-10 energy score classified as severely reduced acute recovery.'),

        ('ENGINE_SORENESS_REDUCED_MIN',
         6, 'score',
         N'Lowest 1-to-10 soreness score classified as reduced acute recovery.'),

        ('ENGINE_SORENESS_SEVERE_MIN',
         8, 'score',
         N'Lowest 1-to-10 soreness score classified as severely reduced acute recovery.');

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
          'ENGINE_SLEEP_REFERENCE_HOURS',
          'ENGINE_SLEEP_REDUCED_RATIO',
          'ENGINE_SLEEP_SEVERE_RATIO',
          'ENGINE_ENERGY_REDUCED_MAX',
          'ENGINE_ENERGY_SEVERE_MAX',
          'ENGINE_SORENESS_REDUCED_MIN',
          'ENGINE_SORENESS_SEVERE_MIN'
      )
    ORDER BY setting_code;

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
