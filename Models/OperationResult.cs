/// <summary>
/// Generic result type for controller operations.
/// Implements Cloudflare-inspired UX principles:
/// - Scannable state names: Clear Success/Fail with descriptive labels
/// - Actionable troubleshooting: Error includes what went wrong and optional resolution guidance
/// - Consistent architecture: Every operation returns result with same structure
/// </summary>
public sealed class OperationResult<T>
{
    public bool Success { get; }
    public T? Value { get; }
    public string? Error { get; }
    /// <summary>A short, scannable label for the error state (e.g. "Session expired", "Zone full").</summary>
    public string? ErrorLabel { get; }
    /// <summary>Optional actionable guidance for the user to resolve the issue.</summary>
    public string? Troubleshooting { get; }

    OperationResult(bool success, T? value, string? error, string? errorLabel = null, string? troubleshooting = null)
    {
        Success = success;
        Value = value;
        Error = error;
        ErrorLabel = errorLabel;
        Troubleshooting = troubleshooting;
    }

    public static OperationResult<T> Ok(T value) => new(true, value, null);
    public static OperationResult<T> Fail(string error) => new(false, default, error);
    /// <summary>Fail with a scannable state label and optional troubleshooting tip.</summary>
    public static OperationResult<T> FailWithHelp(string error, string errorLabel, string? troubleshooting = null)
        => new(false, default, error, errorLabel, troubleshooting);

    /// <summary>Get a user-facing message combining the label and troubleshooting tip.</summary>
    public string UserMessage
    {
        get
        {
            if (Success) return "Operation completed.";
            var label = ErrorLabel ?? "Error";
            var msg = $"{label}: {Error}";
            if (!string.IsNullOrEmpty(Troubleshooting))
                msg += $"\n  Try: {Troubleshooting}";
            return msg;
        }
    }
}

/// <summary>Non-generic version for void operations.</summary>
public sealed class OperationResult
{
    public bool Success { get; }
    public string? Error { get; }
    /// <summary>A short, scannable label for the error state.</summary>
    public string? ErrorLabel { get; }
    /// <summary>Optional actionable guidance for the user to resolve the issue.</summary>
    public string? Troubleshooting { get; }

    OperationResult(bool success, string? error, string? errorLabel = null, string? troubleshooting = null)
    {
        Success = success;
        Error = error;
        ErrorLabel = errorLabel;
        Troubleshooting = troubleshooting;
    }

    public static OperationResult Ok() => new(true, null);
    public static OperationResult Fail(string error) => new(false, error);
    /// <summary>Fail with a scannable state label and optional troubleshooting tip.</summary>
    public static OperationResult FailWithHelp(string error, string errorLabel, string? troubleshooting = null)
        => new(false, error, errorLabel, troubleshooting);

    /// <summary>Get a user-facing message combining the label and troubleshooting tip.</summary>
    public string UserMessage
    {
        get
        {
            if (Success) return "Operation completed.";
            var label = ErrorLabel ?? "Error";
            var msg = $"{label}: {Error}";
            if (!string.IsNullOrEmpty(Troubleshooting))
                msg += $"\n  Try: {Troubleshooting}";
            return msg;
        }
    }
}