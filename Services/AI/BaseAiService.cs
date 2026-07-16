using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BattleLuck.Core;

/// <summary>
/// Base class for AI service implementations providing common functionality:
/// - HTTP client management
/// - Rate limiting
/// - Credential validation
/// - Error tracking
/// - Disposal pattern
/// </summary>
public abstract class BaseAiService : IDisposable
{
    protected readonly HttpClient _httpClient;
    protected readonly SemaphoreSlim _rateLimiter;
    protected readonly int _maxRequestsPerSecond;
    protected readonly int _timeoutSeconds;
    protected DateTime _lastRequestTime;
    protected bool _disposed;
    protected bool _authFailed;

    public abstract bool IsEnabled { get; }
    public bool AuthFailed => _authFailed;
    public string? LastError { get; protected set; }
    public DateTime? LastSuccessfulCallUtc { get; protected set; }

    protected BaseAiService(int maxRequestsPerSecond = 10, int timeoutSeconds = 90)
    {
        _maxRequestsPerSecond = maxRequestsPerSecond > 0 ? maxRequestsPerSecond : 10;
        _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 90;
        _rateLimiter = new SemaphoreSlim(_maxRequestsPerSecond, _maxRequestsPerSecond);
        _lastRequestTime = DateTime.MinValue;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_timeoutSeconds)
        };
    }

    /// <summary>
    /// Apply rate limiting before making an API call.
    /// </summary>
    protected async Task ApplyRateLimitAsync()
    {
        await _rateLimiter.WaitAsync();
        try
        {
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
            var minInterval = 1000.0 / _maxRequestsPerSecond;
            if (timeSinceLastRequest.TotalMilliseconds < minInterval)
            {
                var delayMs = (int)(minInterval - timeSinceLastRequest.TotalMilliseconds);
                if (delayMs > 0)
                    await Task.Delay(delayMs);
            }
            _lastRequestTime = DateTime.UtcNow;
        }
        catch
        {
            _rateLimiter.Release();
            throw;
        }
    }

    /// <summary>
    /// Release the rate limiter semaphore (call in finally block).
    /// </summary>
    protected void ReleaseRateLimit()
    {
        _rateLimiter.Release();
    }

    /// <summary>
    /// Check if a credential value is usable (not a placeholder).
    /// </summary>
    protected static bool IsUsableCredential(string? value, int minLength = 8)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        var upper = trimmed.ToUpperInvariant();
        if (upper.Contains("YOUR_") ||
            upper.Contains("REPLACE") ||
            upper.Contains("PLACEHOLDER") ||
            upper.Contains("TOKEN_HERE") ||
            upper.Contains("API_TOKEN") ||
            upper.Contains("ACCOUNT_ID") ||
            upper.Contains("API_KEY") ||
            upper.Contains("CHANGE_ME") ||
            upper.Contains("CHANGEME") ||
            upper.Contains("DUMMY"))
            return false;

        return trimmed.Length >= minLength;
    }

    /// <summary>
    /// Handle authentication failure from HTTP response.
    /// </summary>
    protected void HandleAuthFailure(string serviceName, string errorDetails)
    {
        _authFailed = true;
        LastError = errorDetails;
        BattleLuckLogger.Warning($"{serviceName} auth failed; provider disabled until reload. {errorDetails}");
    }

    /// <summary>
    /// Handle HTTP error response.
    /// </summary>
    protected void HandleHttpError(string serviceName, HttpStatusCode statusCode, string responseJson)
    {
        LastError = $"HTTP {(int)statusCode}: {responseJson}";
        BattleLuckLogger.Warning($"{serviceName} API error: {LastError}");
    }

    /// <summary>
    /// Handle timeout exception.
    /// </summary>
    protected void HandleTimeout(string serviceName, Exception ex)
    {
        LastError = $"{serviceName} timeout: {ex.Message}";
        BattleLuckLogger.Warning(LastError);
    }

    /// <summary>
    /// Handle general exception.
    /// </summary>
    protected void HandleException(string serviceName, Exception ex)
    {
        LastError = ex.Message;
        BattleLuckLogger.Warning($"{serviceName} error: {ex.Message}");
    }

    /// <summary>
    /// Record successful API call.
    /// </summary>
    protected void RecordSuccess()
    {
        LastSuccessfulCallUtc = DateTime.UtcNow;
        LastError = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient?.Dispose();
        _rateLimiter?.Dispose();
    }
}
