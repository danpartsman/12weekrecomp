SELECT DB_NAME() AS database_name, SYSUTCDATETIME() AS checked_at_utc;

SELECT s.name AS schema_name, t.name AS table_name
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name IN ('app','config','audit')
ORDER BY s.name, t.name;

SELECT version_code, status_code, published_at_utc
FROM config.ConfigVersions
ORDER BY created_at_utc DESC;

DECLARE @ActiveConfig uniqueidentifier =
(
    SELECT TOP (1) config_version_id
    FROM config.ConfigVersions
    WHERE status_code = 'PUBLISHED'
    ORDER BY effective_from_utc DESC, created_at_utc DESC
);

SELECT
    COUNT(*) AS workout_type_count,
    SUM(CASE WHEN counts_dedicated_heavy = 1 THEN 1 ELSE 0 END) AS dedicated_heavy_types,
    SUM(CASE WHEN counts_light_strength = 1 THEN 1 ELSE 0 END) AS light_strength_types,
    SUM(CASE WHEN counts_primary_conditioning = 1 THEN 1 ELSE 0 END) AS conditioning_types,
    SUM(CASE WHEN counts_recovery = 1 THEN 1 ELSE 0 END) AS recovery_types
FROM config.WorkoutTypeDefinitions
WHERE config_version_id = @ActiveConfig
  AND is_active = 1;

SELECT plan_code, display_name, base_rest_carbs_g
FROM config.PlanDefinitions
WHERE config_version_id = @ActiveConfig
ORDER BY sort_order;
