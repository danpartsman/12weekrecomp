SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @ConfigId uniqueidentifier;
DECLARE @StatusCode varchar(20);

SELECT
    @ConfigId = config_version_id,
    @StatusCode = status_code
FROM config.ConfigVersions
WHERE version_code = 'CFG-1008.0';

IF @ConfigId IS NULL
    THROW 51022, 'CFG-1008.0 does not exist.', 1;

IF @StatusCode <> 'DRAFT'
    THROW 51023, 'CFG-1008.0 is not DRAFT and cannot be modified.', 1;

-- Remove temporary administrator-test workout if it still exists.
DELETE FROM config.WorkoutTypeDefinitions
WHERE config_version_id = @ConfigId
  AND workout_type_code = 'SFSFFSD';

-- Heavy yardwork is meaningful conditioning/workload, not rest.
UPDATE config.WorkoutTypeDefinitions
SET workout_category_code = 'CONDITIONING_CARDIO'
WHERE config_version_id = @ConfigId
  AND workout_type_code = 'YARDWORK_HEAVY'
  AND workout_category_code <> 'CONDITIONING_CARDIO';

-- Moderate yardwork is also meaningful conditioning/workload.
UPDATE config.WorkoutTypeDefinitions
SET workout_category_code = 'CONDITIONING_CARDIO'
WHERE config_version_id = @ConfigId
  AND workout_type_code = 'YARDWORK_MODERATE'
  AND workout_category_code <> 'CONDITIONING_CARDIO';

-- Validate duplicate workout codes.
IF EXISTS
(
    SELECT workout_type_code
    FROM config.WorkoutTypeDefinitions
    WHERE config_version_id = @ConfigId
    GROUP BY workout_type_code
    HAVING COUNT(*) > 1
)
    THROW 51024, 'Duplicate workout_type_code detected in CFG-1008.0.', 1;

-- Validate category values.
IF EXISTS
(
    SELECT 1
    FROM config.WorkoutTypeDefinitions
    WHERE config_version_id = @ConfigId
      AND workout_category_code NOT IN
      (
          'MAJOR_STRENGTH',
          'ACCESSORY_STRENGTH',
          'CONDITIONING_CARDIO',
          'REST_RECOVERY_OTHER'
      )
)
    THROW 51025, 'Invalid workout category detected in CFG-1008.0.', 1;

-- A selectable workout must also remain active.
IF EXISTS
(
    SELECT 1
    FROM config.WorkoutTypeDefinitions
    WHERE config_version_id = @ConfigId
      AND is_selectable = 1
      AND is_active = 0
)
    THROW 51026, 'Selectable inactive workout detected in CFG-1008.0.', 1;

-- The cleaned CFG-1008 catalog should contain 25 active workouts.
IF
(
    SELECT COUNT(*)
    FROM config.WorkoutTypeDefinitions
    WHERE config_version_id = @ConfigId
      AND is_active = 1
) <> 25
    THROW 51027, 'CFG-1008.0 should contain 25 active workout definitions.', 1;

COMMIT TRANSACTION;
