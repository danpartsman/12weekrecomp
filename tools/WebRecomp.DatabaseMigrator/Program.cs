using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;

const string connectionStringVariable = "RECOMP_RUNTIME_CONNECTION_STRING";
const string tokenVariable = "AZURE_SQL_ACCESS_TOKEN";

var runtimeConnectionString = Environment.GetEnvironmentVariable(connectionStringVariable);
var accessToken = Environment.GetEnvironmentVariable(tokenVariable);
var configuredAuthentication = args.Contains("--configured-auth", StringComparer.Ordinal);
var sampleData = args.Contains("--test-sample-data", StringComparer.Ordinal);
var accountability = args.Contains("--accountability", StringComparer.Ordinal);
var verifyAccountability = args.Contains("--verify-accountability", StringComparer.Ordinal);
var learningMigration = args.Contains("--learning-coach", StringComparer.Ordinal);
if (args.Any(x => x is not ("--learning-coach" or "--configured-auth" or "--test-sample-data" or "--accountability" or "--verify-accountability"))
    || (configuredAuthentication && !learningMigration && !sampleData && !accountability && !verifyAccountability)
    || (sampleData && (!configuredAuthentication || learningMigration || accountability))
    || (accountability && (learningMigration || sampleData || verifyAccountability))
    || (verifyAccountability && (!configuredAuthentication || learningMigration || sampleData)))
{
    Console.Error.WriteLine("Supported options: --learning-coach [--configured-auth], or --test-sample-data --configured-auth, or --accountability [--configured-auth], or --verify-accountability --configured-auth.");
    return 5;
}

if (string.IsNullOrWhiteSpace(runtimeConnectionString))
{
    Console.Error.WriteLine($"Required environment variable {connectionStringVariable} is missing.");
    return 2;
}

if (!configuredAuthentication && string.IsNullOrWhiteSpace(accessToken))
{
    try
    {
        var credential = new AzureCliCredential();
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://database.windows.net/.default"]));
        accessToken = token.Token;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"Could not obtain Azure SQL access token from Azure CLI: {exception.Message}");
        return 3;
    }
}

var runtimeBuilder = new SqlConnectionStringBuilder(runtimeConnectionString);

if (string.IsNullOrWhiteSpace(runtimeBuilder.DataSource) ||
    string.IsNullOrWhiteSpace(runtimeBuilder.InitialCatalog))
{
    Console.Error.WriteLine("The runtime connection string does not contain both a SQL server and database name.");
    return 4;
}

if ((sampleData || accountability || verifyAccountability) && (!string.Equals(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"), "recomp-test", StringComparison.OrdinalIgnoreCase)
    || !string.Equals(runtimeBuilder.InitialCatalog, Environment.GetEnvironmentVariable("RECOMP_EXPECTED_TEST_DATABASE"), StringComparison.Ordinal)))
{
    Console.Error.WriteLine("This migration requires the recomp-test App Service and its verified, isolated TEST database.");
    return 6;
}

var migrationBuilder = new SqlConnectionStringBuilder
{
    DataSource = runtimeBuilder.DataSource,
    InitialCatalog = runtimeBuilder.InitialCatalog,
    Encrypt = true,
    TrustServerCertificate = false,
    ConnectTimeout = 30,
    Pooling = false
};

const string localAuthMigrationSql = """
SET XACT_ABORT ON;

IF SCHEMA_ID(N'app') IS NULL
    THROW 50001, 'Required app schema does not exist.', 1;

IF OBJECT_ID(N'app.LocalAuthCredentials', N'U') IS NULL
BEGIN
    CREATE TABLE app.LocalAuthCredentials
    (
        UserId uniqueidentifier NOT NULL,
        PasswordHash nvarchar(512) NOT NULL,
        GlobalRoleCode varchar(20) NOT NULL
            CONSTRAINT DF_LocalAuthCredentials_GlobalRoleCode DEFAULT ('CLIENT'),
        FailedSignInCount int NOT NULL
            CONSTRAINT DF_LocalAuthCredentials_FailedSignInCount DEFAULT (0),
        LockoutEndUtc datetime2(3) NULL,
        LastSignInAtUtc datetime2(3) NULL,
        CreatedAtUtc datetime2(3) NOT NULL,
        UpdatedAtUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_LocalAuthCredentials PRIMARY KEY (UserId),
        CONSTRAINT FK_LocalAuthCredentials_UserAccounts
            FOREIGN KEY (UserId) REFERENCES app.UserAccounts(user_id)
    );
END;

IF OBJECT_ID(N'app.LocalAuthCredentials', N'U') IS NULL
    THROW 50002, 'Local auth credential table migration did not complete.', 1;

IF DATABASE_PRINCIPAL_ID(N'web_recomp_app') IS NULL
    THROW 50003, 'Required database role web_recomp_app does not exist.', 1;

IF SCHEMA_ID(N'nutrition') IS NULL
    THROW 50004, 'Required nutrition schema does not exist.', 1;

GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::nutrition TO web_recomp_app;
""";

// The learning migration is additive and idempotent. Run it with the
// deployment identity, never by granting DDL to the runtime web identity.
var migrationSql = verifyAccountability
    ? "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'app') AND name IN (N'CoachAccountabilityAgreements', N'CoachAccountabilityApprovals', N'CoachAccountabilityEvents');"
    : accountability
    ? await File.ReadAllTextAsync(Path.Combine("database", "047_coach_accountability.sql"))
    : sampleData
    ? await File.ReadAllTextAsync(Path.Combine("database", "test_sample_month.sql"))
    : learningMigration
    ? await File.ReadAllTextAsync(Path.Combine("database", "043_v1015c_learning_coach_foundation.sql"))
    : localAuthMigrationSql;
if (learningMigration)
{
    migrationSql += "\n" + await File.ReadAllTextAsync(Path.Combine("database", "044_coach_center_foundation.sql"));
    migrationSql += "\n" + await File.ReadAllTextAsync(Path.Combine("database", "045_coach_prescription_rules.sql"));
    migrationSql += "\n" + await File.ReadAllTextAsync(Path.Combine("database", "046_coach_workout_plan_revisions.sql"));
}

Console.WriteLine("Starting 12WR database migration.");
try
{
    // The optional path reuses the existing configured database identity. It
    // does not create credentials or grant any new database permissions.
    await using var connection = new SqlConnection(
        configuredAuthentication ? runtimeConnectionString : migrationBuilder.ConnectionString);
    if (!configuredAuthentication)
        connection.AccessToken = accessToken;

    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = migrationSql;
    command.CommandTimeout = 60;
    if (verifyAccountability)
    {
        var tables = Convert.ToInt32(await command.ExecuteScalarAsync());
        Console.WriteLine($"Accountability tables present: {tables}/3.");
        if (tables != 3)
        {
            Console.Error.WriteLine("A database administrator must apply database/047_coach_accountability.sql to TEST before deployment. Runtime permissions remain unchanged.");
            return 7;
        }
        Console.WriteLine("ACCOUNTABILITY_SCHEMA_READY");
        return 0;
    }
    if (sampleData || accountability)
    {
        command.Parameters.AddWithValue("@ExpectedDatabase", runtimeBuilder.InitialCatalog);
        await using var reader = await command.ExecuteReaderAsync();
        do
        {
            while (await reader.ReadAsync())
                for (var i = 0; i < reader.FieldCount; i++) Console.WriteLine($"{reader.GetName(i)}={reader.GetValue(i)}");
        } while (await reader.NextResultAsync());
        Console.WriteLine(accountability ? "TEST accountability schema completed and verified. Existing entries were retained." : "TEST sample-data batch completed and verified. Existing entries were retained.");
        return 0;
    }
    await command.ExecuteNonQueryAsync();

    Console.WriteLine(
        $"12WR database migration complete for {runtimeBuilder.InitialCatalog}; {(learningMigration ? "learning coach schema is present" : "local auth schema and nutrition runtime grants are present")}.");
    return 0;
}
catch (SqlException exception)
{
    Console.Error.WriteLine(
        $"12WR database migration failed for {runtimeBuilder.InitialCatalog}: SQL {exception.Number}: {exception.Message}");
    return 10;
}
