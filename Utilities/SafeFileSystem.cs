using System.Text.RegularExpressions;

namespace BattleLuck.Utilities;

public static class SafeFileSystem
{
    static readonly Regex SafeIdentifier = new(
        "^[a-z0-9][a-z0-9_-]{0,63}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly ConcurrentDictionary<string, object> WriteLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool IsSafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && SafeIdentifier.IsMatch(value.Trim());

    public static string RequireSafeIdentifier(string? value, string parameterName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (!IsSafeIdentifier(normalized))
            throw new ArgumentException(
                $"{parameterName} must contain only letters, numbers, '_' or '-' and be at most 64 characters.",
                parameterName);
        return normalized;
    }

    public static string CombineUnderRoot(string root, params string[] segments)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(new[] { fullRoot }.Concat(segments).ToArray()));
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resolved path escapes the configured root.");
        return candidate;
    }

    public static void WriteAllTextAtomic(string path, string content, Encoding? encoding = null)
    {
        var fullPath = Path.GetFullPath(path);
        var gate = WriteLocks.GetOrAdd(fullPath, static _ => new object());
        lock (gate)
        {
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Target path has no directory.");
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, encoding ?? new UTF8Encoding(false)))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
                WriteLocks.TryRemove(fullPath, out _);
            }
        }
    }
}
