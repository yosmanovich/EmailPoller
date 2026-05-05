namespace SMTPPoller.Models;

/// <summary>
/// Model class representing a record from the EmailQueue table.
/// </summary>
public class EmailQueueRecord
{
    // Primary Key
    public int EmailQueueId { get; set; }

    // Queue Management
    public string Status { get; set; } = "Pending";
    public DateTime CreatedDate { get; set; }
    public DateTime? ProcessedDate { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public string? ErrorMessage { get; set; }
    public int? MailItemId { get; set; }

    // sp_send_dbmail Parameters
    public string? ProfileName { get; set; }
    public string Recipients { get; set; } = string.Empty;
    public string? CopyRecipients { get; set; }
    public string? BlindCopyRecipients { get; set; }
    public string? FromAddress { get; set; }
    public string? ReplyTo { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public string? BodyFormat { get; set; } = "TEXT";
    public string? Importance { get; set; } = "Normal";
    public string? Sensitivity { get; set; } = "Normal";
    public string? FileAttachments { get; set; }
}
