using SMTPPoller.Services;
using Xunit;

namespace SMTPPoller.Tests.Services;

public class SmtpThrottlingExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        // Arrange & Act
        var ex = new SmtpThrottlingException("Test message");

        // Assert
        Assert.Equal("Test message", ex.Message);
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_SetsBoth()
    {
        // Arrange
        var innerEx = new InvalidOperationException("Inner error");

        // Act
        var ex = new SmtpThrottlingException("Test message", innerEx);

        // Assert
        Assert.Equal("Test message", ex.Message);
        Assert.Same(innerEx, ex.InnerException);
    }

    [Fact]
    public void Constructor_WithStatusCodes_SetsProperties()
    {
        // Arrange & Act
        var ex = new SmtpThrottlingException("Test message", "421", "4.5.127");

        // Assert
        Assert.Equal("Test message", ex.Message);
        Assert.Equal("421", ex.StatusCode);
        Assert.Equal("4.5.127", ex.EnhancedStatusCode);
    }

    [Theory]
    [InlineData("4.5.127 Message rejected. Excessive message rate.")]
    [InlineData("Mailbox unavailable. The server response was: 4.5.127")]
    [InlineData("EXCESSIVE MESSAGE RATE from sender")]
    [InlineData("Too many messages sent")]
    [InlineData("Rate limit exceeded")]
    [InlineData("Request throttled")]
    [InlineData("421 Service not available, try again later")]
    [InlineData("452 Insufficient system storage")]
    public void IsThrottlingError_WithThrottlingMessage_ReturnsTrue(string message)
    {
        // Arrange
        var ex = new Exception(message);

        // Act
        var result = SmtpThrottlingException.IsThrottlingError(ex);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("Connection refused")]
    [InlineData("Invalid recipient address")]
    [InlineData("Authentication failed")]
    [InlineData("Network error")]
    [InlineData("Timeout expired")]
    [InlineData("550 User not found")]
    public void IsThrottlingError_WithNonThrottlingMessage_ReturnsFalse(string message)
    {
        // Arrange
        var ex = new Exception(message);

        // Act
        var result = SmtpThrottlingException.IsThrottlingError(ex);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsThrottlingError_ChecksInnerExceptionMessage()
    {
        // Arrange
        var innerEx = new Exception("4.5.127 Excessive message rate");
        var outerEx = new Exception("SMTP error", innerEx);

        // Act
        var result = SmtpThrottlingException.IsThrottlingError(outerEx);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsThrottlingError_WithNullMessage_ReturnsFalse()
    {
        // Arrange
        var ex = new Exception();

        // Act
        var result = SmtpThrottlingException.IsThrottlingError(ex);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsThrottlingError_CaseInsensitive()
    {
        // Arrange
        var ex = new Exception("RATE LIMIT exceeded");
        var ex2 = new Exception("rate limit exceeded");
        var ex3 = new Exception("Rate Limit Exceeded");

        // Act & Assert
        Assert.True(SmtpThrottlingException.IsThrottlingError(ex));
        Assert.True(SmtpThrottlingException.IsThrottlingError(ex2));
        Assert.True(SmtpThrottlingException.IsThrottlingError(ex3));
    }
}
