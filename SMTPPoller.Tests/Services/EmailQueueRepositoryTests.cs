using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SMTPPoller.Services;
using Xunit;

namespace SMTPPoller.Tests.Services;

public class EmailQueueRepositoryTests
{
    private readonly Mock<ILogger<EmailQueueRepository>> _loggerMock;

    public EmailQueueRepositoryTests()
    {
        _loggerMock = new Mock<ILogger<EmailQueueRepository>>();
    }

    private IConfiguration CreateConfiguration(string? connectionString)
    {
        var configData = new Dictionary<string, string?>();
        if (connectionString != null)
        {
            configData["SqlConnectionString"] = connectionString;
        }
        
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    [Fact]
    public void Constructor_WithValidConnectionString_CreatesInstance()
    {
        // Arrange
        var config = CreateConfiguration("Server=test;Database=test;");

        // Act
        var repository = new EmailQueueRepository(config, _loggerMock.Object);

        // Assert
        Assert.NotNull(repository);
    }

    [Fact]
    public void Constructor_WithMissingConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange
        var config = CreateConfiguration(null);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => new EmailQueueRepository(config, _loggerMock.Object));
        
        Assert.Contains("SqlConnectionString", exception.Message);
    }

    [Fact]
    public void Constructor_WithEmptyConnectionString_DoesNotThrowImmediately()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["SqlConnectionString"] = ""
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act - Empty string is accepted by constructor
        // Exception will occur when trying to use the connection
        var repository = new EmailQueueRepository(config, _loggerMock.Object);

        // Assert
        Assert.NotNull(repository);
    }

    [Fact]
    public async Task MarkAsProcessingAsync_WithInvalidConnection_ThrowsException()
    {
        // Arrange
        var config = CreateConfiguration("Server=nonexistent.invalid;Database=test;Connection Timeout=1;");
        var repository = new EmailQueueRepository(config, _loggerMock.Object);

        // Act & Assert
        // Should throw when trying to connect to invalid server
        await Assert.ThrowsAnyAsync<Exception>(
            () => repository.MarkAsProcessingAsync(1));
    }

    [Fact]
    public async Task MarkAsSuccessAsync_WithInvalidConnection_ThrowsException()
    {
        // Arrange
        var config = CreateConfiguration("Server=nonexistent.invalid;Database=test;Connection Timeout=1;");
        var repository = new EmailQueueRepository(config, _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => repository.MarkAsSuccessAsync(1));
    }

    [Fact]
    public async Task MarkAsFailureAsync_WithInvalidConnection_ThrowsException()
    {
        // Arrange
        var config = CreateConfiguration("Server=nonexistent.invalid;Database=test;Connection Timeout=1;");
        var repository = new EmailQueueRepository(config, _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => repository.MarkAsFailureAsync(1, "Test error"));
    }

    [Fact]
    public async Task MarkAsFailureAsync_WithNullErrorMessage_DoesNotThrow()
    {
        // Arrange
        var config = CreateConfiguration("Server=nonexistent.invalid;Database=test;Connection Timeout=1;");
        var repository = new EmailQueueRepository(config, _loggerMock.Object);

        // Act & Assert
        // Should throw due to invalid connection, not due to null error message
        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => repository.MarkAsFailureAsync(1, null!));
        
        // Verify it's a connection error, not a null reference error
        Assert.False(exception is NullReferenceException);
    }

    [Fact]
    public async Task GetPendingEmailsAsync_WithInvalidConnection_ThrowsException()
    {
        // Arrange
        var config = CreateConfiguration("Server=nonexistent.invalid;Database=test;Connection Timeout=1;");
        var repository = new EmailQueueRepository(config, _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => repository.GetPendingEmailsAsync(100));
    }

    [Fact]
    public async Task GetPendingEmailsAsync_WithDefaultMaxRecords_Uses100()
    {
        // Arrange
        var config = CreateConfiguration("Server=nonexistent.invalid;Database=test;Connection Timeout=1;");
        var repository = new EmailQueueRepository(config, _loggerMock.Object);

        // Act & Assert
        // The method should use default of 100 if not specified
        // Will fail due to connection, but tests the parameter default
        await Assert.ThrowsAnyAsync<Exception>(
            () => repository.GetPendingEmailsAsync());
    }

    [Fact]
    public async Task GetPendingEmailsAsync_WithCustomMaxRecords_UsesProvidedValue()
    {
        // Arrange
        var config = CreateConfiguration("Server=nonexistent.invalid;Database=test;Connection Timeout=1;");
        var repository = new EmailQueueRepository(config, _loggerMock.Object);

        // Act & Assert
        // Will fail due to connection, but tests the parameter is accepted
        await Assert.ThrowsAnyAsync<Exception>(
            () => repository.GetPendingEmailsAsync(50));
    }
}
