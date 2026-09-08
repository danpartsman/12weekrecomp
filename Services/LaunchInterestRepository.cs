using Microsoft.Data.SqlClient;

namespace TwelveWeekRecomp.Live.Services;

public sealed class LaunchInterestRepository
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LaunchInterestRepository> _logger;

    public LaunchInterestRepository(
        IConfiguration configuration,
        ILogger<LaunchInterestRepository> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task UpsertAsync(
        string name,
        string email,
        string interestCode,
        CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString("LiveDb");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:LiveDb is not configured.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string updateSql = """
            UPDATE app.LaunchInterests
               SET Name = @Name,
                   Email = @Email,
                   InterestCode = @InterestCode,
                   LastSubmittedUtc = SYSUTCDATETIME(),
                   SubmissionCount = SubmissionCount + 1
             WHERE EmailNormalized = @EmailNormalized;
            SELECT @@ROWCOUNT;
            """;

        await using (var update = new SqlCommand(updateSql, connection))
        {
            AddParameters(update, name, email, interestCode);

            var rows = Convert.ToInt32(
                await update.ExecuteScalarAsync(cancellationToken));

            if (rows > 0)
                return;
        }

        const string insertSql = """
            INSERT INTO app.LaunchInterests
            (
                Name,
                Email,
                EmailNormalized,
                InterestCode,
                FirstSubmittedUtc,
                LastSubmittedUtc,
                SubmissionCount
            )
            VALUES
            (
                @Name,
                @Email,
                @EmailNormalized,
                @InterestCode,
                SYSUTCDATETIME(),
                SYSUTCDATETIME(),
                1
            );
            """;

        try
        {
            await using var insert = new SqlCommand(insertSql, connection);
            AddParameters(insert, name, email, interestCode);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            _logger.LogInformation(
                "Duplicate launch-interest email inserted concurrently; updating existing row.");

            await using var retry = new SqlCommand(updateSql, connection);
            AddParameters(retry, name, email, interestCode);
            await retry.ExecuteScalarAsync(cancellationToken);
        }
    }

    private static void AddParameters(
        SqlCommand command,
        string name,
        string email,
        string interestCode)
    {
        command.Parameters.AddWithValue("@Name", name.Trim());
        command.Parameters.AddWithValue("@Email", email.Trim());
        command.Parameters.AddWithValue(
            "@EmailNormalized",
            email.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("@InterestCode", interestCode);
    }
}
