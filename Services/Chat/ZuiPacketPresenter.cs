using System.Collections.Concurrent;
using System.Text.Json;

namespace BattleLuck.Services.Chat;

public sealed record ZuiButton(string Text, string Command);
public sealed record ZuiWindow(string Id, string Title, IReadOnlyList<string> Lines, IReadOnlyList<ZuiButton> Buttons);

/// <summary>Server-only serializer for the optional ZUI client packet convention.</summary>
public sealed class ZuiPacketPresenter
{
    const string Prefix = "[[ZUI]]";
    const int MaxPacketChars = 6000;
    const int MaxLines = 30;
    const int MaxButtons = 12;
    static readonly ConcurrentDictionary<ulong, byte> OptedIn = new();
    readonly Action<ulong, string> _sendPrivate;

    public ZuiPacketPresenter(Action<ulong, string> sendPrivate) => _sendPrivate = sendPrivate;
    public bool IsEnabled(ulong steamId) => OptedIn.ContainsKey(steamId);
    public void Enable(ulong steamId) => OptedIn[steamId] = 0;
    public void Disable(ulong steamId) => OptedIn.TryRemove(steamId, out _);
    public void Clear() => OptedIn.Clear();

    public bool TrySend(ulong steamId, ZuiWindow window)
    {
        if (!IsEnabled(steamId) || string.IsNullOrWhiteSpace(window.Id)) return false;
        var safeButtons = window.Buttons.Take(MaxButtons).Where(button => IsAllowedCommand(button.Command))
            .Select(button => new { text = Plain(button.Text, 80), command = Plain(button.Command, 180) }).ToArray();
        var packet = Prefix + JsonSerializer.Serialize(new
        {
            type = "window",
            id = Plain(window.Id, 80),
            title = Plain(window.Title, 120),
            lines = window.Lines.Take(MaxLines).Select(line => Plain(line, 300)).ToArray(),
            buttons = safeButtons
        });
        if (packet.Length > MaxPacketChars) return false;
        _sendPrivate(steamId, packet);
        return true;
    }

    static bool IsAllowedCommand(string command)
    {
        var value = command.Trim();
        return value.Equals(".bl", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(".bl ", StringComparison.OrdinalIgnoreCase);
    }

    static string Plain(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var clean = value.Replace("<", "‹", StringComparison.Ordinal).Replace(">", "›", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return clean.Length <= max ? clean : clean[..max];
    }
}
