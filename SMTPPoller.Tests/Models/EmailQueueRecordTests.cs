using SMTPPoller.Models;
using Xunit;

namespace SMTPPoller.Tests.Models;

public class EmailQueueRecordTests
{
    [Fact]
    public void EmailQueueRecord_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var record = new EmailQueueRecord();

        // Assert
        Assert.Equal("Pending", record.Status);
        Assert.Equal(0, record.RetryCount);
        Assert.Equal(3, record.MaxRetries);
        Assert.Equal("TEXT", record.BodyFormat);
        Assert.Equal("Normal", record.Importance);
        Assert.Equal("Normal", record.Sensitivity);
        Assert.Equal(string.Empty, record.Recipients);
    }

    [Fact]
    public void EmailQueueRecord_CanSetAllProperties()
    {
        // Arrange
        var createdDate = DateTime.UtcNow;
        var processedDate = DateTime.UtcNow.AddMinutes(5);

        // Act
        var record = new EmailQueueRecord
        {
            EmailQueueId = 123,
            Status = "Processing",
            CreatedDate = createdDate,
            ProcessedDate = processedDate,
            RetryCount = 2,
            MaxRetries = 5,
            ErrorMessage = "Test error",
            MailItemId = 456,
            ProfileName = "TestProfile",
            Recipients = "test@example.com",
            CopyRecipients = "cc@example.com",
            BlindCopyRecipients = "bcc@example.com",
            FromAddress = "from@example.com",
            ReplyTo = "reply@example.com",
            Subject = "Test Subject",
            Body = "Test Body",
            BodyFormat = "HTML",
            Importance = "High",
            Sensitivity = "Private",
            FileAttachments = "C:\\file1.txt;C:\\file2.pdf"
        };

        // Assert
        Assert.Equal(123, record.EmailQueueId);
        Assert.Equal("Processing", record.Status);
        Assert.Equal(createdDate, record.CreatedDate);
        Assert.Equal(processedDate, record.ProcessedDate);
        Assert.Equal(2, record.RetryCount);
        Assert.Equal(5, record.MaxRetries);
        Assert.Equal("Test error", record.ErrorMessage);
        Assert.Equal(456, record.MailItemId);
        Assert.Equal("TestProfile", record.ProfileName);
        Assert.Equal("test@example.com", record.Recipients);
        Assert.Equal("cc@example.com", record.CopyRecipients);
        Assert.Equal("bcc@example.com", record.BlindCopyRecipients);
        Assert.Equal("from@example.com", record.FromAddress);
        Assert.Equal("reply@example.com", record.ReplyTo);
        Assert.Equal("Test Subject", record.Subject);
        Assert.Equal("Test Body", record.Body);
        Assert.Equal("HTML", record.BodyFormat);
        Assert.Equal("High", record.Importance);
        Assert.Equal("Private", record.Sensitivity);
        Assert.Equal("C:\\file1.txt;C:\\file2.pdf", record.FileAttachments);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Processing")]
    [InlineData("Sent")]
    [InlineData("Failed")]
    public void EmailQueueRecord_Status_AcceptsValidValues(string status)
    {
        // Arrange & Act
        var record = new EmailQueueRecord { Status = status };

        // Assert
        Assert.Equal(status, record.Status);
    }

    [Theory]
    [InlineData("TEXT")]
    [InlineData("HTML")]
    public void EmailQueueRecord_BodyFormat_AcceptsValidValues(string bodyFormat)
    {
        // Arrange & Act
        var record = new EmailQueueRecord { BodyFormat = bodyFormat };

        // Assert
        Assert.Equal(bodyFormat, record.BodyFormat);
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Normal")]
    [InlineData("High")]
    public void EmailQueueRecord_Importance_AcceptsValidValues(string importance)
    {
        // Arrange & Act
        var record = new EmailQueueRecord { Importance = importance };

        // Assert
        Assert.Equal(importance, record.Importance);
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("Personal")]
    [InlineData("Private")]
    [InlineData("Confidential")]
    public void EmailQueueRecord_Sensitivity_AcceptsValidValues(string sensitivity)
    {
        // Arrange & Act
        var record = new EmailQueueRecord { Sensitivity = sensitivity };

        // Assert
        Assert.Equal(sensitivity, record.Sensitivity);
    }

    [Fact]
    public void EmailQueueRecord_NullableProperties_CanBeNull()
    {
        // Arrange & Act
        var record = new EmailQueueRecord
        {
            ProcessedDate = null,
            ErrorMessage = null,
            MailItemId = null,
            ProfileName = null,
            CopyRecipients = null,
            BlindCopyRecipients = null,
            FromAddress = null,
            ReplyTo = null,
            Subject = null,
            Body = null,
            BodyFormat = null,
            Importance = null,
            Sensitivity = null,
            FileAttachments = null
        };

        // Assert
        Assert.Null(record.ProcessedDate);
        Assert.Null(record.ErrorMessage);
        Assert.Null(record.MailItemId);
        Assert.Null(record.ProfileName);
        Assert.Null(record.CopyRecipients);
        Assert.Null(record.BlindCopyRecipients);
        Assert.Null(record.FromAddress);
        Assert.Null(record.ReplyTo);
        Assert.Null(record.Subject);
        Assert.Null(record.Body);
        Assert.Null(record.BodyFormat);
        Assert.Null(record.Importance);
        Assert.Null(record.Sensitivity);
        Assert.Null(record.FileAttachments);
    }
}
