SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @ConfigVersionId uniqueidentifier;
DECLARE @StatusCode varchar(30);

SELECT
    @ConfigVersionId = config_version_id,
    @StatusCode = status_code
FROM config.ConfigVersions
WHERE version_code = 'CFG-1008.0';

IF @ConfigVersionId IS NULL
    THROW 50001, 'CFG-1008.0 was not found.', 1;

IF @StatusCode <> 'DRAFT'
    THROW 50002, 'CFG-1008.0 must be DRAFT.', 1;


DECLARE @Settings TABLE
(
    setting_code varchar(100) NOT NULL,
    decimal_value decimal(18,6) NOT NULL,
    unit_code varchar(30) NOT NULL,
    description nvarchar(1000) NOT NULL
);

INSERT INTO @Settings
(
    setting_code,
    decimal_value,
    unit_code,
    description
)
VALUES
(
    'ENGINE21_CATEGORY_BASE_CARB_MULT_MAJOR',
    1.000000,
    'multiplier',
    'Engine 2.1 base-rest carbohydrate multiplier for Major Strength days.'
),
(
    'ENGINE21_CATEGORY_BASE_CARB_MULT_ACCESSORY',
    1.000000,
    'multiplier',
    'Engine 2.1 base-rest carbohydrate multiplier for Accessory Strength days.'
),
(
    'ENGINE21_CATEGORY_BASE_CARB_MULT_CONDITIONING',
    1.000000,
    'multiplier',
    'Engine 2.1 base-rest carbohydrate multiplier for Conditioning and Cardio days.'
),
(
    'ENGINE21_CATEGORY_BASE_CARB_MULT_REST',
    1.000000,
    'multiplier',
    'Engine 2.1 base-rest carbohydrate multiplier for Rest, Recovery and Other days.'
);


IF EXISTS
(
    SELECT 1
    FROM config.GlobalSettings g
    JOIN @Settings s
        ON s.setting_code = g.setting_code
    WHERE g.config_version_id = @ConfigVersionId
      AND
      (
          g.decimal_value <> s.decimal_value
          OR ISNULL(g.unit_code, '') <> s.unit_code
      )
)
    THROW 50003,
        'Existing Engine 2.1 category policy differs from migration baseline.',
        1;


INSERT INTO config.GlobalSettings
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
    FROM config.GlobalSettings g
    WHERE g.config_version_id = @ConfigVersionId
      AND g.setting_code = s.setting_code
);


IF
(
    SELECT COUNT(*)
    FROM config.GlobalSettings
    WHERE config_version_id = @ConfigVersionId
      AND setting_code LIKE
          'ENGINE21_CATEGORY_BASE_CARB_MULT_%'
) <> 4
    THROW 50004,
        'Expected exactly four Engine 2.1 category settings.',
        1;


COMMIT TRANSACTION;


SELECT
    setting_code,
    decimal_value,
    unit_code
FROM config.GlobalSettings
WHERE config_version_id = @ConfigVersionId
  AND setting_code LIKE
      'ENGINE21_CATEGORY_BASE_CARB_MULT_%'
ORDER BY setting_code;
