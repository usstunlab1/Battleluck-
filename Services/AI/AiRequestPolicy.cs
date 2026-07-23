namespace BattleLuck.Services.AI;

/// <summary>
/// Shared validation boundary for player-controlled text before it reaches an
/// action router, sidecar, or model provider.
/// </summary>
public static class AiRequestPolicy
{
    public const int MaxCharacters = 2048;

    public static bool TryValidate(string? value, out string normalized, out string error)
    {
        normalized = (value ?? string.Empty).Trim();
        error = string.Empty;

        if (normalized.Length == 0)
        {
            error = "AI request cannot be empty.";
            return false;
        }

        if (normalized.Length > MaxCharacters)
        {
            error = $"AI request is too long. Maximum: {MaxCharacters} characters.";
            return false;
        }

        foreach (var character in normalized)
        {
            if (!char.IsControl(character) || character is '\r' or '\n' or '\t')
                continue;

            error = "AI request contains unsupported control characters.";
            normalized = string.Empty;
            return false;
        }

        return true;
    }
}
