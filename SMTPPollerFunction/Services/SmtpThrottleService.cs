using Microsoft.Extensions.Logging;

namespace SMTPPoller.Services;

/// <summary>
/// Service for managing SMTP throttling state with exponential backoff.
/// Thread-safe singleton that persists throttle state across function invocations.
/// </summary>
public class SmtpThrottleService : ISmtpThrottleService
{
    private readonly ILogger<SmtpThrottleService> _logger;
    private readonly object _lock = new();

    // Throttle state
    private DateTime _throttledUntil = DateTime.MinValue;
    private int _consecutiveThrottles = 0;
    private int _successesSinceThrottle = 0;

    // Configuration
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan BaseInterMessageDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxInterMessageDelay = TimeSpan.FromSeconds(5);
    private const int SuccessesRequiredToReduceDelay = 10;
    private const double BackoffMultiplier = 2.0;

    public SmtpThrottleService(ILogger<SmtpThrottleService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsThrottled()
    {
        lock (_lock)
        {
            return DateTime.UtcNow < _throttledUntil;
        }
    }

    /// <inheritdoc/>
    public TimeSpan GetThrottleTimeRemaining()
    {
        lock (_lock)
        {
            if (DateTime.UtcNow >= _throttledUntil)
                return TimeSpan.Zero;

            return _throttledUntil - DateTime.UtcNow;
        }
    }

    /// <inheritdoc/>
    public void RecordThrottling()
    {
        lock (_lock)
        {
            _consecutiveThrottles++;
            _successesSinceThrottle = 0;

            // Exponential backoff: 30s, 60s, 120s, 240s, ... up to 15 minutes
            var backoffSeconds = InitialBackoff.TotalSeconds * Math.Pow(BackoffMultiplier, _consecutiveThrottles - 1);
            var backoff = TimeSpan.FromSeconds(Math.Min(backoffSeconds, MaxBackoff.TotalSeconds));

            _throttledUntil = DateTime.UtcNow.Add(backoff);

            _logger.LogWarning(
                "SMTP throttling detected. Backing off for {BackoffSeconds:F0} seconds (attempt #{Attempt}). " +
                "Will resume at {ResumeTime:O}",
                backoff.TotalSeconds,
                _consecutiveThrottles,
                _throttledUntil);
        }
    }

    /// <inheritdoc/>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _successesSinceThrottle++;

            // After enough successes, reduce the consecutive throttle count
            if (_consecutiveThrottles > 0 && _successesSinceThrottle >= SuccessesRequiredToReduceDelay)
            {
                _consecutiveThrottles = Math.Max(0, _consecutiveThrottles - 1);
                _successesSinceThrottle = 0;

                _logger.LogInformation(
                    "SMTP throttle recovery: {Successes} successful sends. Reduced throttle level to {Level}",
                    SuccessesRequiredToReduceDelay,
                    _consecutiveThrottles);
            }
        }
    }

    /// <inheritdoc/>
    public TimeSpan GetInterMessageDelay()
    {
        lock (_lock)
        {
            if (_consecutiveThrottles == 0)
                return TimeSpan.Zero;

            // Scale delay based on recent throttling
            var delayMs = BaseInterMessageDelay.TotalMilliseconds * Math.Pow(1.5, _consecutiveThrottles - 1);
            return TimeSpan.FromMilliseconds(Math.Min(delayMs, MaxInterMessageDelay.TotalMilliseconds));
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (_lock)
        {
            _throttledUntil = DateTime.MinValue;
            _consecutiveThrottles = 0;
            _successesSinceThrottle = 0;

            _logger.LogInformation("SMTP throttle state reset");
        }
    }
}
