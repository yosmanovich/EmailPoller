using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SMTPPoller.Models;

namespace SMTPPoller.Services;

/// <summary>
/// Service for sending emails via SMTP relay.
/// </summary>
public class EmailService : IEmailService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<SmtpSettings> settings, ILogger<EmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends an email based on the EmailQueueRecord.
    /// </summary>
    public async Task SendEmailAsync(EmailQueueRecord emailRecord)
    {
        using var smtpClient = CreateSmtpClient();
        using var mailMessage = CreateMailMessage(emailRecord);

        _logger.LogInformation(
            "Sending email to {Recipients} with subject '{Subject}'",
            emailRecord.Recipients,
            emailRecord.Subject);

        await smtpClient.SendMailAsync(mailMessage);

        _logger.LogInformation(
            "Email sent successfully to {Recipients}",
            emailRecord.Recipients);
    }

    private SmtpClient CreateSmtpClient()
    {
        var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl,
            Timeout = _settings.TimeoutMs,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        // Set credentials if username is provided
        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
        }
        else
        {
            // Use default credentials for relay scenarios
            client.UseDefaultCredentials = true;
        }

        return client;
    }

    private MailMessage CreateMailMessage(EmailQueueRecord record)
    {
        var fromAddress = record.FromAddress ?? _settings.DefaultFromAddress
            ?? throw new InvalidOperationException("No from address specified in record or default settings");

        var message = new MailMessage
        {
            From = new MailAddress(fromAddress),
            Subject = record.Subject ?? string.Empty,
            Body = record.Body ?? string.Empty,
            IsBodyHtml = string.Equals(record.BodyFormat, "HTML", StringComparison.OrdinalIgnoreCase)
        };

        // Add recipients (required)
        AddRecipients(message.To, record.Recipients);

        // Add CC recipients (optional)
        if (!string.IsNullOrWhiteSpace(record.CopyRecipients))
        {
            AddRecipients(message.CC, record.CopyRecipients);
        }

        // Add BCC recipients (optional)
        if (!string.IsNullOrWhiteSpace(record.BlindCopyRecipients))
        {
            AddRecipients(message.Bcc, record.BlindCopyRecipients);
        }

        // Set Reply-To (optional)
        if (!string.IsNullOrWhiteSpace(record.ReplyTo))
        {
            message.ReplyToList.Add(new MailAddress(record.ReplyTo));
        }

        // Set priority based on Importance
        message.Priority = record.Importance?.ToUpperInvariant() switch
        {
            "HIGH" => MailPriority.High,
            "LOW" => MailPriority.Low,
            _ => MailPriority.Normal
        };

        // Add file attachments if specified
        if (!string.IsNullOrWhiteSpace(record.FileAttachments))
        {
            AddAttachments(message, record.FileAttachments);
        }

        return message;
    }

    private static void AddRecipients(MailAddressCollection collection, string recipients)
    {
        // Recipients can be semicolon or comma separated
        var addresses = recipients.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var address in addresses)
        {
            var trimmed = address.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                collection.Add(new MailAddress(trimmed));
            }
        }
    }

    private void AddAttachments(MailMessage message, string fileAttachments)
    {
        // File attachments are semicolon separated paths
        var files = fileAttachments.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var filePath in files)
        {
            var trimmedPath = filePath.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedPath))
            {
                if (File.Exists(trimmedPath))
                {
                    message.Attachments.Add(new Attachment(trimmedPath));
                    _logger.LogDebug("Added attachment: {FilePath}", trimmedPath);
                }
                else
                {
                    _logger.LogWarning("Attachment file not found: {FilePath}", trimmedPath);
                }
            }
        }
    }
}
