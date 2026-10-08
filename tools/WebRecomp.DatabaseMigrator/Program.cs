using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;

const string connectionStringVariable = "RECOMP_RUNTIME_CONNECTION_STRING";
const string tokenVariable = "AZURE_SQL_ACCESS_TOKEN";

var runtimeConnectionString = Environment.GetEnvironmentVariable(connectionStringVariable);
var accessToken = Environment.GetEnvironmentVariable(tokenVariable);

if (string.IsNullOrWhiteSpace(runtimeConnectionString))
{
    Console.Error.WriteLine($"Required environment variable {connectionStringVariable} is missing.");
    return 2;
}

if (string.IsNullOrWhiteSpace(accessToken))
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

var learningMigration = args.Length == 1 && args[0] == "--learning-coach";
if (args.Length > 0 && !learningMigration)
{
    Console.Error.WriteLine("Supported migration option: --learning-coach.");
    return 5;
}

// The learning migration is additive and idempotent. Run it with the
// deployment identity, never by granting DDL to the runtime web identity.
var migrationSql = learningMigration
    ? await File.ReadAllTextAsync(Path.Combine("database", "043_v1015c_learning_coach_foundation.sql"))
    : localAuthMigrationSql;

try
{
    await using var connection = new SqlConnection(migrationBuilder.ConnectionString)
    {
        AccessToken = accessToken
    };

    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = migrationSql;
    command.CommandTimeout = 60;
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
