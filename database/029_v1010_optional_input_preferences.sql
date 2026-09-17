SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/*
    12WR TEST v1010 optional detailed-input preference foundation.

    Additive only.
    - Existing/manual behavior remains OFF/OFF by default.
    - Preferences are program-scoped, not account-scoped.
    - No detailed data is created or deleted by this migration.
*/

IF COL_LENGTH('app.Programs', 'detailed_workout_entry_enabled') IS NULL
BEGIN
    ALTER TABLE app.Programs
        ADD detailed_workout_entry_enabled bit NOT NULL
            CONSTRAINT DF_Programs_DetailedWorkoutEntry DEFAULT 0;
END;
GO

IF COL_LENGTH('app.Programs', 'nutrition_food_log_enabled') IS NULL
BEGIN
    ALTER TABLE app.Programs
        ADD nutrition_food_log_enabled bit NOT NULL
            CONSTRAINT DF_Programs_NutritionFoodLog DEFAULT 0;
END;
GO

PRINT 'v1010 optional-input preference foundation applied successfully.';
GO
