namespace SMTPPoller.Services;

/// <summary>
/// SMTP configuration settings loaded from app settings.
/// </summary>
public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; } = false;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? DefaultFromAddress { get; set; }
    public int TimeoutMs { get; set; } = 30000;
}
