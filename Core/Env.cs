namespace BattleLuck.Core;

/// <summary>
/// Provides lightweight environment variable resolution with optional local .env file support.
/// </summary>
/// <remarks>
/// Resolution order: local .env (case-insensitive) → process environment variable → fallback value.
/// </remarks>
internal static class Env
{
    private static readonly Dictionary<string, string?> _local = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the value for the specified environment variable key from the local .env store, falling back to the process environment.
    /// </summary>
    /// <param name="key">The environment variable key to retrieve.</param>
    /// <returns>The resolved value, or <c>null</c> if not found.</returns>
    public static string? Get(string key)
    {
        if (_local.Count > 0 && _local.TryGetValue(key, out var localValue) && !string.IsNullOrEmpty(localValue))
            return localValue;
        return Environment.GetEnvironmentVariable(key);
    }

    /// <summary>
    /// Gets the value for the specified environment variable key, throwing if the value is missing or whitespace.
    /// </summary>
    /// <param name="key">The environment variable key to retrieve.</param>
    /// <returns>The resolved non-empty value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the variable is not set or is empty.</exception>
    public static string Require(string key)
    {
        var value = Get(key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Required environment variable not set: {key}");
        return value!;
    }

    /// <summary>
    /// Loads environment variables from a .env-formatted file into the local store, replacing any previously loaded local values.
    /// </summary>
    /// <param name="path">The absolute or relative path to the .env file.</param>
    /// <exception cref="IOException">Thrown when the file cannot be read due to I/O errors.</exception>
    public static void Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        _local.Clear();
        var parsed = 0;
        var skipped = 0;

        if (!System.IO.File.Exists(path))
        {
            BattleLuckPlugin.Log?.LogInfo($"[Env] .env file not found at {path}. Using process environment variables.");
            return;
        }

        foreach (var raw in System.IO.File.ReadAllLines(path))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                skipped++;
                continue;
            }

            var equals = trimmed.IndexOf('=');
            if (equals <= 0)
            {
                skipped++;
                continue;
            }

            var key = trimmed[..equals].Trim();
            if (key.Length == 0)
            {
                skipped++;
                continue;
            }

            var value = trimmed[(equals + 1)..].Trim();
            if (value.Length >= 2 && ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
                value = value[1..^1];

            _local[key] = ResolveEnvReferences(value);
            parsed++;
        }

        BattleLuckPlugin.Log?.LogInfo($"[Env] Loaded {parsed} local env entries from {path} ({skipped} skipped).");
    }

    static string ResolveEnvReferences(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var result = value;
        var changed = true;
        var safety = 0;
        while (changed && safety++ < 10)
        {
            changed = false;
            var start = result.IndexOf("${", StringComparison.Ordinal);
            if (start < 0)
                break;

            var end = result.IndexOf('}', start);
            if (end <= start)
                break;

            var key = result.Substring(start + 2, end - start - 2);
            var replacement = Get(key) ?? string.Empty;
            if (!string.Equals(replacement, result.Substring(start, end - start + 1), StringComparison.Ordinal))
                changed = true;

            result = result.Substring(0, start) + replacement + result.Substring(end + 1);
        }

        return result;
    }

    /// <summary>
    /// Attempts to load a .env file from the configured BattleLuck config root.
    /// Failures are logged as warnings and do not prevent startup.
    /// </summary>
    public static void LoadFromConfigRoot()
    {
        try
        {
            var path = System.IO.Path.Combine(ConfigLoader.ConfigRoot, ".env");
            Load(path);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.Log?.LogWarning($"[Env] Failed to load .env from config root: {ex.Message}");
        }
    }
}
