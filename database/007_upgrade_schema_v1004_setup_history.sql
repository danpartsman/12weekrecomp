SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/*
    Web Recomp v1004 Review My Setup / plan-history foundation.

    Additive only. ProgramRecord.SelectedPlanCode remains the current plan.
    This table records effective-dated Primary Plan changes so historical
    recommendation dates can continue resolving the plan that was in effect
    at that time.
*/

IF OBJECT_ID('app.ProgramPlanChanges', 'U') IS NULL
BEGIN
    CREATE TABLE app.ProgramPlanChanges
    (
        program_plan_change_id uniqueidentifier NOT NULL,
        program_id uniqueidentifier NOT NULL,
        changed_by_user_id uniqueidentifier NULL,
        old_plan_code varchar(40) NOT NULL,
        new_plan_code varchar(40) NOT NULL,
        old_goal_code varchar(50) NOT NULL,
        new_goal_code varchar(50) NOT NULL,
        effective_date date NOT NULL,
        change_source_code varchar(40) NOT NULL
            CONSTRAINT DF_ProgramPlanChanges_Source DEFAULT 'USER_SETUP_REVIEW',
        changed_at_utc datetime2(3) NOT NULL
            CONSTRAINT DF_ProgramPlanChanges_ChangedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ProgramPlanChanges PRIMARY KEY (program_plan_change_id),
        CONSTRAINT FK_ProgramPlanChanges_Program
            FOREIGN KEY (program_id) REFERENCES app.Programs(program_id),
        CONSTRAINT FK_ProgramPlanChanges_User
            FOREIGN KEY (changed_by_user_id) REFERENCES app.UserAccounts(user_id)
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_ProgramPlanChanges_Program_EffectiveDate'
      AND object_id = OBJECT_ID('app.ProgramPlanChanges')
)
BEGIN
    CREATE INDEX IX_ProgramPlanChanges_Program_EffectiveDate
        ON app.ProgramPlanChanges(program_id, effective_date, changed_at_utc);
END;
GO

PRINT 'v1004 Review My Setup / plan-history schema applied successfully.';
GO
