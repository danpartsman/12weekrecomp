using Microsoft.Data.SqlClient;

namespace TwelveWeekRecomp.Live.Services;

public sealed record LaunchInterestUpsertResult(
    long LaunchInterestId,
    Guid UnsubscribeToken,
    bool IsNew,
    bool Reactivated);

public sealed record LaunchInterestRow(
    long LaunchInterestId,
    string Name,
    string Email,
    string InterestCode,
    string EmailStatus,
    string SignupSource,
    int SubmissionCount,
    DateTime FirstSubmittedUtc,
    DateTime LastSubmittedUtc,
    DateTime? UnsubscribedUtc,
    string WelcomeEmailStatus,
    DateTime? WelcomeEmailSentUtc,
    Guid UnsubscribeToken);

public sealed record LaunchInterestSummary(
    int Total,
    int Active,
    int FreeUpdates,
    int FoundersRate,
    int Unsubscribed);

public sealed record BroadcastRecipient(
    long LaunchInterestId,
    string Name,
    string Email,
    Guid UnsubscribeToken);

public sealed record EmailBroadcastHistoryRow(
    long EmailBroadcastId,
    string Subject,
    string AudienceCode,
    string StatusCode,
    int RecipientCount,
    int SentCount,
    int FailedCount,
    string? CreatedBy,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

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

    public async Task<LaunchInterestUpsertResult> UpsertAsync(
        string name,
        string email,
        string interestCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var normalized = email.Trim().ToLowerInvariant();

        const string selectSql = """
            SELECT TOP (1)
                LaunchInterestId,
                UnsubscribeToken,
                EmailStatus
            FROM app.LaunchInterests
            WHERE EmailNormalized = @EmailNormalized;
            """;

        await using (var select = new SqlCommand(selectSql, connection))
        {
            select.Parameters.AddWithValue("@EmailNormalized", normalized);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                var token = reader.GetGuid(1);
                var oldStatus = reader.GetString(2);
                await reader.DisposeAsync();

                const string updateExistingSql = """
                    UPDATE app.LaunchInterests
                       SET Name = @Name,
                           Email = @Email,
                           InterestCode = @InterestCode,
                           LastSubmittedUtc = SYSUTCDATETIME(),
                           SubmissionCount = SubmissionCount + 1,
                           EmailStatus = 'ACTIVE',
                           UnsubscribedUtc = NULL,
                           SignupSource = 'PRELAUNCH_WEB',
                           ConsentVersion = 'PRELAUNCH_V1'
                     WHERE LaunchInterestId = @LaunchInterestId;
                    """;

                await using var update = new SqlCommand(updateExistingSql, connection);
                update.Parameters.AddWithValue("@Name", name.Trim());
                update.Parameters.AddWithValue("@Email", email.Trim());
                update.Parameters.AddWithValue("@InterestCode", interestCode);
                update.Parameters.AddWithValue("@LaunchInterestId", id);
                await update.ExecuteNonQueryAsync(cancellationToken);

                return new(id, token, false,
                    string.Equals(oldStatus, "UNSUBSCRIBED", StringComparison.OrdinalIgnoreCase));
            }
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
                SubmissionCount,
                EmailStatus,
                SignupSource,
                ConsentVersion,
                UnsubscribeToken,
                WelcomeEmailStatus
            )
            OUTPUT INSERTED.LaunchInterestId, INSERTED.UnsubscribeToken
            VALUES
            (
                @Name,
                @Email,
                @EmailNormalized,
                @InterestCode,
                SYSUTCDATETIME(),
                SYSUTCDATETIME(),
                1,
                'ACTIVE',
                'PRELAUNCH_WEB',
                'PRELAUNCH_V1',
                NEWID(),
                'NOT_SENT'
            );
            """;

        try
        {
            await using var insert = new SqlCommand(insertSql, connection);
            AddInsertParameters(insert, name, email, interestCode);
            await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new(reader.GetInt64(0), reader.GetGuid(1), true, false);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            _logger.LogInformation(
                "Duplicate launch-interest email inserted concurrently; retrying update path.");

            return await UpsertAsync(name, email, interestCode, cancellationToken);
        }
    }

    public async Task MarkWelcomeEmailAsync(
        long launchInterestId,
        bool sent,
        string? error,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        const string sql = """
            UPDATE app.LaunchInterests
               SET WelcomeEmailStatus = @Status,
                   WelcomeEmailSentUtc = CASE WHEN @Status = 'SENT' THEN SYSUTCDATETIME() ELSE WelcomeEmailSentUtc END,
                   WelcomeEmailLastError = @Error
             WHERE LaunchInterestId = @Id;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Status", sent ? "SENT" : "FAILED");
        command.Parameters.AddWithValue("@Error", (object?)TrimError(error) ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", launchInterestId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> UnsubscribeAsync(
        Guid token,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        const string sql = """
            UPDATE app.LaunchInterests
               SET EmailStatus = 'UNSUBSCRIBED',
                   UnsubscribedUtc = COALESCE(UnsubscribedUtc, SYSUTCDATETIME())
             WHERE UnsubscribeToken = @Token
               AND EmailStatus <> 'UNSUBSCRIBED';
            SELECT @@ROWCOUNT;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Token", token);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    public async Task<LaunchInterestSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        const string sql = """
            SELECT
                COUNT(*) AS Total,
                SUM(CASE WHEN EmailStatus = 'ACTIVE' THEN 1 ELSE 0 END) AS Active,
                SUM(CASE WHEN EmailStatus = 'ACTIVE' AND InterestCode = 'FREE_UPDATES' THEN 1 ELSE 0 END) AS FreeUpdates,
                SUM(CASE WHEN EmailStatus = 'ACTIVE' AND InterestCode = 'FOUNDERS_RATE' THEN 1 ELSE 0 END) AS FoundersRate,
                SUM(CASE WHEN EmailStatus = 'UNSUBSCRIBED' THEN 1 ELSE 0 END) AS Unsubscribed
            FROM app.LaunchInterests;
            """;
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(
            reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            reader.IsDBNull(4) ? 0 : reader.GetInt32(4));
    }

    public async Task<IReadOnlyList<LaunchInterestRow>> ListAsync(
        string? search,
        string? interestCode,
        string? emailStatus,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        const string sql = """
            SELECT TOP (1000)
                LaunchInterestId,
                Name,
                Email,
                InterestCode,
                EmailStatus,
                SignupSource,
                SubmissionCount,
                FirstSubmittedUtc,
                LastSubmittedUtc,
                UnsubscribedUtc,
                WelcomeEmailStatus,
                WelcomeEmailSentUtc,
                UnsubscribeToken
            FROM app.LaunchInterests
            WHERE (@Search IS NULL OR Name LIKE '%' + @Search + '%' OR Email LIKE '%' + @Search + '%')
              AND (@InterestCode IS NULL OR InterestCode = @InterestCode)
              AND (@EmailStatus IS NULL OR EmailStatus = @EmailStatus)
            ORDER BY LastSubmittedUtc DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Search", DbValue(search));
        command.Parameters.AddWithValue("@InterestCode", DbValue(interestCode));
        command.Parameters.AddWithValue("@EmailStatus", DbValue(emailStatus));

        var rows = new List<LaunchInterestRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetDateTime(7),
                reader.GetDateTime(8),
                reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                reader.GetGuid(12)));
        }
        return rows;
    }

    public async Task<IReadOnlyList<BroadcastRecipient>> GetRecipientsAsync(
        string audienceCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        const string sql = """
            SELECT LaunchInterestId, Name, Email, UnsubscribeToken
            FROM app.LaunchInterests
            WHERE EmailStatus = 'ACTIVE'
              AND (
                    @AudienceCode = 'ALL_ACTIVE'
                    OR InterestCode = @AudienceCode
                  )
            ORDER BY LaunchInterestId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@AudienceCode", audienceCode);
        var rows = new List<BroadcastRecipient>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3)));
        }
        return rows;
    }


    public async Task<IReadOnlyList<EmailBroadcastHistoryRow>> GetBroadcastHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        const string sql = """
            SELECT TOP (200)
                EmailBroadcastId, Subject, AudienceCode, StatusCode,
                RecipientCount, SentCount, FailedCount, CreatedBy, CreatedAtUtc, CompletedAtUtc
            FROM app.EmailBroadcasts
            ORDER BY EmailBroadcastId DESC;
            """;
        await using var command = new SqlCommand(sql, connection);
        var rows = new List<EmailBroadcastHistoryRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetDateTime(8),
                reader.IsDBNull(9) ? null : reader.GetDateTime(9)));
        }
        return rows;
    }

    public async Task<long> CreateBroadcastAsync(
        string subject,
        string messageText,
        string audienceCode,
        int recipientCount,
        string? createdBy,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        const string sql = """
            INSERT INTO app.EmailBroadcasts
            (Subject, MessageText, AudienceCode, StatusCode, RecipientCount, SentCount, FailedCount, CreatedBy, CreatedAtUtc)
            OUTPUT INSERTED.EmailBroadcastId
            VALUES
            (@Subject, @MessageText, @AudienceCode, 'SENDING', @RecipientCount, 0, 0, @CreatedBy, SYSUTCDATETIME());
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Subject", subject.Trim());
        command.Parameters.AddWithValue("@MessageText", messageText.Trim());
        command.Parameters.AddWithValue("@AudienceCode", audienceCode);
        command.Parameters.AddWithValue("@RecipientCount", recipientCount);
        command.Parameters.AddWithValue("@CreatedBy", DbValue(createdBy));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task RecordDeliveryAsync(
        long broadcastId,
        BroadcastRecipient recipient,
        string subject,
        bool sent,
        string? error,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        const string sql = """
            INSERT INTO app.EmailDeliveries
            (EmailBroadcastId, LaunchInterestId, RecipientEmail, Subject, StatusCode, AttemptedAtUtc, SentAtUtc, ErrorMessage)
            VALUES
            (@BroadcastId, @LaunchInterestId, @RecipientEmail, @Subject, @StatusCode, SYSUTCDATETIME(),
             CASE WHEN @StatusCode = 'SENT' THEN SYSUTCDATETIME() ELSE NULL END,
             @ErrorMessage);
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BroadcastId", broadcastId);
        command.Parameters.AddWithValue("@LaunchInterestId", recipient.LaunchInterestId);
        command.Parameters.AddWithValue("@RecipientEmail", recipient.Email);
        command.Parameters.AddWithValue("@Subject", subject.Trim());
        command.Parameters.AddWithValue("@StatusCode", sent ? "SENT" : "FAILED");
        command.Parameters.AddWithValue("@ErrorMessage", (object?)TrimError(error) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteBroadcastAsync(
        long broadcastId,
        int sentCount,
        int failedCount,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var status = failedCount == 0 ? "COMPLETED" : sentCount > 0 ? "PARTIAL" : "FAILED";
        const string sql = """
            UPDATE app.EmailBroadcasts
               SET StatusCode = @StatusCode,
                   SentCount = @SentCount,
                   FailedCount = @FailedCount,
                   CompletedAtUtc = SYSUTCDATETIME()
             WHERE EmailBroadcastId = @BroadcastId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@StatusCode", status);
        command.Parameters.AddWithValue("@SentCount", sentCount);
        command.Parameters.AddWithValue("@FailedCount", failedCount);
        command.Parameters.AddWithValue("@BroadcastId", broadcastId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("LiveDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:LiveDb is not configured.");

        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddInsertParameters(
        SqlCommand command,
        string name,
        string email,
        string interestCode)
    {
        command.Parameters.AddWithValue("@Name", name.Trim());
        command.Parameters.AddWithValue("@Email", email.Trim());
        command.Parameters.AddWithValue("@EmailNormalized", email.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("@InterestCode", interestCode);
    }

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string? TrimError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length <= 1800 ? value : value[..1800];
    }
}
