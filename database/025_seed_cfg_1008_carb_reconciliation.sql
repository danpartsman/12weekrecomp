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
    WHERE version_code = 'CFG-1008.0';

    IF @ConfigVersionId IS NULL
        THROW 51000, 'CFG-1008.0 was not found.', 1;

    IF @StatusCode <> 'DRAFT'
        THROW 51001, 'CFG-1008.0 is not DRAFT and cannot be modified.', 1;

    -------------------------------------------------------------------------
    -- CARB_STANDARD_RECOMP_MAX_G_PER_KG
    --
    -- This is a 12WR ENGINEERING GUARDRAIL, not a claim that 5.0 g/kg/day
    -- is a universal physiological maximum.
    --
    -- Evidence basis / rationale:
    --
    -- Impey SG et al. Fuel for the Work Required.
    -- Sports Med. 2018;48:1031-1048.
    -- DOI: 10.1007/s40279-018-0867-7
    --
    -- Henselmans M et al. The Effect of Carbohydrate Intake on Strength
    -- and Resistance Training Performance: A Systematic Review.
    -- Nutrients. 2022;14(4):856.
    -- DOI: 10.3390/nu14040856
    --
    -- Hamidvand A et al. Acute effects of resistance exercise on skeletal
    -- muscle glycogen depletion: systematic review and meta-analysis.
    -- Physiol Rep. 2025;13:e70683.
    -- DOI: 10.14814/phy2.70683
    --
    -- Purpose:
    -- Prevent the energy-reconciliation layer from manufacturing very large
    -- carbohydrate prescriptions merely to fill an available calorie budget.
    -- Physiological carbohydrate requirements may override this standard
    -- boundary when justified.
    -------------------------------------------------------------------------

    DELETE FROM config.GlobalSettings
    WHERE config_version_id = @ConfigVersionId
      AND setting_code = 'CARB_STANDARD_RECOMP_MAX_G_PER_KG';

    INSERT config.GlobalSettings
    (
        global_setting_id,
        config_version_id,
        setting_code,
        decimal_value,
        text_value,
        unit_code,
        description
    )
    VALUES
    (
        NEWID(),
        @ConfigVersionId,
        'CARB_STANDARD_RECOMP_MAX_G_PER_KG',
        5.000000,
        NULL,
        'g/kg',
        '12WR standard recomp carbohydrate useful-range ceiling. Engineering guardrail; physiological requirements may override.'
    );

    COMMIT TRANSACTION;

    SELECT
        cv.version_code,
        gs.setting_code,
        gs.decimal_value,
        gs.unit_code,
        gs.description
    FROM config.GlobalSettings gs
    INNER JOIN config.ConfigVersions cv
        ON cv.config_version_id = gs.config_version_id
    WHERE cv.version_code = 'CFG-1008.0'
      AND gs.setting_code = 'CARB_STANDARD_RECOMP_MAX_G_PER_KG';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
