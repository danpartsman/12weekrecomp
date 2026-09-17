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


IF
(
    SELECT COUNT(*)
    FROM config.GlobalSettings
    WHERE config_version_id = @ConfigVersionId
      AND setting_code IN
      (
          'ENGINE21_CATEGORY_BASE_CARB_MULT_MAJOR',
          'ENGINE21_CATEGORY_BASE_CARB_MULT_ACCESSORY',
          'ENGINE21_CATEGORY_BASE_CARB_MULT_CONDITIONING',
          'ENGINE21_CATEGORY_BASE_CARB_MULT_REST'
      )
) <> 4
    THROW 50003,
        'Expected exactly four Engine 2.1 category settings.',
        1;


UPDATE config.GlobalSettings
SET
    decimal_value =
        CASE setting_code
            WHEN 'ENGINE21_CATEGORY_BASE_CARB_MULT_MAJOR'
                THEN 1.100000

            WHEN 'ENGINE21_CATEGORY_BASE_CARB_MULT_ACCESSORY'
                THEN 1.000000

            WHEN 'ENGINE21_CATEGORY_BASE_CARB_MULT_CONDITIONING'
                THEN 0.950000

            WHEN 'ENGINE21_CATEGORY_BASE_CARB_MULT_REST'
                THEN 0.900000
        END,

    description =
        CASE setting_code
            WHEN 'ENGINE21_CATEGORY_BASE_CARB_MULT_MAJOR'
                THEN 'Engine 2.1 approved base-rest carbohydrate multiplier for Major Strength days.'

            WHEN 'ENGINE21_CATEGORY_BASE_CARB_MULT_ACCESSORY'
                THEN 'Engine 2.1 approved base-rest carbohydrate multiplier for Accessory Strength days.'

            WHEN 'ENGINE21_CATEGORY_BASE_CARB_MULT_CONDITIONING'
                THEN 'Engine 2.1 approved base-rest carbohydrate multiplier for Conditioning and Cardio days.'

            WHEN 'ENGINE21_CATEGORY_BASE_CARB_MULT_REST'
                THEN 'Engine 2.1 approved base-rest carbohydrate multiplier for Rest, Recovery and Other days.'
        END
WHERE config_version_id = @ConfigVersionId
  AND setting_code IN
  (
      'ENGINE21_CATEGORY_BASE_CARB_MULT_MAJOR',
      'ENGINE21_CATEGORY_BASE_CARB_MULT_ACCESSORY',
      'ENGINE21_CATEGORY_BASE_CARB_MULT_CONDITIONING',
      'ENGINE21_CATEGORY_BASE_CARB_MULT_REST'
  );

IF @@ROWCOUNT <> 4
    THROW 50004,
        'Engine 2.1 category policy update did not affect exactly four rows.',
        1;

COMMIT TRANSACTION;


SELECT
    setting_code,
    decimal_value
FROM config.GlobalSettings
WHERE config_version_id = @ConfigVersionId
  AND setting_code LIKE
      'ENGINE21_CATEGORY_BASE_CARB_MULT_%'
ORDER BY setting_code;
