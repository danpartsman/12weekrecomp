SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO

---------------------------------------------------------------
-- ENGINE 3.0 CONFIGURATION
--
-- This is deliberately separate from CFG-1008.0.
---------------------------------------------------------------

IF NOT EXISTS
(
    SELECT 1
    FROM [config].[ConfigVersions]
    WHERE [version_code] = 'CFG-3000.0'
)
BEGIN

    INSERT INTO [config].[ConfigVersions]
    (
        [config_version_id],
        [version_code],
        [status_code],
        [description],
        [published_at_utc],
        [effective_from_utc],
        [created_at_utc]
    )
    VALUES
    (
        NEWID(),
        'CFG-3000.0',
        'DRAFT',
        'Engine 3.0 shadow configuration and workout calibration catalog.',
        NULL,
        NULL,
        SYSUTCDATETIME()
    );

    PRINT 'CREATED: CFG-3000.0';

END
ELSE
BEGIN

    PRINT 'EXISTS: CFG-3000.0';

END;
GO


DECLARE @ConfigVersionId uniqueidentifier;

SELECT
    @ConfigVersionId = [config_version_id]
FROM
    [config].[ConfigVersions]
WHERE
    [version_code] = 'CFG-3000.0';

IF @ConfigVersionId IS NULL
BEGIN
    THROW 52001, 'CFG-3000.0 could not be resolved.', 1;
END;


---------------------------------------------------------------
-- OFFICIAL ENGINE 3.0 PRESET CATALOG
---------------------------------------------------------------

DECLARE @Catalog TABLE
(
    workout_type_code       varchar(80)   NOT NULL,
    display_name            nvarchar(160) NOT NULL,
    workout_category_code   varchar(40)   NOT NULL,
    display_order           smallint      NOT NULL
);

INSERT INTO @Catalog
(
    workout_type_code,
    display_name,
    workout_category_code,
    display_order
)
VALUES

-- Recovery
('REST',
 'Rest',
 'REST_RECOVERY_OTHER',
 10),

('MOBILITY_RECOVERY',
 'Mobility / Recovery',
 'REST_RECOVERY_OTHER',
 20),

-- Continuous Cardio
('GENERAL_CARDIO',
 'General Cardio',
 'CONDITIONING_CARDIO',
 30),

('EASY_CARDIO',
 'Easy Cardio',
 'CONDITIONING_CARDIO',
 40),

('ZONE2_CARDIO',
 'Zone 2 Cardio',
 'CONDITIONING_CARDIO',
 50),

('WALKING',
 'Walking',
 'CONDITIONING_CARDIO',
 60),

('RUNNING',
 'Running',
 'CONDITIONING_CARDIO',
 70),

('TREADMILL',
 'Treadmill',
 'CONDITIONING_CARDIO',
 80),

('CYCLING',
 'Cycling',
 'CONDITIONING_CARDIO',
 90),

('ROWING',
 'Rowing',
 'CONDITIONING_CARDIO',
 100),

('ELLIPTICAL',
 'Elliptical',
 'CONDITIONING_CARDIO',
 110),

('STAIR_CLIMBER',
 'Stair Climber',
 'CONDITIONING_CARDIO',
 120),

('SWIMMING',
 'Swimming',
 'CONDITIONING_CARDIO',
 130),

-- Resistance
('MAJOR_LEGS',
 'Major Legs',
 'MAJOR_STRENGTH',
 140),

('MAJOR_PUSH',
 'Major Push',
 'MAJOR_STRENGTH',
 150),

('MAJOR_PULL',
 'Major Pull',
 'MAJOR_STRENGTH',
 160),

('FULL_BODY_STRENGTH',
 'Full-Body Strength',
 'MAJOR_STRENGTH',
 170),

('ACCESSORY',
 'Accessory / Isolation',
 'ACCESSORY_STRENGTH',
 180),

('RESISTANCE_CIRCUIT',
 'Resistance Circuit',
 'ACCESSORY_STRENGTH',
 190),

-- Intervals
('RUNNING_INTERVALS',
 'Running Intervals',
 'CONDITIONING_CARDIO',
 200),

('CYCLING_INTERVALS',
 'Cycling Intervals',
 'CONDITIONING_CARDIO',
 210),

('ROWING_INTERVALS',
 'Rowing Intervals',
 'CONDITIONING_CARDIO',
 220),

('MIXED_CONDITIONING',
 'Mixed Conditioning',
 'CONDITIONING_CARDIO',
 230),

('TRUE_HIIT',
 'HIIT / Sprint Intervals',
 'CONDITIONING_CARDIO',
 240),

-- Composite
('ORANGETHEORY',
 'Orangetheory / Mixed Interval Class',
 'CONDITIONING_CARDIO',
 250),

('MIXED_GYM_SESSION',
 'Mixed Gym Session',
 'CONDITIONING_CARDIO',
 260),

('CUSTOM_COMPOSITE',
 'Custom Multi-Block Workout',
 'CONDITIONING_CARDIO',
 270);


---------------------------------------------------------------
-- CREATE ANY MISSING WORKOUT DEFINITION ROWS
---------------------------------------------------------------

INSERT INTO [config].[WorkoutTypeDefinitions]
(
    [workout_type_definition_id],
    [config_version_id],

    [workout_type_code],
    [display_name],

    [workout_category_code],
    [is_selectable],
    [display_order],

    [description],
    [instructions],
    [overview_video_url],

    [weekly_role_code],
    [region_code],

    [counts_dedicated_heavy],
    [counts_light_strength],
    [counts_primary_conditioning],
    [counts_recovery],

    [fuel_priority_code],
    [schedule_principle_code],
    [strategy_note],

    [macro_class_code],
    [primary_focus_code],

    [fuel_score],
    [default_minutes],
    [raw_glycogen_g_min],
    [default_intensity_factor],
    [default_rest_sec],
    [reference_workout_cal_per_min],
    [glycogen_carb_share],

    [push_load],
    [pull_load],
    [leg_load],
    [cardio_load],

    [strength_stress],
    [planned_restoration_pct],
    [protein_adjustment_g],
    [target_fat_g],
    [typical_recovery_hours],

    [planning_note],
    [is_active]
)
SELECT
    NEWID(),
    @ConfigVersionId,

    c.workout_type_code,
    c.display_name,

    c.workout_category_code,
    0,
    c.display_order,

    'Engine 3.0 shadow workout preset.',
    NULL,
    NULL,

    CASE
        WHEN c.workout_category_code = 'MAJOR_STRENGTH'
            THEN 'HEAVY'
        ELSE 'OTHER_LOAD'
    END,

    NULL,

    CASE
        WHEN c.workout_category_code = 'MAJOR_STRENGTH'
            THEN 1
        ELSE 0
    END,

    CASE
        WHEN c.workout_category_code = 'ACCESSORY_STRENGTH'
            THEN 1
        ELSE 0
    END,

    CASE
        WHEN c.workout_category_code = 'CONDITIONING_CARDIO'
            THEN 1
        ELSE 0
    END,

    CASE
        WHEN c.workout_category_code = 'REST_RECOVERY_OTHER'
            THEN 1
        ELSE 0
    END,

    CASE
        WHEN c.workout_category_code = 'MAJOR_STRENGTH'
             OR c.workout_type_code IN
             (
                'RUNNING_INTERVALS',
                'CYCLING_INTERVALS',
                'ROWING_INTERVALS',
                'MIXED_CONDITIONING',
                'TRUE_HIIT',
                'ORANGETHEORY',
                'MIXED_GYM_SESSION',
                'CUSTOM_COMPOSITE'
             )
            THEN 'HIGH'
        ELSE 'LOW'
    END,

    'SCHEDULE_NORMALLY',
    NULL,

    NULL,
    NULL,

    0,
    0,
    0,
    0,
    0,
    0,
    0,

    0,
    0,
    0,
    0,

    0,
    0,
    0,
    0,
    0,

    NULL,
    1

FROM @Catalog c

WHERE NOT EXISTS
(
    SELECT 1
    FROM [config].[WorkoutTypeDefinitions] w
    WHERE
        w.[config_version_id] = @ConfigVersionId
        AND
        w.[workout_type_code] = c.workout_type_code
);


PRINT 'Engine 3.0 workout definitions synchronized.';
GO


---------------------------------------------------------------
-- WEB APP MANAGED IDENTITY PERMISSIONS
--
-- Admin functions need to:
--   INSERT new workouts
--   UPDATE workout metadata
--   INSERT profile versions
--   UPDATE which profile version is active
---------------------------------------------------------------

IF DATABASE_PRINCIPAL_ID(N'recomp-test') IS NULL
BEGIN
    THROW 52002,
        'Database principal recomp-test was not found.',
        1;
END;
GO

GRANT SELECT, INSERT, UPDATE
ON OBJECT::[config].[WorkoutTypeDefinitions]
TO [recomp-test];
GO

GRANT SELECT, INSERT, UPDATE
ON OBJECT::[config].[Engine30WorkoutProfileVersions]
TO [recomp-test];
GO


---------------------------------------------------------------
-- VERIFY
---------------------------------------------------------------

DECLARE @Engine30ConfigId uniqueidentifier;

SELECT
    @Engine30ConfigId = [config_version_id]
FROM
    [config].[ConfigVersions]
WHERE
    [version_code] = 'CFG-3000.0';

SELECT
    [version_code],
    [status_code],
    [description]
FROM
    [config].[ConfigVersions]
WHERE
    [config_version_id] = @Engine30ConfigId;

SELECT
    COUNT(*) AS Engine30WorkoutCount
FROM
    [config].[WorkoutTypeDefinitions]
WHERE
    [config_version_id] = @Engine30ConfigId;

SELECT
    [workout_type_code],
    [display_name],
    [workout_category_code]
FROM
    [config].[WorkoutTypeDefinitions]
WHERE
    [config_version_id] = @Engine30ConfigId
ORDER BY
    [display_order];

PRINT 'PASS: CFG-3000.0 Engine 3.0 workout catalog is ready.';
GO
