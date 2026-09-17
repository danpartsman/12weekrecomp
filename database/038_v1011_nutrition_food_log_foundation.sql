SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF SCHEMA_ID(N'nutrition') IS NULL
        THROW 51120, 'nutrition schema is missing; apply Phase 1B first.', 1;

    IF OBJECT_ID(N'nutrition.ExternalFoods', N'U') IS NULL
        THROW 51121, 'nutrition.ExternalFoods is missing; apply Phase 1B first.', 1;

    DECLARE @basisAmountExists bit =
        CASE
            WHEN COL_LENGTH(
                N'nutrition.ExternalFoods',
                N'nutrient_basis_amount') IS NULL
            THEN 0
            ELSE 1
        END;

    DECLARE @basisUnitExists bit =
        CASE
            WHEN COL_LENGTH(
                N'nutrition.ExternalFoods',
                N'nutrient_basis_unit') IS NULL
            THEN 0
            ELSE 1
        END;

    IF @basisAmountExists <> @basisUnitExists
        THROW 51122, 'Partial Phase 1C-B nutrient basis columns detected.', 1;

    IF @basisAmountExists = 0
    BEGIN
        ALTER TABLE nutrition.ExternalFoods
            ADD nutrient_basis_amount decimal(12,4) NULL,
                nutrient_basis_unit varchar(30) NULL;
    END;

    /*
        SQL Server compiles a batch before executing it. Statements later in
        the same batch cannot safely bind to columns that were just added by
        ALTER TABLE. Run every statement that references the new columns
        through sp_executesql so name resolution happens after ALTER TABLE.
    */
    EXEC sys.sp_executesql N'
        UPDATE nutrition.ExternalFoods
        SET
            nutrient_basis_amount = COALESCE(nutrient_basis_amount, 100.0000),
            nutrient_basis_unit = COALESCE(nutrient_basis_unit, ''g'')
        WHERE nutrient_basis_amount IS NULL
           OR nutrient_basis_unit IS NULL;
    ';

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns c
        INNER JOIN sys.tables t
            ON t.object_id = c.object_id
        INNER JOIN sys.schemas s
            ON s.schema_id = t.schema_id
        WHERE s.name = N'nutrition'
          AND t.name = N'ExternalFoods'
          AND c.name = N'nutrient_basis_amount'
          AND c.is_nullable = 1
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE nutrition.ExternalFoods
                ALTER COLUMN nutrient_basis_amount decimal(12,4) NOT NULL;
        ';
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns c
        INNER JOIN sys.tables t
            ON t.object_id = c.object_id
        INNER JOIN sys.schemas s
            ON s.schema_id = t.schema_id
        WHERE s.name = N'nutrition'
          AND t.name = N'ExternalFoods'
          AND c.name = N'nutrient_basis_unit'
          AND c.is_nullable = 1
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE nutrition.ExternalFoods
                ALTER COLUMN nutrient_basis_unit varchar(30) NOT NULL;
        ';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints cc
        INNER JOIN sys.tables t
            ON t.object_id = cc.parent_object_id
        INNER JOIN sys.schemas s
            ON s.schema_id = t.schema_id
        WHERE s.name = N'nutrition'
          AND t.name = N'ExternalFoods'
          AND cc.name = N'CK_NutritionExternalFoods_NutrientBasisAmount'
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE nutrition.ExternalFoods
                ADD CONSTRAINT CK_NutritionExternalFoods_NutrientBasisAmount
                    CHECK (nutrient_basis_amount > 0);
        ';
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
