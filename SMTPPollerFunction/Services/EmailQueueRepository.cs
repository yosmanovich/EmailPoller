using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SMTPPoller.Models;

namespace SMTPPoller.Services;

/// <summary>
/// Repository for email queue database operations via stored procedures.
/// </summary>
public class EmailQueueRepository : IEmailQueueRepository
{
    private readonly string _connectionString;
    private readonly ILogger<EmailQueueRepository> _logger;

    public EmailQueueRepository(IConfiguration configuration, ILogger<EmailQueueRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("SqlConnectionString")
            ?? configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException("SqlConnectionString is not configured");
        _logger = logger;
    }

    /// <summary>
    /// Atomically claims pending emails by calling dbo.EmailQueue_ClaimBatch.
    /// Returns emails already marked as 'Processing'.
    /// </summary>
    public async Task<IReadOnlyList<EmailQueueRecord>> GetPendingEmailsAsync(int maxRecords = 100)
    {
        _logger.LogDebug("Claiming up to {MaxRecords} pending emails from queue", maxRecords);

        var emails = new List<EmailQueueRecord>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("dbo.EmailQueue_ClaimBatch", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@MaxRecords", maxRecords);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            emails.Add(new EmailQueueRecord
            {
                EmailQueueId = reader.GetInt32(reader.GetOrdinal("EmailQueueId")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
                ProcessedDate = reader.IsDBNull(reader.GetOrdinal("ProcessedDate")) ? null : reader.GetDateTime(reader.GetOrdinal("ProcessedDate")),
                RetryCount = reader.GetInt32(reader.GetOrdinal("RetryCount")),
                MaxRetries = reader.GetInt32(reader.GetOrdinal("MaxRetries")),
                ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? null : reader.GetString(reader.GetOrdinal("ErrorMessage")),
                MailItemId = reader.IsDBNull(reader.GetOrdinal("MailItemId")) ? null : reader.GetInt32(reader.GetOrdinal("MailItemId")),
                ProfileName = reader.IsDBNull(reader.GetOrdinal("ProfileName")) ? null : reader.GetString(reader.GetOrdinal("ProfileName")),
                Recipients = reader.GetString(reader.GetOrdinal("Recipients")),
                CopyRecipients = reader.IsDBNull(reader.GetOrdinal("CopyRecipients")) ? null : reader.GetString(reader.GetOrdinal("CopyRecipients")),
                BlindCopyRecipients = reader.IsDBNull(reader.GetOrdinal("BlindCopyRecipients")) ? null : reader.GetString(reader.GetOrdinal("BlindCopyRecipients")),
                FromAddress = reader.IsDBNull(reader.GetOrdinal("FromAddress")) ? null : reader.GetString(reader.GetOrdinal("FromAddress")),
                ReplyTo = reader.IsDBNull(reader.GetOrdinal("ReplyTo")) ? null : reader.GetString(reader.GetOrdinal("ReplyTo")),
                Subject = reader.IsDBNull(reader.GetOrdinal("Subject")) ? null : reader.GetString(reader.GetOrdinal("Subject")),
                Body = reader.IsDBNull(reader.GetOrdinal("Body")) ? null : reader.GetString(reader.GetOrdinal("Body")),
                BodyFormat = reader.IsDBNull(reader.GetOrdinal("BodyFormat")) ? null : reader.GetString(reader.GetOrdinal("BodyFormat")),
                Importance = reader.IsDBNull(reader.GetOrdinal("Importance")) ? null : reader.GetString(reader.GetOrdinal("Importance")),
                Sensitivity = reader.IsDBNull(reader.GetOrdinal("Sensitivity")) ? null : reader.GetString(reader.GetOrdinal("Sensitivity")),
                FileAttachments = reader.IsDBNull(reader.GetOrdinal("FileAttachments")) ? null : reader.GetString(reader.GetOrdinal("FileAttachments"))
            });
        }

        _logger.LogInformation("Claimed {Count} emails from queue", emails.Count);
        return emails;
    }

    /// <summary>
    /// Marks an email as processing by calling dbo.EmailQueue_Processing.
    /// </summary>
    public async Task MarkAsProcessingAsync(int emailQueueId)
    {
        _logger.LogDebug("Marking email {EmailQueueId} as Processing", emailQueueId);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("dbo.EmailQueue_Processing", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@EmailQueueId", emailQueueId);

        await command.ExecuteNonQueryAsync();

        _logger.LogInformation("Email {EmailQueueId} marked as Processing", emailQueueId);
    }

    /// <summary>
    /// Marks an email as successfully sent by calling dbo.EmailQueue_Success.
    /// Removes the email from the queue.
    /// </summary>
    public async Task MarkAsSuccessAsync(int emailQueueId)
    {
        _logger.LogDebug("Marking email {EmailQueueId} as Success", emailQueueId);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("dbo.EmailQueue_Success", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@EmailQueueId", emailQueueId);

        await command.ExecuteNonQueryAsync();

        _logger.LogInformation("Email {EmailQueueId} processed successfully and removed from queue", emailQueueId);
    }

    /// <summary>
    /// Handles email failure by calling dbo.EmailQueue_Failure.
    /// Increments retry count and sets status based on retry limit.
    /// </summary>
    public async Task MarkAsFailureAsync(int emailQueueId, string errorMessage)
    {
        _logger.LogDebug("Marking email {EmailQueueId} as Failed with error: {ErrorMessage}", 
            emailQueueId, errorMessage);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("dbo.EmailQueue_Failure", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@EmailQueueId", emailQueueId);
        command.Parameters.AddWithValue("@ErrorMessage", errorMessage ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync();

        _logger.LogWarning("Email {EmailQueueId} marked as Failed", emailQueueId);
    }
}
