using SMTPPoller.Models;

namespace SMTPPoller.Services;

/// <summary>
/// Interface for email queue database operations.
/// </summary>
public interface IEmailQueueRepository
{
    /// <summary>
    /// Gets all pending emails from the queue.
    /// </summary>
    Task<IReadOnlyList<EmailQueueRecord>> GetPendingEmailsAsync(int maxRecords = 100);

    /// <summary>
    /// Marks an email as processing by calling dbo.EmailQueue_Processing.
    /// </summary>
    Task MarkAsProcessingAsync(int emailQueueId);

    /// <summary>
    /// Marks an email as successfully sent by calling dbo.EmailQueue_Success.
    /// </summary>
    Task MarkAsSuccessAsync(int emailQueueId);

    /// <summary>
    /// Handles email failure by calling dbo.EmailQueue_Failure.
    /// </summary>
    Task MarkAsFailureAsync(int emailQueueId, string errorMessage);
}
