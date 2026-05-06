namespace SMTPPoller.Services;

/// <summary>
/// Service for managing SMTP throttling state and backoff logic.
/// </summary>
public interface ISmtpThrottleService
{
    /// <summary>
    /// Checks if sending is currently throttled and should be delayed.
    /// </summary>
    /// <returns>True if throttled, false if sending can proceed.</returns>
    bool IsThrottled();

    /// <summary>
    /// Gets the time until throttling expires.
    /// </summary>
    /// <returns>TimeSpan until throttle expires, or TimeSpan.Zero if not throttled.</returns>
    TimeSpan GetThrottleTimeRemaining();

    /// <summary>
    /// Records a throttling error and activates backoff.
    /// </summary>
    void RecordThrottling();

    /// <summary>
    /// Records a successful send, which can reduce backoff time.
    /// </summary>
    void RecordSuccess();

    /// <summary>
    /// Gets the recommended delay between messages during active throttling recovery.
    /// </summary>
    /// <returns>TimeSpan to wait between sends.</returns>
    TimeSpan GetInterMessageDelay();

    /// <summary>
    /// Resets all throttling state.
    /// </summary>
    void Reset();
}
