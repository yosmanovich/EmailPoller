using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SMTPPoller.Functions;
using SMTPPoller.Models;
using SMTPPoller.Services;
using SMTPPoller.Tests.Helpers;
using Xunit;

namespace SMTPPoller.Tests.Functions;

public class EmailQueueTimerTriggerTests
{
    private readonly Mock<ILogger<EmailQueueTimerTrigger>> _loggerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IEmailQueueRepository> _repositoryMock;
    private readonly EmailQueueTimerTrigger _trigger;

    public EmailQueueTimerTriggerTests()
    {
        _loggerMock = new Mock<ILogger<EmailQueueTimerTrigger>>();
        _configurationMock = new Mock<IConfiguration>();
        _emailServiceMock = new Mock<IEmailService>();
        _repositoryMock = new Mock<IEmailQueueRepository>();

        _configurationMock.Setup(c => c["EmailQueueTimerMaxRecords"]).Returns("100");

        _trigger = new EmailQueueTimerTrigger(
            _loggerMock.Object,
            _configurationMock.Object,
            _emailServiceMock.Object,
            _repositoryMock.Object);
    }

    private static TimerInfo CreateTimerInfo(bool isPastDue = false)
    {
        return new TestTimerInfo(isPastDue);
    }

    [Fact]
    public async Task Run_WithNoPendingEmails_DoesNotProcessAnything()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();
        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<EmailQueueRecord>());

        // Act
        await _trigger.Run(timerInfo);

        // Assert
        _repositoryMock.Verify(r => r.GetPendingEmailsAsync(100), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()), Times.Never);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Run_WithPendingEmails_ProcessesAllEmails()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();
        var emails = new List<EmailQueueRecord>
        {
            EmailQueueRecordFactory.CreateValid(emailQueueId: 1),
            EmailQueueRecordFactory.CreateValid(emailQueueId: 2),
            EmailQueueRecordFactory.CreateValid(emailQueueId: 3)
        };

        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(It.IsAny<int>()))
            .ReturnsAsync(emails);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(timerInfo);

        // Assert - ClaimBatch already marked as Processing, so we just verify send and success
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()), Times.Exactly(3));
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(It.IsAny<int>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Run_WithSinglePendingEmail_ProcessesEmail()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();
        var record = EmailQueueRecordFactory.CreateValid();
        var emails = new List<EmailQueueRecord> { record };

        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(It.IsAny<int>()))
            .ReturnsAsync(emails);
        _emailServiceMock.Setup(e => e.SendEmailAsync(record))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(record.EmailQueueId))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(timerInfo);

        // Assert - ClaimBatch already marked as Processing
        _emailServiceMock.Verify(e => e.SendEmailAsync(record), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(record.EmailQueueId), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsFailureAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Run_WhenEmailSendFails_MarksAsFailure()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();
        var record = EmailQueueRecordFactory.CreateValid();
        var emails = new List<EmailQueueRecord> { record };

        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(It.IsAny<int>()))
            .ReturnsAsync(emails);
        _emailServiceMock.Setup(e => e.SendEmailAsync(record))
            .ThrowsAsync(new InvalidOperationException("SMTP server unavailable"));
        _repositoryMock.Setup(r => r.MarkAsFailureAsync(record.EmailQueueId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(timerInfo);

        // Assert - ClaimBatch already marked as Processing
        _emailServiceMock.Verify(e => e.SendEmailAsync(record), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(It.IsAny<int>()), Times.Never);
        _repositoryMock.Verify(r => r.MarkAsFailureAsync(record.EmailQueueId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Run_WithMixedSuccessAndFailure_ContinuesProcessing()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();
        var successRecord1 = EmailQueueRecordFactory.CreateValid(emailQueueId: 1);
        var failRecord = EmailQueueRecordFactory.CreateValid(emailQueueId: 2);
        var successRecord2 = EmailQueueRecordFactory.CreateValid(emailQueueId: 3);
        var emails = new List<EmailQueueRecord> { successRecord1, failRecord, successRecord2 };

        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(It.IsAny<int>()))
            .ReturnsAsync(emails);
        
        // First and third emails succeed, second fails
        _emailServiceMock.Setup(e => e.SendEmailAsync(successRecord1))
            .Returns(Task.CompletedTask);
        _emailServiceMock.Setup(e => e.SendEmailAsync(failRecord))
            .ThrowsAsync(new InvalidOperationException("SMTP error"));
        _emailServiceMock.Setup(e => e.SendEmailAsync(successRecord2))
            .Returns(Task.CompletedTask);
        
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsFailureAsync(It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(timerInfo);

        // Assert - all three should be attempted (ClaimBatch already marked as Processing)
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()), Times.Exactly(3));
        
        // Two successes, one failure
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(successRecord1.EmailQueueId), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(successRecord2.EmailQueueId), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsFailureAsync(failRecord.EmailQueueId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Run_UsesConfiguredMaxRecords()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();
        _configurationMock.Setup(c => c["EmailQueueTimerMaxRecords"]).Returns("50");
        
        var trigger = new EmailQueueTimerTrigger(
            _loggerMock.Object,
            _configurationMock.Object,
            _emailServiceMock.Object,
            _repositoryMock.Object);

        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(50))
            .ReturnsAsync(new List<EmailQueueRecord>());

        // Act
        await trigger.Run(timerInfo);

        // Assert
        _repositoryMock.Verify(r => r.GetPendingEmailsAsync(50), Times.Once);
    }

    [Fact]
    public async Task Run_WithInvalidMaxRecordsConfig_UsesDefault100()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();
        _configurationMock.Setup(c => c["EmailQueueTimerMaxRecords"]).Returns("invalid");
        
        var trigger = new EmailQueueTimerTrigger(
            _loggerMock.Object,
            _configurationMock.Object,
            _emailServiceMock.Object,
            _repositoryMock.Object);

        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(100))
            .ReturnsAsync(new List<EmailQueueRecord>());

        // Act
        await trigger.Run(timerInfo);

        // Assert
        _repositoryMock.Verify(r => r.GetPendingEmailsAsync(100), Times.Once);
    }

    [Fact]
    public async Task Run_WithNullMaxRecordsConfig_UsesDefault100()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();
        _configurationMock.Setup(c => c["EmailQueueTimerMaxRecords"]).Returns((string?)null);
        
        var trigger = new EmailQueueTimerTrigger(
            _loggerMock.Object,
            _configurationMock.Object,
            _emailServiceMock.Object,
            _repositoryMock.Object);

        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(100))
            .ReturnsAsync(new List<EmailQueueRecord>());

        // Act
        await trigger.Run(timerInfo);

        // Assert
        _repositoryMock.Verify(r => r.GetPendingEmailsAsync(100), Times.Once);
    }

    [Fact]
    public async Task Run_WhenPastDue_StillProcessesEmails()
    {
        // Arrange
        var timerInfo = CreateTimerInfo(isPastDue: true);
        var record = EmailQueueRecordFactory.CreateValid();
        var emails = new List<EmailQueueRecord> { record };

        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(It.IsAny<int>()))
            .ReturnsAsync(emails);
        _emailServiceMock.Setup(e => e.SendEmailAsync(record))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(record.EmailQueueId))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(timerInfo);

        // Assert - ClaimBatch already marked as Processing
        _emailServiceMock.Verify(e => e.SendEmailAsync(record), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(record.EmailQueueId), Times.Once);
    }

    [Fact]
    public async Task Run_WhenGetPendingEmailsFails_ThrowsException()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();
        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _trigger.Run(timerInfo));
    }

    [Fact]
    public async Task Run_WhenClaimBatchFails_ThrowsException()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();

        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("Database error during claim"));

        // Act & Assert - ClaimBatch (GetPendingEmailsAsync) failure should throw
        await Assert.ThrowsAsync<InvalidOperationException>(() => _trigger.Run(timerInfo));
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()), Times.Never);
    }

    [Fact]
    public async Task Run_WhenMarkAsFailureFails_ContinuesProcessing()
    {
        // Arrange
        var timerInfo = CreateTimerInfo();
        var failRecord = EmailQueueRecordFactory.CreateValid(emailQueueId: 1);
        var successRecord = EmailQueueRecordFactory.CreateValid(emailQueueId: 2);
        var emails = new List<EmailQueueRecord> { failRecord, successRecord };

        _repositoryMock.Setup(r => r.GetPendingEmailsAsync(It.IsAny<int>()))
            .ReturnsAsync(emails);
        
        // First email fails to send
        _emailServiceMock.Setup(e => e.SendEmailAsync(failRecord))
            .ThrowsAsync(new InvalidOperationException("SMTP error"));
        // And marking as failure also fails
        _repositoryMock.Setup(r => r.MarkAsFailureAsync(failRecord.EmailQueueId, It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));
        
        // Second email succeeds
        _emailServiceMock.Setup(e => e.SendEmailAsync(successRecord))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(successRecord.EmailQueueId))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(timerInfo);

        // Assert - should continue to process second email (ClaimBatch already marked as Processing)
        _emailServiceMock.Verify(e => e.SendEmailAsync(successRecord), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(successRecord.EmailQueueId), Times.Once);
    }
}

/// <summary>
/// Test implementation of TimerInfo for unit testing.
/// </summary>
public class TestTimerInfo : TimerInfo
{
    public TestTimerInfo(bool isPastDue = false)
    {
        IsPastDue = isPastDue;
        ScheduleStatus = new TestScheduleStatus();
    }
}

/// <summary>
/// Test implementation of ScheduleStatus for unit testing.
/// </summary>
public class TestScheduleStatus : ScheduleStatus
{
    public TestScheduleStatus()
    {
        Last = DateTime.UtcNow.AddMinutes(-5);
        Next = DateTime.UtcNow.AddMinutes(5);
        LastUpdated = DateTime.UtcNow;
    }
}
