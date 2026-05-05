using SMTPPoller.Models;

namespace SMTPPoller.Services;

/// <summary>
/// Interface for email sending service.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email based on the EmailQueueRecord.
    /// </summary>
    /// <param name="emailRecord">The email queue record containing email details.</param>
    /// <returns>A task that completes when the email is sent.</returns>
    Task SendEmailAsync(EmailQueueRecord emailRecord);
}
