using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SMTPPoller.Services;
using SMTPPoller.Tests.Helpers;
using Xunit;

namespace SMTPPoller.Tests.Services;

public class EmailServiceTests
{
    private readonly Mock<ILogger<EmailService>> _loggerMock;
    private readonly SmtpSettings _defaultSettings;

    public EmailServiceTests()
    {
        _loggerMock = new Mock<ILogger<EmailService>>();
        _defaultSettings = new SmtpSettings
        {
            Host = "smtp.example.com",
            Port = 25,
            EnableSsl = false,
            Username = null,
            Password = null,
            DefaultFromAddress = "default@example.com",
            TimeoutMs = 30000
        };
    }

    private EmailService CreateService(SmtpSettings? settings = null)
    {
        var options = Options.Create(settings ?? _defaultSettings);
        return new EmailService(options, _loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithValidSettings_CreatesInstance()
    {
        // Act
        var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public async Task SendEmailAsync_WithNullFromAddress_AndNoDefaultFromAddress_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new SmtpSettings
        {
            Host = "smtp.example.com",
            DefaultFromAddress = null
        };
        var service = CreateService(settings);
        var record = EmailQueueRecordFactory.CreateValid();
        record.FromAddress = null;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendEmailAsync(record));
    }

    [Fact]
    public async Task SendEmailAsync_WithEmptyDefaultFromAddress_AndNullRecordFromAddress_ThrowsException()
    {
        // Arrange
        var settings = new SmtpSettings
        {
            Host = "smtp.example.com",
            DefaultFromAddress = ""
        };
        var service = CreateService(settings);
        var record = EmailQueueRecordFactory.CreateValid();
        record.FromAddress = null;

        // Act & Assert
        // Either InvalidOperationException (our validation) or ArgumentException (MailAddress constructor)
        await Assert.ThrowsAnyAsync<Exception>(() => service.SendEmailAsync(record));
    }

    [Fact]
    public void SmtpSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new SmtpSettings();

        // Assert
        Assert.Equal(string.Empty, settings.Host);
        Assert.Equal(25, settings.Port);
        Assert.False(settings.EnableSsl);
        Assert.Null(settings.Username);
        Assert.Null(settings.Password);
        Assert.Null(settings.DefaultFromAddress);
        Assert.Equal(30000, settings.TimeoutMs);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(465)]
    [InlineData(587)]
    [InlineData(2525)]
    public void SmtpSettings_Port_AcceptsCommonValues(int port)
    {
        // Arrange & Act
        var settings = new SmtpSettings { Port = port };

        // Assert
        Assert.Equal(port, settings.Port);
    }

    [Fact]
    public void SmtpSettings_EnableSsl_CanBeEnabled()
    {
        // Arrange & Act
        var settings = new SmtpSettings { EnableSsl = true };

        // Assert
        Assert.True(settings.EnableSsl);
    }

    [Fact]
    public void SmtpSettings_Credentials_CanBeSet()
    {
        // Arrange & Act
        var settings = new SmtpSettings
        {
            Username = "testuser",
            Password = "testpass"
        };

        // Assert
        Assert.Equal("testuser", settings.Username);
        Assert.Equal("testpass", settings.Password);
    }
}
