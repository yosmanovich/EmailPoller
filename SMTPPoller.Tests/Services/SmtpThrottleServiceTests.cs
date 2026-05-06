using Microsoft.Extensions.Logging;
using Moq;
using SMTPPoller.Services;
using Xunit;

namespace SMTPPoller.Tests.Services;

public class SmtpThrottleServiceTests
{
    private readonly Mock<ILogger<SmtpThrottleService>> _loggerMock;
    private readonly SmtpThrottleService _service;

    public SmtpThrottleServiceTests()
    {
        _loggerMock = new Mock<ILogger<SmtpThrottleService>>();
        _service = new SmtpThrottleService(_loggerMock.Object);
    }

    [Fact]
    public void IsThrottled_Initially_ReturnsFalse()
    {
        // Act
        var result = _service.IsThrottled();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsThrottled_AfterRecordThrottling_ReturnsTrue()
    {
        // Arrange
        _service.RecordThrottling();

        // Act
        var result = _service.IsThrottled();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetThrottleTimeRemaining_WhenNotThrottled_ReturnsZero()
    {
        // Act
        var result = _service.GetThrottleTimeRemaining();

        // Assert
        Assert.Equal(TimeSpan.Zero, result);
    }

    [Fact]
    public void GetThrottleTimeRemaining_AfterRecordThrottling_ReturnsPositiveValue()
    {
        // Arrange
        _service.RecordThrottling();

        // Act
        var result = _service.GetThrottleTimeRemaining();

        // Assert
        Assert.True(result > TimeSpan.Zero);
        Assert.True(result <= TimeSpan.FromSeconds(30)); // Initial backoff is 30s
    }

    [Fact]
    public void RecordThrottling_MultipleTimesIncreases_BackoffExponentially()
    {
        // Arrange & Act
        _service.RecordThrottling(); // 30s backoff
        var firstBackoff = _service.GetThrottleTimeRemaining();

        // Need to reset and record twice for 60s backoff
        _service.Reset();
        _service.RecordThrottling();
        _service.RecordThrottling(); // Should now have 60s backoff
        var secondBackoff = _service.GetThrottleTimeRemaining();

        // Assert - second should be roughly double (accounting for time elapsed)
        Assert.True(secondBackoff > firstBackoff);
    }

    [Fact]
    public void GetInterMessageDelay_WhenNotThrottled_ReturnsZero()
    {
        // Act
        var result = _service.GetInterMessageDelay();

        // Assert
        Assert.Equal(TimeSpan.Zero, result);
    }

    [Fact]
    public void GetInterMessageDelay_AfterThrottling_ReturnsPositiveValue()
    {
        // Arrange
        _service.RecordThrottling();

        // Act
        var result = _service.GetInterMessageDelay();

        // Assert
        Assert.True(result > TimeSpan.Zero);
    }

    [Fact]
    public void RecordSuccess_AfterThrottling_DecreasesThrottleLevel()
    {
        // Arrange
        _service.RecordThrottling();
        var initialDelay = _service.GetInterMessageDelay();

        // Record enough successes to reduce throttle level
        for (int i = 0; i < 10; i++)
        {
            _service.RecordSuccess();
        }

        // Act
        var reducedDelay = _service.GetInterMessageDelay();

        // Assert - delay should be reduced (or zero if fully recovered)
        Assert.True(reducedDelay <= initialDelay);
    }

    [Fact]
    public void Reset_ClearsAllThrottleState()
    {
        // Arrange
        _service.RecordThrottling();
        Assert.True(_service.IsThrottled());

        // Act
        _service.Reset();

        // Assert
        Assert.False(_service.IsThrottled());
        Assert.Equal(TimeSpan.Zero, _service.GetThrottleTimeRemaining());
        Assert.Equal(TimeSpan.Zero, _service.GetInterMessageDelay());
    }

    [Fact]
    public void RecordSuccess_WhenNotThrottled_DoesNotThrow()
    {
        // Act & Assert - should not throw
        var exception = Record.Exception(() => _service.RecordSuccess());
        Assert.Null(exception);
    }

    [Fact]
    public void RecordThrottling_CapsBackoffAtMaximum()
    {
        // Arrange - record many throttles to hit max backoff
        for (int i = 0; i < 20; i++)
        {
            _service.RecordThrottling();
        }

        // Act
        var backoff = _service.GetThrottleTimeRemaining();

        // Assert - should not exceed 15 minutes
        Assert.True(backoff <= TimeSpan.FromMinutes(15));
    }
}
