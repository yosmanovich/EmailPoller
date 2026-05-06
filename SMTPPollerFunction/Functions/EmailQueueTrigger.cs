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
    private readonly ISmtpThrottleService _throttleService;

    public EmailQueueTrigger(
        ILogger<EmailQueueTrigger> logger,
        IConfiguration configuration,
        IEmailService emailService,
        IEmailQueueRepository emailQueueRepository,
        ISmtpThrottleService throttleService)
    {
        _logger = logger;
        _configuration = configuration;
        _emailService = emailService;
        _emailQueueRepository = emailQueueRepository;
        _throttleService = throttleService;
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

        // Check if we're currently throttled
        if (_throttleService.IsThrottled())
        {
            var remaining = _throttleService.GetThrottleTimeRemaining();
            _logger.LogWarning(
                "SMTP throttling active. Skipping {Count} messages. Throttle expires in {Seconds:F0} seconds",
                changes.Count,
                remaining.TotalSeconds);
            return; // Let the timer trigger pick these up later
        }

        var interMessageDelay = _throttleService.GetInterMessageDelay();
        var isFirstMessage = true;

        foreach (var change in changes)
        {
            // Only process INSERT operations for new queue items
            if (change.Operation == SqlChangeOperation.Insert)
            {
                // Add delay between messages if we've had recent throttling
                if (!isFirstMessage && interMessageDelay > TimeSpan.Zero)
                {
                    _logger.LogDebug("Throttle recovery: waiting {Delay}ms between messages", interMessageDelay.TotalMilliseconds);
                    await Task.Delay(interMessageDelay);
                }

                await ProcessEmailQueueItemAsync(change.Item);
                isFirstMessage = false;

                // Re-check throttle status after each send
                if (_throttleService.IsThrottled())
                {
                    _logger.LogWarning("Throttling activated during batch. Stopping further processing.");
                    break;
                }
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

            // Record successful send for throttle recovery
            _throttleService.RecordSuccess();

            _logger.LogInformation(
                "Successfully sent email EmailQueueId={EmailQueueId} to {Recipients}",
                record.EmailQueueId,
                record.Recipients);
        }
        catch (SmtpThrottlingException ex)
        {
            // Record throttling and activate backoff
            _throttleService.RecordThrottling();

            _logger.LogWarning(
                ex,
                "SMTP throttling for EmailQueueId={EmailQueueId}. Will retry later.",
                record.EmailQueueId);

            try
            {
                // Mark as failed with throttling message - will be retried
                await _emailQueueRepository.MarkAsFailureAsync(
                    record.EmailQueueId,
                    $"SMTP throttling: {ex.Message}");
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
