SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'app') EXEC('CREATE SCHEMA app');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'config') EXEC('CREATE SCHEMA config');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'audit') EXEC('CREATE SCHEMA audit');

CREATE TABLE app.UserAccounts (
    user_id uniqueidentifier NOT NULL CONSTRAINT PK_UserAccounts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    external_subject nvarchar(200) NOT NULL,
    email_normalized nvarchar(320) NULL,
    display_name nvarchar(100) NULL,
    account_status_code varchar(20) NOT NULL CONSTRAINT DF_UserAccounts_Status DEFAULT 'ACTIVE',
    created_at_utc datetime2(3) NOT NULL CONSTRAINT DF_UserAccounts_Created DEFAULT SYSUTCDATETIME(),
    updated_at_utc datetime2(3) NOT NULL CONSTRAINT DF_UserAccounts_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_UserAccounts_ExternalSubject UNIQUE (external_subject)
);

CREATE TABLE app.Clients (
    client_id uniqueidentifier NOT NULL CONSTRAINT PK_Clients PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    client_name nvarchar(100) NOT NULL,
    date_of_birth date NULL,
    sex_code varchar(20) NULL,
    height_in decimal(5,2) NOT NULL,
    time_zone nvarchar(100) NOT NULL CONSTRAINT DF_Clients_TimeZone DEFAULT 'America/Los_Angeles',
    unit_system_code varchar(10) NOT NULL CONSTRAINT DF_Clients_Units DEFAULT 'US',
    training_experience_code varchar(20) NULL,
    baseline_steps_day int NULL,
    baseline_sleep_hours decimal(4,2) NULL,
    client_status_code varchar(20) NOT NULL CONSTRAINT DF_Clients_Status DEFAULT 'ACTIVE',
    row_version rowversion NOT NULL,
    created_at_utc datetime2(3) NOT NULL CONSTRAINT DF_Clients_Created DEFAULT SYSUTCDATETIME(),
    updated_at_utc datetime2(3) NOT NULL CONSTRAINT DF_Clients_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Clients_Height CHECK (height_in BETWEEN 48 AND 90)
);

CREATE TABLE app.UserClientAccess (
    user_client_access_id uniqueidentifier NOT NULL CONSTRAINT PK_UserClientAccess PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    user_id uniqueidentifier NOT NULL,
    client_id uniqueidentifier NOT NULL,
    access_role_code varchar(20) NOT NULL,
    granted_at_utc datetime2(3) NOT NULL CONSTRAINT DF_UserClientAccess_Granted DEFAULT SYSUTCDATETIME(),
    revoked_at_utc datetime2(3) NULL,
    CONSTRAINT FK_UserClientAccess_User FOREIGN KEY (user_id) REFERENCES app.UserAccounts(user_id),
    CONSTRAINT FK_UserClientAccess_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id)
);
CREATE UNIQUE INDEX UX_UserClientAccess_Active
ON app.UserClientAccess(user_id, client_id)
WHERE revoked_at_utc IS NULL;

CREATE TABLE config.ConfigVersions (
    config_version_id uniqueidentifier NOT NULL CONSTRAINT PK_ConfigVersions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    version_code varchar(30) NOT NULL,
    status_code varchar(20) NOT NULL,
    description nvarchar(1000) NULL,
    published_at_utc datetime2(3) NULL,
    effective_from_utc datetime2(3) NULL,
    created_at_utc datetime2(3) NOT NULL CONSTRAINT DF_ConfigVersions_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_ConfigVersions_Code UNIQUE(version_code)
);

CREATE TABLE config.PlanDefinitions (
    plan_definition_id uniqueidentifier NOT NULL CONSTRAINT PK_PlanDefinitions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    config_version_id uniqueidentifier NOT NULL,
    plan_code varchar(40) NOT NULL,
    display_name nvarchar(120) NOT NULL,
    base_rest_carbs_g decimal(7,2) NOT NULL,
    plan_category_code varchar(40) NOT NULL,
    sort_order smallint NOT NULL,
    is_active bit NOT NULL CONSTRAINT DF_PlanDefinitions_Active DEFAULT 1,
    CONSTRAINT FK_PlanDefinitions_Config FOREIGN KEY (config_version_id) REFERENCES config.ConfigVersions(config_version_id),
    CONSTRAINT UQ_PlanDefinitions_Code UNIQUE(config_version_id, plan_code)
);

CREATE TABLE config.WorkoutTypeDefinitions (
    workout_type_definition_id uniqueidentifier NOT NULL CONSTRAINT PK_WorkoutTypeDefinitions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    config_version_id uniqueidentifier NOT NULL,
    workout_type_code varchar(60) NOT NULL,
    display_name nvarchar(120) NOT NULL,

    weekly_role_code varchar(30) NOT NULL,
    region_code varchar(30) NULL,
    counts_dedicated_heavy bit NOT NULL,
    counts_light_strength bit NOT NULL,
    counts_primary_conditioning bit NOT NULL,
    counts_recovery bit NOT NULL,
    fuel_priority_code varchar(20) NOT NULL,
    schedule_principle_code varchar(30) NOT NULL,
    strategy_note nvarchar(1000) NULL,

    macro_class_code varchar(40) NULL,
    primary_focus_code varchar(60) NULL,
    fuel_score decimal(6,2) NOT NULL,
    default_minutes decimal(6,2) NOT NULL,
    raw_glycogen_g_min decimal(7,4) NOT NULL,
    default_intensity_factor decimal(7,4) NOT NULL,
    default_rest_sec smallint NOT NULL,
    reference_workout_cal_per_min decimal(7,3) NOT NULL,
    glycogen_carb_share decimal(7,5) NOT NULL,

    push_load decimal(7,3) NOT NULL,
    pull_load decimal(7,3) NOT NULL,
    leg_load decimal(7,3) NOT NULL,
    cardio_load decimal(7,3) NOT NULL,
    strength_stress decimal(7,3) NOT NULL,
    planned_restoration_pct decimal(7,5) NOT NULL,
    protein_adjustment_g decimal(7,2) NOT NULL,
    target_fat_g decimal(7,2) NOT NULL,
    typical_recovery_hours decimal(7,2) NOT NULL,
    planning_note nvarchar(1200) NULL,
    is_active bit NOT NULL CONSTRAINT DF_WorkoutTypeDefinitions_Active DEFAULT 1,

    CONSTRAINT FK_WorkoutTypeDefinitions_Config FOREIGN KEY (config_version_id) REFERENCES config.ConfigVersions(config_version_id),
    CONSTRAINT UQ_WorkoutTypeDefinitions_Code UNIQUE(config_version_id, workout_type_code)
);

CREATE TABLE config.GlobalSettings (
    global_setting_id uniqueidentifier NOT NULL CONSTRAINT PK_GlobalSettings PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    config_version_id uniqueidentifier NOT NULL,
    setting_code varchar(80) NOT NULL,
    decimal_value decimal(18,6) NULL,
    text_value nvarchar(1000) NULL,
    unit_code varchar(30) NULL,
    description nvarchar(1200) NOT NULL,
    CONSTRAINT FK_GlobalSettings_Config FOREIGN KEY (config_version_id) REFERENCES config.ConfigVersions(config_version_id),
    CONSTRAINT UQ_GlobalSettings_Code UNIQUE(config_version_id, setting_code)
);

CREATE TABLE app.Programs (
    program_id uniqueidentifier NOT NULL CONSTRAINT PK_Programs PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    client_id uniqueidentifier NOT NULL,
    config_version_id uniqueidentifier NOT NULL,
    selected_plan_code varchar(40) NOT NULL,
    goal_code varchar(40) NOT NULL,
    start_date date NOT NULL,
    duration_weeks smallint NOT NULL CONSTRAINT DF_Programs_Duration DEFAULT 12,
    available_training_days_week tinyint NOT NULL,
    program_status_code varchar(20) NOT NULL CONSTRAINT DF_Programs_Status DEFAULT 'ACTIVE',
    row_version rowversion NOT NULL,
    created_at_utc datetime2(3) NOT NULL CONSTRAINT DF_Programs_Created DEFAULT SYSUTCDATETIME(),
    updated_at_utc datetime2(3) NOT NULL CONSTRAINT DF_Programs_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Programs_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id),
    CONSTRAINT FK_Programs_Config FOREIGN KEY (config_version_id) REFERENCES config.ConfigVersions(config_version_id),
    CONSTRAINT CK_Programs_TrainingDays CHECK (available_training_days_week BETWEEN 1 AND 7)
);
CREATE UNIQUE INDEX UX_Programs_OneActive
ON app.Programs(client_id)
WHERE program_status_code = 'ACTIVE';

CREATE TABLE app.ProgramGoals (
    program_goal_id uniqueidentifier NOT NULL CONSTRAINT PK_ProgramGoals PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    program_id uniqueidentifier NOT NULL,
    target_weight_low_lb decimal(6,2) NULL,
    target_weight_high_lb decimal(6,2) NULL,
    target_waist_in decimal(5,2) NULL,
    goal_notes nvarchar(1000) NULL,
    CONSTRAINT FK_ProgramGoals_Program FOREIGN KEY (program_id) REFERENCES app.Programs(program_id),
    CONSTRAINT UQ_ProgramGoals_Program UNIQUE(program_id)
);

CREATE TABLE app.Measurements (
    measurement_id uniqueidentifier NOT NULL CONSTRAINT PK_Measurements PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    client_id uniqueidentifier NOT NULL,
    program_id uniqueidentifier NULL,
    measurement_at_utc datetime2(3) NOT NULL,
    local_measurement_date date NOT NULL,
    weight_lb decimal(6,2) NULL,
    body_fat_pct decimal(7,5) NULL,
    muscle_mass_lb decimal(6,2) NULL,
    skeletal_muscle_mass_lb decimal(6,2) NULL,
    body_water_lb decimal(6,2) NULL,
    visceral_fat_rating decimal(6,2) NULL,
    subcutaneous_fat_pct decimal(7,5) NULL,
    waist_in decimal(5,2) NULL,
    hips_in decimal(5,2) NULL,
    scale_whr decimal(6,4) NULL,
    measurement_source_code varchar(30) NOT NULL CONSTRAINT DF_Measurements_Source DEFAULT 'MANUAL',
    notes nvarchar(1000) NULL,
    is_deleted bit NOT NULL CONSTRAINT DF_Measurements_Deleted DEFAULT 0,
    created_at_utc datetime2(3) NOT NULL CONSTRAINT DF_Measurements_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Measurements_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id),
    CONSTRAINT FK_Measurements_Program FOREIGN KEY (program_id) REFERENCES app.Programs(program_id)
);
CREATE INDEX IX_Measurements_ClientDate
ON app.Measurements(client_id, local_measurement_date DESC);

CREATE TABLE app.DailyCheckins (
    checkin_id uniqueidentifier NOT NULL CONSTRAINT PK_DailyCheckins PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    client_id uniqueidentifier NOT NULL,
    program_id uniqueidentifier NOT NULL,
    checkin_date date NOT NULL,
    sleep_hours decimal(4,2) NULL,
    resting_hr smallint NULL,
    steps int NULL,
    energy_score tinyint NULL,
    soreness_score tinyint NULL,
    pain_flag bit NOT NULL CONSTRAINT DF_DailyCheckins_Pain DEFAULT 0,
    illness_flag bit NOT NULL CONSTRAINT DF_DailyCheckins_Illness DEFAULT 0,
    intended_workout_type_code varchar(60) NULL,
    notes nvarchar(2000) NULL,
    row_version rowversion NOT NULL,
    created_at_utc datetime2(3) NOT NULL CONSTRAINT DF_DailyCheckins_Created DEFAULT SYSUTCDATETIME(),
    updated_at_utc datetime2(3) NOT NULL CONSTRAINT DF_DailyCheckins_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_DailyCheckins_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id),
    CONSTRAINT FK_DailyCheckins_Program FOREIGN KEY (program_id) REFERENCES app.Programs(program_id),
    CONSTRAINT UQ_DailyCheckins_Date UNIQUE(program_id, checkin_date),
    CONSTRAINT CK_DailyCheckins_Sleep CHECK (sleep_hours IS NULL OR sleep_hours BETWEEN 0 AND 16),
    CONSTRAINT CK_DailyCheckins_Energy CHECK (energy_score IS NULL OR energy_score BETWEEN 1 AND 10),
    CONSTRAINT CK_DailyCheckins_Soreness CHECK (soreness_score IS NULL OR soreness_score BETWEEN 1 AND 10)
);

CREATE TABLE app.PlannedWorkouts (
    planned_workout_id uniqueidentifier NOT NULL CONSTRAINT PK_PlannedWorkouts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    client_id uniqueidentifier NOT NULL,
    program_id uniqueidentifier NOT NULL,
    planned_date date NOT NULL,
    workout_type_code varchar(60) NOT NULL,
    source_code varchar(20) NOT NULL CONSTRAINT DF_PlannedWorkouts_Source DEFAULT 'CLIENT',
    notes nvarchar(1000) NULL,
    row_version rowversion NOT NULL,
    created_at_utc datetime2(3) NOT NULL CONSTRAINT DF_PlannedWorkouts_Created DEFAULT SYSUTCDATETIME(),
    updated_at_utc datetime2(3) NOT NULL CONSTRAINT DF_PlannedWorkouts_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_PlannedWorkouts_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id),
    CONSTRAINT FK_PlannedWorkouts_Program FOREIGN KEY (program_id) REFERENCES app.Programs(program_id),
    CONSTRAINT UQ_PlannedWorkouts_Date UNIQUE(program_id, planned_date)
);
CREATE INDEX IX_PlannedWorkouts_ProgramDate
ON app.PlannedWorkouts(program_id, planned_date);

CREATE TABLE app.WorkoutSessions (
    workout_session_id uniqueidentifier NOT NULL CONSTRAINT PK_WorkoutSessions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    client_id uniqueidentifier NOT NULL,
    program_id uniqueidentifier NOT NULL,
    workout_date date NOT NULL,
    workout_type_code varchar(60) NOT NULL,
    duration_min decimal(6,2) NOT NULL,
    typical_rest_sec smallint NULL,
    session_rpe decimal(3,1) NULL,
    device_calories decimal(7,2) NULL,
    completed_flag bit NOT NULL CONSTRAINT DF_WorkoutSessions_Completed DEFAULT 1,

    calculated_workout_calories decimal(7,2) NULL,
    calculated_scaled_glycogen_burn_g decimal(7,2) NULL,
    calculated_push_load decimal(7,3) NULL,
    calculated_pull_load decimal(7,3) NULL,
    calculated_leg_load decimal(7,3) NULL,
    calculated_cardio_load decimal(7,3) NULL,
    calculation_engine_version varchar(30) NULL,

    notes nvarchar(2000) NULL,
    row_version rowversion NOT NULL,
    created_at_utc datetime2(3) NOT NULL CONSTRAINT DF_WorkoutSessions_Created DEFAULT SYSUTCDATETIME(),
    updated_at_utc datetime2(3) NOT NULL CONSTRAINT DF_WorkoutSessions_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_WorkoutSessions_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id),
    CONSTRAINT FK_WorkoutSessions_Program FOREIGN KEY (program_id) REFERENCES app.Programs(program_id),
    CONSTRAINT CK_WorkoutSessions_Duration CHECK (duration_min BETWEEN 0 AND 360),
    CONSTRAINT CK_WorkoutSessions_RPE CHECK (session_rpe IS NULL OR session_rpe BETWEEN 1 AND 10)
);
CREATE INDEX IX_WorkoutSessions_ProgramDate
ON app.WorkoutSessions(program_id, workout_date);

CREATE TABLE app.NutritionDays (
    nutrition_day_id uniqueidentifier NOT NULL CONSTRAINT PK_NutritionDays PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    client_id uniqueidentifier NOT NULL,
    program_id uniqueidentifier NOT NULL,
    nutrition_date date NOT NULL,
    actual_calories decimal(8,2) NULL,
    actual_protein_g decimal(7,2) NULL,
    actual_carbs_g decimal(7,2) NULL,
    actual_fat_g decimal(7,2) NULL,
    fiber_g decimal(7,2) NULL,
    water_oz decimal(7,2) NULL,
    entry_source_code varchar(20) NOT NULL CONSTRAINT DF_NutritionDays_Source DEFAULT 'MANUAL',
    notes nvarchar(2000) NULL,
    row_version rowversion NOT NULL,
    created_at_utc datetime2(3) NOT NULL CONSTRAINT DF_NutritionDays_Created DEFAULT SYSUTCDATETIME(),
    updated_at_utc datetime2(3) NOT NULL CONSTRAINT DF_NutritionDays_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_NutritionDays_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id),
    CONSTRAINT FK_NutritionDays_Program FOREIGN KEY (program_id) REFERENCES app.Programs(program_id),
    CONSTRAINT UQ_NutritionDays_Date UNIQUE(program_id, nutrition_date)
);

CREATE TABLE app.StrengthTests (
    strength_test_id uniqueidentifier NOT NULL CONSTRAINT PK_StrengthTests PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    client_id uniqueidentifier NOT NULL,
    program_id uniqueidentifier NULL,
    test_code varchar(60) NOT NULL,
    test_at_utc datetime2(3) NOT NULL,
    result_value decimal(10,3) NOT NULL,
    unit_code varchar(20) NOT NULL,
    ratio_score decimal(10,5) NULL,
    notes nvarchar(1000) NULL,
    CONSTRAINT FK_StrengthTests_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id),
    CONSTRAINT FK_StrengthTests_Program FOREIGN KEY (program_id) REFERENCES app.Programs(program_id)
);

CREATE TABLE app.AnalysisRuns (
    analysis_run_id uniqueidentifier NOT NULL CONSTRAINT PK_AnalysisRuns PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    client_id uniqueidentifier NOT NULL,
    program_id uniqueidentifier NOT NULL,
    action_date date NOT NULL,
    input_cutoff_at_utc datetime2(3) NOT NULL,
    engine_version varchar(30) NOT NULL,
    config_version_id uniqueidentifier NOT NULL,
    run_status_code varchar(20) NOT NULL,
    data_confidence_code varchar(20) NULL,
    diagnostic_json nvarchar(max) NULL,
    started_at_utc datetime2(3) NOT NULL CONSTRAINT DF_AnalysisRuns_Started DEFAULT SYSUTCDATETIME(),
    completed_at_utc datetime2(3) NULL,
    CONSTRAINT FK_AnalysisRuns_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id),
    CONSTRAINT FK_AnalysisRuns_Program FOREIGN KEY (program_id) REFERENCES app.Programs(program_id),
    CONSTRAINT FK_AnalysisRuns_Config FOREIGN KEY (config_version_id) REFERENCES config.ConfigVersions(config_version_id)
);

CREATE TABLE app.WeeklyStrategySnapshots (
    weekly_strategy_snapshot_id uniqueidentifier NOT NULL CONSTRAINT PK_WeeklyStrategySnapshots PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    analysis_run_id uniqueidentifier NOT NULL,
    week_start_date date NOT NULL,
    major_push_count smallint NOT NULL,
    major_pull_count smallint NOT NULL,
    major_legs_count smallint NOT NULL,
    light_strength_count smallint NOT NULL,
    primary_conditioning_count smallint NOT NULL,
    recovery_count smallint NOT NULL,
    heavy_generic_count smallint NOT NULL,
    status_code varchar(20) NOT NULL,
    strategy_note nvarchar(1600) NOT NULL,
    CONSTRAINT FK_WeeklyStrategySnapshots_Run FOREIGN KEY (analysis_run_id) REFERENCES app.AnalysisRuns(analysis_run_id)
);

CREATE TABLE app.Recommendations (
    recommendation_id uniqueidentifier NOT NULL CONSTRAINT PK_Recommendations PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    analysis_run_id uniqueidentifier NOT NULL,
    client_id uniqueidentifier NOT NULL,
    program_id uniqueidentifier NOT NULL,
    action_date date NOT NULL,

    planned_workout_type_code varchar(60) NULL,
    user_selected_workout_type_code varchar(60) NULL,
    recommended_workout_type_code varchar(60) NOT NULL,
    schedule_change_recommended bit NOT NULL,
    recommendation_basis_code varchar(40) NOT NULL,

    base_calories decimal(8,2) NOT NULL,
    recommended_calories decimal(8,2) NOT NULL,
    recommended_protein_g decimal(7,2) NOT NULL,
    base_carbs_g decimal(7,2) NOT NULL,
    recovery_allocation_g decimal(7,2) NOT NULL,
    pre_workout_portion_g decimal(7,2) NOT NULL,
    recommended_carbs_g decimal(7,2) NOT NULL,
    recommended_fat_g decimal(7,2) NOT NULL,

    four_day_glycogen_demand_g decimal(8,2) NOT NULL,
    four_day_restoration_credit_g decimal(8,2) NOT NULL,
    absolute_restoration_ratio decimal(8,5) NOT NULL,
    functional_target_ratio decimal(8,5) NOT NULL,
    target_achievement_ratio decimal(8,5) NOT NULL,
    glycogen_readiness_code varchar(30) NOT NULL,

    hard_session_count_4d smallint NOT NULL,
    region_priority_code varchar(20) NULL,
    cardio_need_code varchar(20) NULL,
    weekly_strategy_status_code varchar(20) NOT NULL,
    weekly_strategy_note nvarchar(1600) NOT NULL,
    timing_note nvarchar(1600) NOT NULL,
    short_explanation nvarchar(2400) NOT NULL,

    created_at_utc datetime2(3) NOT NULL CONSTRAINT DF_Recommendations_Created DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_Recommendations_Run FOREIGN KEY (analysis_run_id) REFERENCES app.AnalysisRuns(analysis_run_id),
    CONSTRAINT FK_Recommendations_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id),
    CONSTRAINT FK_Recommendations_Program FOREIGN KEY (program_id) REFERENCES app.Programs(program_id),
    CONSTRAINT UQ_Recommendations_Run UNIQUE(analysis_run_id)
);
CREATE INDEX IX_Recommendations_ProgramDate
ON app.Recommendations(program_id, action_date DESC);

CREATE TABLE app.RecommendationAlerts (
    recommendation_alert_id uniqueidentifier NOT NULL CONSTRAINT PK_RecommendationAlerts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    recommendation_id uniqueidentifier NOT NULL,
    alert_code varchar(60) NOT NULL,
    severity_code varchar(20) NOT NULL,
    message nvarchar(1200) NOT NULL,
    sort_order smallint NOT NULL CONSTRAINT DF_RecommendationAlerts_Order DEFAULT 1,
    CONSTRAINT FK_RecommendationAlerts_Recommendation
        FOREIGN KEY (recommendation_id) REFERENCES app.Recommendations(recommendation_id) ON DELETE CASCADE
);

CREATE TABLE audit.AuditEvents (
    audit_event_id uniqueidentifier NOT NULL CONSTRAINT PK_AuditEvents PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    occurred_at_utc datetime2(3) NOT NULL CONSTRAINT DF_AuditEvents_Occurred DEFAULT SYSUTCDATETIME(),
    actor_user_id uniqueidentifier NULL,
    client_id uniqueidentifier NULL,
    event_type_code varchar(60) NOT NULL,
    entity_type varchar(60) NOT NULL,
    entity_id uniqueidentifier NULL,
    details_json nvarchar(max) NULL,
    correlation_id uniqueidentifier NULL,
    CONSTRAINT FK_AuditEvents_User FOREIGN KEY (actor_user_id) REFERENCES app.UserAccounts(user_id),
    CONSTRAINT FK_AuditEvents_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id)
);

COMMIT TRANSACTION;
