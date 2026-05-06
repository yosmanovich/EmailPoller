using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SMTPPoller.Models;
using SMTPPoller.Services;

namespace SMTPPoller.Functions;

/// <summary>
/// Timer-triggered Azure Function that polls the EmailQueue table for pending emails.
/// Acts as a backup/retry mechanism for emails that weren't processed by the SQL trigger.
/// 
/// Configuration (from App Settings):
/// - EmailQueueTimerSchedule: CRON expression for the timer (default: "0 */5 * * * *" - every 5 minutes)
/// - EmailQueueTimerMaxRecords: Maximum records to process per run (default: 100)
/// </summary>
public class EmailQueueTimerTrigger
{
    private readonly ILogger<EmailQueueTimerTrigger> _logger;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly IEmailQueueRepository _emailQueueRepository;
    private readonly ISmtpThrottleService _throttleService;

    public EmailQueueTimerTrigger(
        ILogger<EmailQueueTimerTrigger> logger,
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
    /// Timer-triggered function that runs on a configurable schedule.
    /// Queries for pending emails and processes them via SMTP.
    /// 
    /// Default schedule: Every 5 minutes ("0 */5 * * * *")
    /// Configure via EmailQueueTimerSchedule app setting.
    /// 
    /// CRON format: {second} {minute} {hour} {day} {month} {day-of-week}
    /// Examples:
    /// - "0 */5 * * * *"  = Every 5 minutes
    /// - "0 */1 * * * *"  = Every minute
    /// - "0 0 * * * *"    = Every hour
    /// - "0 0 */6 * * *"  = Every 6 hours
    /// </summary>
    [Function("EmailQueueTimerTrigger")]
    public async Task Run(
        [TimerTrigger("%EmailQueueTimerSchedule%")] TimerInfo timerInfo)
    {
        _logger.LogInformation("EmailQueueTimerTrigger started at: {Time}", DateTime.UtcNow);

        if (timerInfo.IsPastDue)
        {
            _logger.LogWarning("Timer is running late! This execution was past due.");
        }

        // Check if we're currently throttled
        if (_throttleService.IsThrottled())
        {
            var remaining = _throttleService.GetThrottleTimeRemaining();
            _logger.LogWarning(
                "SMTP throttling active. Skipping this timer run. Throttle expires in {Seconds:F0} seconds",
                remaining.TotalSeconds);
            return;
        }

        try
        {
            // Get configurable max records, default to 100
            var maxRecordsConfig = _configuration["EmailQueueTimerMaxRecords"];
            var maxRecords = int.TryParse(maxRecordsConfig, out var parsed) ? parsed : 100;

            // Fetch pending emails from the queue
            var pendingEmails = await _emailQueueRepository.GetPendingEmailsAsync(maxRecords);

            if (pendingEmails.Count == 0)
            {
                _logger.LogDebug("No pending emails found in the queue");
                return;
            }

            _logger.LogInformation("Found {Count} pending emails to process", pendingEmails.Count);

            var successCount = 0;
            var failureCount = 0;
            var throttledCount = 0;
            var interMessageDelay = _throttleService.GetInterMessageDelay();

            foreach (var email in pendingEmails)
            {
                // Check if throttling activated during batch
                if (_throttleService.IsThrottled())
                {
                    throttledCount = pendingEmails.Count - successCount - failureCount;
                    _logger.LogWarning(
                        "Throttling activated during batch. Skipping remaining {Count} emails.",
                        throttledCount);
                    break;
                }

                // Add delay between messages if we've had recent throttling
                if (successCount > 0 && interMessageDelay > TimeSpan.Zero)
                {
                    _logger.LogDebug("Throttle recovery: waiting {Delay}ms between messages", interMessageDelay.TotalMilliseconds);
                    await Task.Delay(interMessageDelay);
                }

                try
                {
                    await ProcessEmailQueueItemAsync(email);
                    successCount++;
                }
                catch (SmtpThrottlingException)
                {
                    // Throttling exception already handled and recorded in ProcessEmailQueueItemAsync
                    failureCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process EmailQueueId={EmailQueueId}", email.EmailQueueId);
                    failureCount++;
                }
            }

            _logger.LogInformation(
                "EmailQueueTimerTrigger completed. Processed: {Total}, Success: {Success}, Failed: {Failed}, Skipped (throttled): {Throttled}",
                pendingEmails.Count, successCount, failureCount, throttledCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EmailQueueTimerTrigger failed with error: {ErrorMessage}", ex.Message);
            throw;
        }

        _logger.LogDebug("Next timer schedule at: {NextRun}", timerInfo.ScheduleStatus?.Next);
    }

    /// <summary>
    /// Processes a single email queue item:
    /// 1. Sends via SMTP (already marked as Processing by ClaimBatch)
    /// 2. Marks as Success or Failure
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
            // Send email via SMTP (already marked as Processing by ClaimBatch)
            await _emailService.SendEmailAsync(record);

            // Mark as Success
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

            // Re-throw so caller knows this was a throttling exception
            throw;
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

            throw; // Re-throw to count as failure in the batch
        }
    }
}
