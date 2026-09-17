using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence.Catalog;
using WebRecomp.WorkoutIntelligence.Persistence;

static string GetRequiredArg(string[] args, string name)
{
    var index = Array.FindIndex(
        args,
        value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));

    if (index < 0 ||
        index + 1 >= args.Length ||
        string.IsNullOrWhiteSpace(args[index + 1]))
    {
        throw new ArgumentException($"Missing required argument: {name}");
    }

    return args[index + 1];
}

static string Sha256File(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static void RequireMax(
    string field,
    string? value,
    int maxLength,
    string exerciseName)
{
    if (value is not null && value.Length > maxLength)
    {
        throw new InvalidDataException(
            $"{field} exceeds SQL max length {maxLength} " +
            $"for exercise '{exerciseName}'. Actual length: {value.Length}. " +
            $"Value: {value}");
    }
}

static void ValidateCatalogForSql(
    ExerciseCatalogSnapshot snapshot,
    ExerciseCatalogProvenance provenance)
{
    RequireMax("provenance.SourceCode", provenance.SourceCode, 40, "(provenance)");
    RequireMax("provenance.LicenseCode", provenance.LicenseCode, 40, "(provenance)");
    RequireMax("provenance.SourceVersion", provenance.SourceVersion, 200, "(provenance)");
    RequireMax("provenance.ManifestSha256", provenance.ManifestSha256, 64, "(provenance)");

    foreach (var item in snapshot.Exercises)
    {
        RequireMax("SourceCode", item.SourceCode, 40, item.Name);
        RequireMax("SourceExternalId", item.SourceExternalId, 100, item.Name);
        RequireMax("Name", item.Name, 200, item.Name);
        RequireMax("MeasurementType", item.MeasurementType, 30, item.Name);
        RequireMax("DifficultyLevel", item.DifficultyLevel, 50, item.Name);
        RequireMax("ForceType", item.ForceType, 30, item.Name);
        RequireMax("Mechanics", item.Mechanics, 30, item.Name);
        RequireMax("Category", item.Category, 60, item.Name);

        foreach (var muscle in item.MuscleTargets)
        {
            RequireMax("Muscle.ExternalId", muscle.ExternalId, 100, item.Name);
            RequireMax("Muscle.Slug", muscle.Slug, 100, item.Name);
            RequireMax("Muscle.Name", muscle.Name, 150, item.Name);
            RequireMax("Muscle.Role", muscle.Role, 30, item.Name);
        }

        foreach (var equipment in item.Equipment)
        {
            RequireMax("Equipment.ExternalId", equipment.ExternalId, 100, item.Name);
            RequireMax("Equipment.Name", equipment.Name, 150, item.Name);
            RequireMax("Equipment.Type", equipment.Type, 80, item.Name);
            RequireMax("Equipment.UsageType", equipment.UsageType, 80, item.Name);
        }
    }
}

var server = GetRequiredArg(args, "--server");
var database = GetRequiredArg(args, "--database");
var snapshotDirectory = GetRequiredArg(args, "--snapshot-directory");

var token = Environment.GetEnvironmentVariable("AZURE_SQL_ACCESS_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    throw new InvalidOperationException(
        "AZURE_SQL_ACCESS_TOKEN is not set. Use the 12WR Azure SQL auth wrapper.");
}

try
{
    var exercisesPath = Path.Combine(snapshotDirectory, "exercises.json");
    var manifestPath = Path.Combine(snapshotDirectory, "snapshot-manifest.json");
    var licensePath = Path.Combine(snapshotDirectory, "LICENSE-KINETIC.txt");

    foreach (var path in new[] { exercisesPath, manifestPath, licensePath })
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Required Kinetic snapshot file is missing.",
                path);
        }
    }

    var licenseText = await File.ReadAllTextAsync(licensePath);
    if (!licenseText.Contains("MIT License", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            "Kinetic license snapshot does not contain the MIT License marker.");
    }

    using var manifest =
        JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));

    var root = manifest.RootElement;

    var source = root.GetProperty("source").GetString();
    var sourceBranch = root.GetProperty("source_branch").GetString();
    var license = root.GetProperty("license").GetString();
    var capturedUtcText = root.GetProperty("captured_utc").GetString();
    var expectedExerciseCount =
        root.GetProperty("exercise_count").GetInt32();

    var expectedExercisesSha =
        root.GetProperty("files")
            .GetProperty("exercises.json")
            .GetProperty("sha256")
            .GetString();

    if (!string.Equals(license, "MIT", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            $"Unexpected Kinetic license in manifest: {license}");
    }

    if (string.IsNullOrWhiteSpace(source) ||
        !source.Contains(
            "kinetic-place/exercises-db",
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            $"Unexpected Kinetic source in manifest: {source}");
    }

    if (!DateTime.TryParse(
            capturedUtcText,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var capturedUtc))
    {
        throw new InvalidDataException(
            $"Invalid captured_utc value in snapshot manifest: {capturedUtcText}");
    }

    capturedUtc = capturedUtc.ToUniversalTime();

    var actualExercisesSha = Sha256File(exercisesPath);

    if (!string.Equals(
            actualExercisesSha,
            expectedExercisesSha,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            "exercises.json SHA-256 does not match snapshot-manifest.json.");
    }

    var reader = new KineticExerciseCatalogReader();
    var snapshot = reader.ReadFile(exercisesPath);

    if (snapshot.Exercises.Count != expectedExerciseCount)
    {
        throw new InvalidDataException(
            $"Manifest says {expectedExerciseCount} exercises, " +
            $"but the 12WR Kinetic adapter produced {snapshot.Exercises.Count}.");
    }

    if (snapshot.Exercises.Count < 899)
    {
        throw new InvalidDataException(
            $"Kinetic snapshot contains only {snapshot.Exercises.Count} exercises.");
    }

    var duplicateExternalIds =
        snapshot.Exercises
            .GroupBy(
                x => x.SourceExternalId,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Take(10)
            .ToArray();

    if (duplicateExternalIds.Length > 0)
    {
        throw new InvalidDataException(
            "Kinetic snapshot contains duplicate exercise IDs: " +
            string.Join(", ", duplicateExternalIds));
    }

    var duplicatePrimaryIds =
        snapshot.Exercises
            .GroupBy(x => x.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Take(10)
            .ToArray();

    if (duplicatePrimaryIds.Length > 0)
    {
        throw new InvalidDataException(
            "Kinetic snapshot contains duplicate primary GUIDs: " +
            string.Join(", ", duplicatePrimaryIds));
    }

    var manifestSha = Sha256File(manifestPath);

    var sourceVersion =
        $"{sourceBranch ?? "main"}/{actualExercisesSha[..12]}";

    var provenance =
        new ExerciseCatalogProvenance(
            SourceCode: ExerciseCatalogSources.Kinetic,
            LicenseCode: "MIT",
            SourceVersion: sourceVersion,
            ManifestSha256: manifestSha,
            CapturedAtUtc: capturedUtc);

    ValidateCatalogForSql(snapshot, provenance);

    Console.WriteLine("Kinetic snapshot: VERIFIED");
    Console.WriteLine($"Exercises:        {snapshot.Exercises.Count}");
    Console.WriteLine($"Source version:   {sourceVersion}");
    Console.WriteLine($"Manifest SHA-256: {manifestSha}");
    Console.WriteLine("SQL column-length prevalidation: PASS");

    var connectionString =
        $"Server=tcp:{server},1433;" +
        $"Database={database};" +
        "Encrypt=True;" +
        "TrustServerCertificate=False;" +
        "Connection Timeout=30;";

    await using var connection =
        new SqlConnection(connectionString)
        {
            AccessToken = token
        };

    await connection.OpenAsync();

    await using (var verify = connection.CreateCommand())
    {
        verify.CommandText =
            """
            SELECT
                DB_NAME() AS database_name,
                CASE WHEN OBJECT_ID(N'app.ExerciseCatalogItems', N'U') IS NULL
                     THEN 0 ELSE 1 END AS catalog_ready;
            """;

        await using var readerVerify = await verify.ExecuteReaderAsync();

        if (!await readerVerify.ReadAsync())
        {
            throw new InvalidOperationException(
                "TEST SQL verification query returned no row.");
        }

        var actualDatabase = readerVerify.GetString(0);
        var catalogReady = readerVerify.GetInt32(1) == 1;

        if (!string.Equals(
                actualDatabase,
                database,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Connected to unexpected database: {actualDatabase}");
        }

        if (!catalogReady)
        {
            throw new InvalidOperationException(
                "app.ExerciseCatalogItems does not exist. " +
                "Migration 030 must be applied before catalog import.");
        }
    }

    var options =
        new DbContextOptionsBuilder<RecompDbContext>()
            .UseSqlServer(
                connection,
                sql =>
                {
                    sql.CommandTimeout(300);
                    sql.MaxBatchSize(50);
                })
            .Options;

    await using var db = new RecompDbContext(options);

    Console.WriteLine("SQL command timeout for catalog import: 300 seconds");
    Console.WriteLine("EF SQL batch size for catalog import: 50");

    var beforeImports = await db.ExerciseCatalogImports.CountAsync();
    var beforeItems = await db.ExerciseCatalogItems.CountAsync();

    Console.WriteLine($"Catalog rows before import: {beforeItems}");
    Console.WriteLine($"Import records before import: {beforeImports}");

    var store = new SqlExerciseCatalogStore(db);

    ExerciseCatalogImportResult result;

    try
    {
        result =
            await store.ImportSnapshotAsync(snapshot, provenance);
    }
    catch (DbUpdateException ex)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("DATABASE SAVE FAILED - FULL EF/SQL DETAIL");
        Console.Error.WriteLine("========================================");
        Console.Error.WriteLine(ex.ToString());
        throw;
    }
    catch (SqlException ex)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("SQL ERROR - FULL DETAIL");
        Console.Error.WriteLine("=======================");
        Console.Error.WriteLine(ex.ToString());
        throw;
    }

    var activeCount =
        await db.ExerciseCatalogItems.CountAsync(
            row =>
                row.SourceCode == ExerciseCatalogSources.Kinetic &&
                row.IsActive);

    var muscleRows =
        await db.ExerciseMuscleTargets
            .Join(
                db.ExerciseCatalogItems,
                muscle => muscle.ExerciseCatalogItemId,
                exercise => exercise.ExerciseCatalogItemId,
                (muscle, exercise) => new { muscle, exercise })
            .CountAsync(
                row =>
                    row.exercise.SourceCode ==
                    ExerciseCatalogSources.Kinetic);

    var equipmentRows =
        await db.ExerciseEquipmentRequirements
            .Join(
                db.ExerciseCatalogItems,
                equipment => equipment.ExerciseCatalogItemId,
                exercise => exercise.ExerciseCatalogItemId,
                (equipment, exercise) => new { equipment, exercise })
            .CountAsync(
                row =>
                    row.exercise.SourceCode ==
                    ExerciseCatalogSources.Kinetic);

    if (activeCount != snapshot.Exercises.Count)
    {
        throw new InvalidOperationException(
            "Post-import active catalog count mismatch. " +
            $"Expected {snapshot.Exercises.Count}, found {activeCount}.");
    }

    if (muscleRows <= 0)
    {
        throw new InvalidOperationException(
            "Post-import Kinetic muscle-target count is zero.");
    }

    if (equipmentRows <= 0)
    {
        throw new InvalidOperationException(
            "Post-import Kinetic equipment count is zero.");
    }

    Console.WriteLine();
    Console.WriteLine("KINETIC CATALOG IMPORT: GREEN");
    Console.WriteLine($"Import ID:        {result.ImportId}");
    Console.WriteLine($"Added:            {result.Added}");
    Console.WriteLine($"Updated:          {result.Updated}");
    Console.WriteLine($"Deactivated:      {result.Deactivated}");
    Console.WriteLine($"Active exercises: {activeCount}");
    Console.WriteLine($"Muscle rows:      {muscleRows}");
    Console.WriteLine($"Equipment rows:   {equipmentRows}");
    Console.WriteLine("Runtime Kinetic API dependency: NONE");
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("KINETIC IMPORT FAILED");
    Console.Error.WriteLine("=====================");
    Console.Error.WriteLine(ex.ToString());
    Environment.ExitCode = 1;
}
