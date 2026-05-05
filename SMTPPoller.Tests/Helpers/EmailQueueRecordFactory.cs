using SMTPPoller.Models;

namespace SMTPPoller.Tests.Helpers;

/// <summary>
/// Factory for creating test EmailQueueRecord instances.
/// </summary>
public static class EmailQueueRecordFactory
{
    /// <summary>
    /// Creates a valid EmailQueueRecord with all required fields populated.
    /// </summary>
    public static EmailQueueRecord CreateValid(
        int emailQueueId = 1,
        string recipients = "test@example.com",
        string? subject = "Test Subject",
        string? body = "Test Body",
        string? fromAddress = "sender@example.com",
        string status = "Pending")
    {
        return new EmailQueueRecord
        {
            EmailQueueId = emailQueueId,
            Status = status,
            CreatedDate = DateTime.UtcNow,
            ProcessedDate = null,
            RetryCount = 0,
            MaxRetries = 3,
            ErrorMessage = null,
            MailItemId = null,
            ProfileName = null,
            Recipients = recipients,
            CopyRecipients = null,
            BlindCopyRecipients = null,
            FromAddress = fromAddress,
            ReplyTo = null,
            Subject = subject,
            Body = body,
            BodyFormat = "TEXT",
            Importance = "Normal",
            Sensitivity = "Normal",
            FileAttachments = null
        };
    }

    /// <summary>
    /// Creates an EmailQueueRecord with HTML body format.
    /// </summary>
    public static EmailQueueRecord CreateHtmlEmail(
        int emailQueueId = 1,
        string recipients = "test@example.com")
    {
        return new EmailQueueRecord
        {
            EmailQueueId = emailQueueId,
            Status = "Pending",
            CreatedDate = DateTime.UtcNow,
            Recipients = recipients,
            FromAddress = "sender@example.com",
            Subject = "HTML Test",
            Body = "<html><body><h1>Hello</h1></body></html>",
            BodyFormat = "HTML",
            Importance = "Normal"
        };
    }

    /// <summary>
    /// Creates an EmailQueueRecord with multiple recipients.
    /// </summary>
    public static EmailQueueRecord CreateWithMultipleRecipients(int emailQueueId = 1)
    {
        return new EmailQueueRecord
        {
            EmailQueueId = emailQueueId,
            Status = "Pending",
            CreatedDate = DateTime.UtcNow,
            Recipients = "user1@example.com;user2@example.com,user3@example.com",
            CopyRecipients = "cc1@example.com;cc2@example.com",
            BlindCopyRecipients = "bcc@example.com",
            FromAddress = "sender@example.com",
            Subject = "Multi-recipient Test",
            Body = "Test body"
        };
    }

    /// <summary>
    /// Creates an EmailQueueRecord with high importance.
    /// </summary>
    public static EmailQueueRecord CreateHighPriority(int emailQueueId = 1)
    {
        var record = CreateValid(emailQueueId);
        record.Importance = "High";
        record.Subject = "URGENT: High Priority Email";
        return record;
    }

    /// <summary>
    /// Creates an EmailQueueRecord with low importance.
    /// </summary>
    public static EmailQueueRecord CreateLowPriority(int emailQueueId = 1)
    {
        var record = CreateValid(emailQueueId);
        record.Importance = "Low";
        return record;
    }

    /// <summary>
    /// Creates an EmailQueueRecord that has failed and reached max retries.
    /// </summary>
    public static EmailQueueRecord CreateFailed(int emailQueueId = 1)
    {
        return new EmailQueueRecord
        {
            EmailQueueId = emailQueueId,
            Status = "Failed",
            CreatedDate = DateTime.UtcNow.AddHours(-1),
            ProcessedDate = DateTime.UtcNow,
            RetryCount = 3,
            MaxRetries = 3,
            ErrorMessage = "SMTP connection failed",
            Recipients = "test@example.com",
            FromAddress = "sender@example.com",
            Subject = "Failed Email",
            Body = "This email failed to send"
        };
    }

    /// <summary>
    /// Creates an EmailQueueRecord with reply-to address.
    /// </summary>
    public static EmailQueueRecord CreateWithReplyTo(int emailQueueId = 1)
    {
        var record = CreateValid(emailQueueId);
        record.ReplyTo = "replyto@example.com";
        return record;
    }
}
