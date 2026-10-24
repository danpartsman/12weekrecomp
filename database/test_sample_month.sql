-- Explicit, TEST-only sample batch. Not a schema migration.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
IF DB_NAME() <> @ExpectedDatabase THROW 51000, 'Unexpected sample-data database.', 1;
DECLARE @Email nvarchar(320) = N'danpartsman@gmail.com';
DECLARE @EndDate date = '20261023', @StartDate date = DATEADD(day, -34, '20261023');
DECLARE @Batch nvarchar(100) = N'12WR_TEST_MONTH_20261023';
DECLARE @UserId uniqueidentifier, @ClientId uniqueidentifier, @ProgramId uniqueidentifier;
IF (SELECT COUNT(*) FROM app.UserAccounts u JOIN app.LocalAuthCredentials a ON a.UserId = u.user_id
    WHERE LOWER(u.email_normalized) = @Email AND u.account_status_code = 'ACTIVE') <> 1
    THROW 51001, 'Expected exactly one active native TEST account for the requested email.', 1;
SELECT @UserId=u.user_id FROM app.UserAccounts u JOIN app.LocalAuthCredentials a ON a.UserId=u.user_id
WHERE LOWER(u.email_normalized)=@Email AND u.account_status_code='ACTIVE';
IF (SELECT COUNT(*) FROM app.UserClientAccess a JOIN app.Clients c ON c.client_id=a.client_id
    WHERE a.user_id=@UserId AND a.access_role_code='OWNER' AND a.revoked_at_utc IS NULL AND c.client_status_code='ACTIVE') <> 1
    THROW 51002, 'Expected exactly one active owned client profile; no account/profile was changed.', 1;
SELECT @ClientId=a.client_id FROM app.UserClientAccess a JOIN app.Clients c ON c.client_id=a.client_id
WHERE a.user_id=@UserId AND a.access_role_code='OWNER' AND a.revoked_at_utc IS NULL AND c.client_status_code='ACTIVE';
IF (SELECT COUNT(*) FROM app.Programs WHERE client_id=@ClientId AND program_status_code='ACTIVE') <> 1
    THROW 51003, 'Expected one active TEST program; no program was created or changed.', 1;
SELECT @ProgramId=program_id FROM app.Programs WHERE client_id=@ClientId AND program_status_code='ACTIVE';
DECLARE @Now datetime2=SYSUTCDATETIME(), @Date date=@StartDate, @i int=0;
DECLARE @Scales int=0,@Nutrition int=0,@Workouts int=0,@Checkins int=0,@Plans int=0;
WHILE @Date<=@EndDate
BEGIN
    DECLARE @Day int = DATEDIFF(day,'19000101',@Date)%7;
    DECLARE @Source nvarchar(200)=@Batch+N':'+CONVERT(nvarchar(10),@Date,23);
    DECLARE @Type varchar(60)=CASE @Day WHEN 0 THEN 'MAJOR_LEGS' WHEN 1 THEN 'EASY_CARDIO'
        WHEN 2 THEN 'MAJOR_PUSH' WHEN 4 THEN 'MAJOR_PULL' WHEN 5 THEN 'ZONE2_CARDIO' ELSE 'REST' END;
    DECLARE @Minutes decimal(6,2)=CASE @Day WHEN 0 THEN 45 WHEN 1 THEN 25 WHEN 2 THEN 40 WHEN 4 THEN 45 WHEN 5 THEN 35 ELSE 0 END;
    DECLARE @Rpe decimal(3,1)=CASE WHEN @Type LIKE 'MAJOR_%' THEN 7 WHEN @Type='ZONE2_CARDIO' THEN 4 WHEN @Type='EASY_CARDIO' THEN 3 ELSE 1 END;
    DECLARE @Protein decimal(7,2)=180+(@i%5)*3, @Carbs decimal(7,2)=CASE WHEN @Type LIKE 'MAJOR_%' THEN 190 ELSE 145 END+(@i%4)*5;
    DECLARE @Fat decimal(7,2)=55+(@i%4)*5;
    IF NOT EXISTS(SELECT 1 FROM app.Measurements WHERE client_id=@ClientId AND local_measurement_date=@Date AND is_deleted=0)
       AND NOT EXISTS(SELECT 1 FROM app.Measurements WHERE client_id=@ClientId AND source_record_id=@Source)
    BEGIN
        INSERT app.Measurements(measurement_id,client_id,program_id,measurement_at_utc,local_measurement_date,
            weight_lb,body_fat_pct,skeletal_muscle_mass_lb,muscle_mass_lb,body_water_lb,visceral_fat_rating,
            subcutaneous_fat_pct,waist_in,hips_in,measurement_source_code,source_record_id,notes,is_deleted,created_at_utc)
        VALUES(NEWID(),@ClientId,@ProgramId,DATEADD(hour,14,CAST(@Date AS datetime2)),@Date,
            213.0-@i*.09+CASE @i%3 WHEN 0 THEN .3 WHEN 1 THEN -.2 ELSE 0 END,
            .2700-@i*.00035,89.0+@i*.025,144+@i*.02,111+(@i%3)*.3,10,.1930-@i*.00020,
            39.5-@i*.02,41,'TEST_SAMPLE',@Source,@Batch+N' · Synthetic scale reading for TEST preview.',0,@Now);
        SET @Scales+=1;
    END;
    IF NOT EXISTS(SELECT 1 FROM app.NutritionDays WHERE client_id=@ClientId AND nutrition_date=@Date)
    BEGIN
        INSERT app.NutritionDays(nutrition_day_id,client_id,program_id,nutrition_date,actual_calories,actual_protein_g,
            actual_carbs_g,actual_fat_g,fiber_g,water_oz,entry_source_code,source_record_id,notes,created_at_utc,updated_at_utc)
        VALUES(NEWID(),@ClientId,@ProgramId,@Date,@Protein*4+@Carbs*4+@Fat*9,@Protein,@Carbs,@Fat,
            24+@i%8,80+(@i%4)*8,'TEST_SAMPLE',@Source,@Batch+N' · Synthetic daily nutrition totals; no real food log was replaced.',@Now,@Now);
        SET @Nutrition+=1;
    END;
    IF NOT EXISTS(SELECT 1 FROM app.DailyCheckins WHERE client_id=@ClientId AND checkin_date=@Date)
    BEGIN
        INSERT app.DailyCheckins(checkin_id,client_id,program_id,checkin_date,sleep_hours,resting_hr,steps,energy_score,
            soreness_score,pain_flag,illness_flag,notes,created_at_utc,updated_at_utc)
        VALUES(NEWID(),@ClientId,@ProgramId,@Date,6.5+(@i%4)*.25,54+@i%5,5500+(@i%7)*600,
            6+@i%3,2+@i%3,0,0,@Batch+N' · Sample check-in: '+CASE WHEN @Type LIKE 'MAJOR_%' THEN N'normal muscle soreness after training.' ELSE N'recovery and walking day.' END,@Now,@Now);
        SET @Checkins+=1;
    END;
    IF NOT EXISTS(SELECT 1 FROM app.PlannedWorkouts WHERE client_id=@ClientId AND planned_date=@Date)
    BEGIN
        INSERT app.PlannedWorkouts(planned_workout_id,client_id,program_id,planned_date,workout_type_code,source_code,notes,
            planned_duration_min,planned_rpe,created_at_utc,updated_at_utc)
        VALUES(NEWID(),@ClientId,@ProgramId,@Date,@Type,'TEST_SAMPLE',@Batch+N' · Sample weekly schedule.',@Minutes,@Rpe,@Now,@Now);
        SET @Plans+=1;
    END;
    IF @Minutes>0 AND NOT EXISTS(SELECT 1 FROM app.WorkoutSessions WHERE client_id=@ClientId AND workout_date=@Date)
    BEGIN
        INSERT app.WorkoutSessions(workout_session_id,client_id,program_id,workout_date,workout_name,workout_type_code,
            duration_min,typical_rest_sec,session_rpe,completed_flag,entry_source_code,source_record_id,notes,created_at_utc,updated_at_utc)
        VALUES(NEWID(),@ClientId,@ProgramId,@Date,N'[Sample] '+REPLACE(@Type,'_',' '),@Type,@Minutes,120,@Rpe,1,
            'TEST_SAMPLE',@Source,@Batch+N' · Synthetic completed workout for TEST preview.',@Now,@Now);
        SET @Workouts+=1;
        -- A few days include two genuine sample session rows to preview multiple actuals.
        IF @Day=2
        BEGIN
            INSERT app.WorkoutSessions(workout_session_id,client_id,program_id,workout_date,workout_name,workout_type_code,
                duration_min,session_rpe,completed_flag,entry_source_code,source_record_id,notes,created_at_utc,updated_at_utc)
            VALUES(NEWID(),@ClientId,@ProgramId,@Date,N'[Sample] Evening walk','EASY_CARDIO',20,3,1,
                'TEST_SAMPLE',@Source+N':walk',@Batch+N' · Synthetic second session.',@Now,@Now);
            SET @Workouts+=1;
        END;
    END;
    SET @Date=DATEADD(day,1,@Date); SET @i+=1;
END;
-- Let this existing coach/administrator preview their own TEST profile in their roster.
-- Never promote an account or grant access to someone else's client.
IF EXISTS(SELECT 1 FROM app.LocalAuthCredentials WHERE UserId=@UserId AND GlobalRoleCode IN('ADMINISTRATOR','COACH'))
 AND NOT EXISTS(SELECT 1 FROM app.CoachClientRelationships WHERE coach_user_id=@UserId AND client_id=@ClientId AND status_code='ACTIVE')
    INSERT app.CoachClientRelationships(coach_client_relationship_id,coach_user_id,client_id,status_code,
        invite_expires_at_utc,created_at_utc,accepted_at_utc)
    VALUES(NEWID(),@UserId,@ClientId,'ACTIVE',@Now,@Now,@Now);
COMMIT TRANSACTION;
SELECT @ClientId AS ClientId,@StartDate AS StartDate,@EndDate AS EndDate,@Scales AS AddedScaleReadings,
    @Nutrition AS AddedNutritionDays,@Workouts AS AddedWorkouts,@Checkins AS AddedCheckins,@Plans AS AddedPlans,
    (SELECT COUNT(*) FROM app.Measurements WHERE client_id=@ClientId AND notes LIKE @Batch+'%') AS VerifiedSampleScales,
    (SELECT COUNT(*) FROM app.NutritionDays WHERE client_id=@ClientId AND notes LIKE @Batch+'%') AS VerifiedSampleNutrition,
    (SELECT COUNT(*) FROM app.WorkoutSessions WHERE client_id=@ClientId AND notes LIKE @Batch+'%') AS VerifiedSampleWorkouts;
