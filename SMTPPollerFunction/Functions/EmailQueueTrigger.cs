using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SMTPPoller.Models;
using SMTPPoller.Services;

namespace SMTPPoller.Functions;

/// <summary>
/// Azure Function triggered by database inserts to the EmailQueue table.
/// Processes email queue items by sending via SMTP relay.
/// </summary>
public class EmailQueueTrigger
{
    private readonly ILogger<EmailQueueTrigger> _logger;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly IEmailQueueRepository _emailQueueRepository;

    public EmailQueueTrigger(
        ILogger<EmailQueueTrigger> logger,
        IConfiguration configuration,
        IEmailService emailService,
        IEmailQueueRepository emailQueueRepository)
    {
        _logger = logger;
        _configuration = configuration;
        _emailService = emailService;
        _emailQueueRepository = emailQueueRepository;
    }

    /// <summary>
    /// Function triggered when new records are inserted into the EmailQueue table.
    /// 
    /// Configuration (from App Settings):
    /// - SqlConnectionString: Connection string to the Azure SQL database
    /// - MonitoredTableName: Name of the table to monitor (e.g., "dbo.EmailQueue")
    /// - SmtpHost: SMTP relay server hostname
    /// - SmtpPort: SMTP port (default: 25)
    /// - SmtpEnableSsl: Enable SSL/TLS (default: false)
    /// - SmtpUsername: SMTP username (optional, for authenticated relay)
    /// - SmtpPassword: SMTP password (optional, for authenticated relay)
    /// - SmtpDefaultFromAddress: Default from address if not specified in queue record
    /// 
    /// Prerequisites:
    /// 1. Enable Change Tracking on the database:
    ///    ALTER DATABASE [YourDatabase] SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON);
    /// 
    /// 2. Enable Change Tracking on the table:
    ///    ALTER TABLE [dbo].[EmailQueue] ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
    /// </summary>
    [Function("EmailQueueTrigger")]
    public async Task Run(
        [SqlTrigger(
            tableName: "[dbo].[EmailQueue]",
            connectionStringSetting: "SqlConnectionString")]
        IReadOnlyList<SqlChange<EmailQueueRecord>> changes)
    {
        var tableName = _configuration["MonitoredTableName"];
        _logger.LogInformation("Processing {Count} change(s) from table: {TableName}", changes.Count, tableName);

        foreach (var change in changes)
        {
            // Only process INSERT operations for new queue items
            if (change.Operation == SqlChangeOperation.Insert)
            {               
                await ProcessEmailQueueItemAsync(change.Item);
            }
            else
            {
                _logger.LogDebug(
                    "Ignoring {Operation} operation for EmailQueueId={EmailQueueId}",
                    change.Operation,
                    change.Item.EmailQueueId);
            }
        }
    }

    /// <summary>
    /// Processes a single email queue item:
    /// 1. Marks as Processing
    /// 2. Sends via SMTP
    /// 3. Marks as Success or Failure
    /// </summary>
    private async Task ProcessEmailQueueItemAsync(EmailQueueRecord record)
    {
        _logger.LogInformation(
            "Processing EmailQueueId={EmailQueueId}, Recipients={Recipients}, Subject={Subject}",
            record.EmailQueueId,
            record.Recipients,
            record.Subject);

        try
        {
            if (record.Status != "Pending")
            {
                _logger.LogDebug(
                    "Skipping EmailQueueId={EmailQueueId} with Status={Status}",
                    record.EmailQueueId,
                    record.Status);
                return;
            }
            // Step 1: Mark as Processing
            await _emailQueueRepository.MarkAsProcessingAsync(record.EmailQueueId);

            // Step 2: Send email via SMTP
            await _emailService.SendEmailAsync(record);

            // Step 3: Mark as Success (removes from queue)
            await _emailQueueRepository.MarkAsSuccessAsync(record.EmailQueueId);

            _logger.LogInformation(
                "Successfully sent email EmailQueueId={EmailQueueId} to {Recipients}",
                record.EmailQueueId,
                record.Recipients);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process EmailQueueId={EmailQueueId}: {ErrorMessage}",
                record.EmailQueueId,
                ex.Message);

            try
            {
                // Mark as Failed - stored procedure handles retry logic
                await _emailQueueRepository.MarkAsFailureAsync(record.EmailQueueId, ex.Message);
            }
            catch (Exception failureEx)
            {
                _logger.LogError(
                    failureEx,
                    "Failed to mark EmailQueueId={EmailQueueId} as failed: {ErrorMessage}",
                    record.EmailQueueId,
                    failureEx.Message);
            }
        }
    }
}
