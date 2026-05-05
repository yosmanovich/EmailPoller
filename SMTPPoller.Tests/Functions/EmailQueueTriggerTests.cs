using Microsoft.Azure.Functions.Worker.Extensions.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SMTPPoller.Functions;
using SMTPPoller.Models;
using SMTPPoller.Services;
using SMTPPoller.Tests.Helpers;
using Xunit;

namespace SMTPPoller.Tests.Functions;

public class EmailQueueTriggerTests
{
    private readonly Mock<ILogger<EmailQueueTrigger>> _loggerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IEmailQueueRepository> _repositoryMock;
    private readonly EmailQueueTrigger _trigger;

    public EmailQueueTriggerTests()
    {
        _loggerMock = new Mock<ILogger<EmailQueueTrigger>>();
        _configurationMock = new Mock<IConfiguration>();
        _emailServiceMock = new Mock<IEmailService>();
        _repositoryMock = new Mock<IEmailQueueRepository>();

        _configurationMock.Setup(c => c["MonitoredTableName"]).Returns("dbo.EmailQueue");

        _trigger = new EmailQueueTrigger(
            _loggerMock.Object,
            _configurationMock.Object,
            _emailServiceMock.Object,
            _repositoryMock.Object);
    }

    private static SqlChange<EmailQueueRecord> CreateSqlChange(
        EmailQueueRecord record,
        SqlChangeOperation operation)
    {
        return new SqlChange<EmailQueueRecord>(operation, record);
    }

    [Fact]
    public async Task Run_WithInsertOperation_ProcessesEmail()
    {
        // Arrange
        var record = EmailQueueRecordFactory.CreateValid();
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(record, SqlChangeOperation.Insert)
        };

        _repositoryMock.Setup(r => r.MarkAsProcessingAsync(record.EmailQueueId))
            .Returns(Task.CompletedTask);
        _emailServiceMock.Setup(e => e.SendEmailAsync(record))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(record.EmailQueueId))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(changes);

        // Assert
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(record.EmailQueueId), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(record), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(record.EmailQueueId), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsFailureAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Run_WithUpdateOperation_DoesNotProcessEmail()
    {
        // Arrange
        var record = EmailQueueRecordFactory.CreateValid();
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(record, SqlChangeOperation.Update)
        };

        // Act
        await _trigger.Run(changes);

        // Assert
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(It.IsAny<int>()), Times.Never);
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()), Times.Never);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Run_WithDeleteOperation_DoesNotProcessEmail()
    {
        // Arrange
        var record = EmailQueueRecordFactory.CreateValid();
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(record, SqlChangeOperation.Delete)
        };

        // Act
        await _trigger.Run(changes);

        // Assert
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(It.IsAny<int>()), Times.Never);
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()), Times.Never);
    }

    [Fact]
    public async Task Run_WithMultipleInserts_ProcessesAllEmails()
    {
        // Arrange
        var record1 = EmailQueueRecordFactory.CreateValid(emailQueueId: 1);
        var record2 = EmailQueueRecordFactory.CreateValid(emailQueueId: 2);
        var record3 = EmailQueueRecordFactory.CreateValid(emailQueueId: 3);
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(record1, SqlChangeOperation.Insert),
            CreateSqlChange(record2, SqlChangeOperation.Insert),
            CreateSqlChange(record3, SqlChangeOperation.Insert)
        };

        _repositoryMock.Setup(r => r.MarkAsProcessingAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(changes);

        // Assert
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(It.IsAny<int>()), Times.Exactly(3));
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()), Times.Exactly(3));
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(It.IsAny<int>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Run_WithMixedOperations_OnlyProcessesInserts()
    {
        // Arrange
        var insertRecord = EmailQueueRecordFactory.CreateValid(emailQueueId: 1);
        var updateRecord = EmailQueueRecordFactory.CreateValid(emailQueueId: 2);
        var deleteRecord = EmailQueueRecordFactory.CreateValid(emailQueueId: 3);
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(insertRecord, SqlChangeOperation.Insert),
            CreateSqlChange(updateRecord, SqlChangeOperation.Update),
            CreateSqlChange(deleteRecord, SqlChangeOperation.Delete)
        };

        _repositoryMock.Setup(r => r.MarkAsProcessingAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(changes);

        // Assert - only the insert should be processed
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(1), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(insertRecord), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(1), Times.Once);
        
        // Update and delete should not trigger processing
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(2), Times.Never);
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(3), Times.Never);
    }

    [Fact]
    public async Task Run_WhenEmailServiceThrows_CallsMarkAsFailure()
    {
        // Arrange
        var record = EmailQueueRecordFactory.CreateValid();
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(record, SqlChangeOperation.Insert)
        };

        _repositoryMock.Setup(r => r.MarkAsProcessingAsync(record.EmailQueueId))
            .Returns(Task.CompletedTask);
        _emailServiceMock.Setup(e => e.SendEmailAsync(record))
            .ThrowsAsync(new InvalidOperationException("SMTP connection failed"));
        _repositoryMock.Setup(r => r.MarkAsFailureAsync(record.EmailQueueId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(changes);

        // Assert
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(record.EmailQueueId), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(record), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(record.EmailQueueId), Times.Never);
        _repositoryMock.Verify(
            r => r.MarkAsFailureAsync(record.EmailQueueId, "SMTP connection failed"), 
            Times.Once);
    }

    [Fact]
    public async Task Run_WhenMarkAsProcessingThrows_CallsMarkAsFailure()
    {
        // Arrange
        var record = EmailQueueRecordFactory.CreateValid();
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(record, SqlChangeOperation.Insert)
        };

        _repositoryMock.Setup(r => r.MarkAsProcessingAsync(record.EmailQueueId))
            .ThrowsAsync(new Exception("Database error"));
        _repositoryMock.Setup(r => r.MarkAsFailureAsync(record.EmailQueueId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(changes);

        // Assert
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(record.EmailQueueId), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()), Times.Never);
        _repositoryMock.Verify(
            r => r.MarkAsFailureAsync(record.EmailQueueId, "Database error"), 
            Times.Once);
    }

    [Fact]
    public async Task Run_WhenMarkAsFailureAlsoThrows_DoesNotCrash()
    {
        // Arrange
        var record = EmailQueueRecordFactory.CreateValid();
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(record, SqlChangeOperation.Insert)
        };

        _repositoryMock.Setup(r => r.MarkAsProcessingAsync(record.EmailQueueId))
            .Returns(Task.CompletedTask);
        _emailServiceMock.Setup(e => e.SendEmailAsync(record))
            .ThrowsAsync(new Exception("SMTP error"));
        _repositoryMock.Setup(r => r.MarkAsFailureAsync(record.EmailQueueId, It.IsAny<string>()))
            .ThrowsAsync(new Exception("Database unavailable"));

        // Act - should not throw
        await _trigger.Run(changes);

        // Assert
        _repositoryMock.Verify(r => r.MarkAsFailureAsync(record.EmailQueueId, "SMTP error"), Times.Once);
    }

    [Fact]
    public async Task Run_WithEmptyChangesList_CompletesWithoutError()
    {
        // Arrange
        var changes = new List<SqlChange<EmailQueueRecord>>();

        // Act
        await _trigger.Run(changes);

        // Assert
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(It.IsAny<int>()), Times.Never);
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()), Times.Never);
    }

    [Fact]
    public async Task Run_WithOneFailure_ContinuesProcessingOtherEmails()
    {
        // Arrange
        var record1 = EmailQueueRecordFactory.CreateValid(emailQueueId: 1);
        var record2 = EmailQueueRecordFactory.CreateValid(emailQueueId: 2);
        var record3 = EmailQueueRecordFactory.CreateValid(emailQueueId: 3);
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(record1, SqlChangeOperation.Insert),
            CreateSqlChange(record2, SqlChangeOperation.Insert),
            CreateSqlChange(record3, SqlChangeOperation.Insert)
        };

        _repositoryMock.Setup(r => r.MarkAsProcessingAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        
        // Record 2 fails
        _emailServiceMock.Setup(e => e.SendEmailAsync(record1))
            .Returns(Task.CompletedTask);
        _emailServiceMock.Setup(e => e.SendEmailAsync(record2))
            .ThrowsAsync(new Exception("Failed for record 2"));
        _emailServiceMock.Setup(e => e.SendEmailAsync(record3))
            .Returns(Task.CompletedTask);
        
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsFailureAsync(It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(changes);

        // Assert
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(1), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsFailureAsync(2, "Failed for record 2"), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(3), Times.Once);
    }

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Arrange & Act
        var trigger = new EmailQueueTrigger(
            _loggerMock.Object,
            _configurationMock.Object,
            _emailServiceMock.Object,
            _repositoryMock.Object);

        // Assert
        Assert.NotNull(trigger);
    }

    [Fact]
    public async Task Run_WithPendingStatus_ProcessesEmail()
    {
        // Arrange
        var record = EmailQueueRecordFactory.CreateValid(status: "Pending");
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(record, SqlChangeOperation.Insert)
        };

        _repositoryMock.Setup(r => r.MarkAsProcessingAsync(record.EmailQueueId))
            .Returns(Task.CompletedTask);
        _emailServiceMock.Setup(e => e.SendEmailAsync(record))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(record.EmailQueueId))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(changes);

        // Assert
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(record.EmailQueueId), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(record), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(record.EmailQueueId), Times.Once);
    }

    [Theory]
    [InlineData("Processing")]
    [InlineData("Failed")]
    [InlineData("Success")]
    [InlineData("Completed")]
    public async Task Run_WithNonPendingStatus_SkipsProcessing(string status)
    {
        // Arrange
        var record = EmailQueueRecordFactory.CreateValid(status: status);
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(record, SqlChangeOperation.Insert)
        };

        // Act
        await _trigger.Run(changes);

        // Assert - should skip all processing steps
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(It.IsAny<int>()), Times.Never);
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()), Times.Never);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(It.IsAny<int>()), Times.Never);
        _repositoryMock.Verify(r => r.MarkAsFailureAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Run_WithMixedStatusRecords_OnlyProcessesPending()
    {
        // Arrange
        var pendingRecord = EmailQueueRecordFactory.CreateValid(emailQueueId: 1, status: "Pending");
        var processingRecord = EmailQueueRecordFactory.CreateValid(emailQueueId: 2, status: "Processing");
        var failedRecord = EmailQueueRecordFactory.CreateValid(emailQueueId: 3, status: "Failed");
        var changes = new List<SqlChange<EmailQueueRecord>>
        {
            CreateSqlChange(pendingRecord, SqlChangeOperation.Insert),
            CreateSqlChange(processingRecord, SqlChangeOperation.Insert),
            CreateSqlChange(failedRecord, SqlChangeOperation.Insert)
        };

        _repositoryMock.Setup(r => r.MarkAsProcessingAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<EmailQueueRecord>()))
            .Returns(Task.CompletedTask);
        _repositoryMock.Setup(r => r.MarkAsSuccessAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        // Act
        await _trigger.Run(changes);

        // Assert - only pending record should be processed
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(1), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(pendingRecord), Times.Once);
        _repositoryMock.Verify(r => r.MarkAsSuccessAsync(1), Times.Once);

        // Non-pending records should not be processed
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(2), Times.Never);
        _repositoryMock.Verify(r => r.MarkAsProcessingAsync(3), Times.Never);
        _emailServiceMock.Verify(e => e.SendEmailAsync(processingRecord), Times.Never);
        _emailServiceMock.Verify(e => e.SendEmailAsync(failedRecord), Times.Never);
    }
}
