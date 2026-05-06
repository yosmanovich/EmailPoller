namespace SMTPPoller.Services;

/// <summary>
/// Exception thrown when SMTP server returns a throttling response.
/// This allows callers to distinguish throttling from other errors.
/// </summary>
public class SmtpThrottlingException : Exception
{
    /// <summary>
    /// The SMTP status code (e.g., 421, 451, 452).
    /// </summary>
    public string? StatusCode { get; }

    /// <summary>
    /// The enhanced status code if present (e.g., "4.5.127").
    /// </summary>
    public string? EnhancedStatusCode { get; }

    public SmtpThrottlingException(string message)
        : base(message)
    {
    }

    public SmtpThrottlingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SmtpThrottlingException(string message, string? statusCode, string? enhancedStatusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        EnhancedStatusCode = enhancedStatusCode;
    }

    /// <summary>
    /// Checks if an exception message indicates SMTP throttling.
    /// </summary>
    public static bool IsThrottlingError(Exception ex)
    {
        var message = ex.Message?.ToUpperInvariant() ?? string.Empty;
        var innerMessage = ex.InnerException?.Message?.ToUpperInvariant() ?? string.Empty;

        // Common throttling patterns across SMTP providers
        return message.Contains("4.5.127") ||           // Exchange/O365 excessive rate
               message.Contains("4.7.427") ||           // Exchange rate limit
               message.Contains("EXCESSIVE MESSAGE RATE") ||
               message.Contains("TOO MANY") ||
               message.Contains("RATE LIMIT") ||
               message.Contains("THROTTL") ||
               message.Contains("TRY AGAIN LATER") ||
               message.Contains("452 ") ||              // Insufficient storage (often rate limit)
               message.Contains("421 ") ||              // Service not available (often rate limit)
               innerMessage.Contains("4.5.127") ||
               innerMessage.Contains("EXCESSIVE MESSAGE RATE") ||
               innerMessage.Contains("RATE LIMIT");
    }
}
