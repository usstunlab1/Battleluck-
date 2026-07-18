namespace BattleLuck.Services;

/// <summary>
/// Server-authoritative compact event console. An unmodified V Rising client
/// cannot receive a new fixed HUD panel, so this uses native system messages
/// and keeps all ranking/history state on the server.
/// </summary>
public sealed class EventConsoleService
{
    readonly GameModeContext _context;
    readonly EventConsoleSettings _settings;
    readonly Queue<string> _recentActions = new();
    readonly Dictionary<ulong, int> _deaths = new();
    readonly HashSet<ulong> _knownPlayers = new();
    readonly HashSet<ulong> _disabledPlayers = new();
    readonly HashSet<ulong> _enabledPlayers = new();
    readonly object _sync = new();
    float _refreshElapsed;

    public EventConsoleService(GameModeContext context, EventConsoleSettings? settings)
    {
        _context = context;
        _settings = settings ?? new EventConsoleSettings();
    }

    public bool Enabled => _settings.Enabled;

    public int GetDeaths(ulong steamId)
    {
        lock (_sync)
            return _deaths.GetValueOrDefault(steamId);
    }

    public void RecordPlayerJoined(ulong steamId)
    {
        if (steamId == 0) return;
        lock (_sync)
        {
            _knownPlayers.Add(steamId);
            EnqueueLocked($"{ResolveName(steamId)} JOINED");
        }
    }

    public void RecordPlayerLeft(ulong steamId)
    {
        if (steamId == 0) return;
        lock (_sync)
            EnqueueLocked($"{ResolveName(steamId)} LEFT");
    }

    public void RecordDeath(ulong victimSteamId, ulong? killerSteamId)
    {
        if (victimSteamId == 0) return;
        lock (_sync)
        {
            _knownPlayers.Add(victimSteamId);
            _deaths[victimSteamId] = _deaths.GetValueOrDefault(victimSteamId) + 1;
            var detail = killerSteamId is > 0 && killerSteamId != victimSteamId
                ? $" by {ResolveName(killerSteamId.Value)}"
                : "";
            EnqueueLocked($"{ResolveName(victimSteamId)} DOWN{detail}");
        }
    }

    public void RecordScoreAction(ulong steamId, string action, int points)
    {
        if (steamId == 0 || string.IsNullOrWhiteSpace(action)) return;
        lock (_sync)
        {
            _knownPlayers.Add(steamId);
            var delta = points == 0 ? "" : points > 0 ? $" +{points}" : $" {points}";
            EnqueueLocked($"{ResolveName(steamId)} {CompactAction(action)}{delta}");
        }
    }

    public void RecordFlowAction(ulong steamId, string action)
    {
        if (!ShouldDisplayFlowAction(action)) return;
        RecordScoreAction(steamId, action, 0);
    }

    public void RecordSystemAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        lock (_sync)
            EnqueueLocked(CompactAction(action));
    }

    public void SetVisible(ulong steamId, bool visible)
    {
        lock (_sync)
        {
            if (visible)
            {
                _disabledPlayers.Remove(steamId);
                _enabledPlayers.Add(steamId);
            }
            else
            {
                _enabledPlayers.Remove(steamId);
                _disabledPlayers.Add(steamId);
            }
        }
    }

    public bool IsVisible(ulong steamId)
    {
        lock (_sync)
            return _settings.AutoShow ? !_disabledPlayers.Contains(steamId) : _enabledPlayers.Contains(steamId);
    }

    public void Tick(float deltaSeconds, IReadOnlyList<Entity> onlinePlayers)
    {
        if (!Enabled) return;
        _refreshElapsed += Math.Max(0f, deltaSeconds);
        if (_refreshElapsed < Math.Clamp(_settings.RefreshSeconds, 5f, 120f))
            return;

        _refreshElapsed = 0f;
        SendAllNow(onlinePlayers);
    }

    public void SendAllNow(IReadOnlyList<Entity>? onlinePlayers = null)
    {
        if (!Enabled) return;
        var online = onlinePlayers ?? VRisingCore.GetOnlinePlayers().ToList();
        foreach (var steamId in _context.Players.ToList())
            SendToPlayer(steamId, online, force: false);
    }

    public bool SendNow(ulong steamId)
    {
        if (!Enabled) return false;
        return SendToPlayer(steamId, VRisingCore.GetOnlinePlayers().ToList(), force: true);
    }

    bool SendToPlayer(ulong steamId, IReadOnlyList<Entity> onlinePlayers, bool force)
    {
        if (!force && !IsVisible(steamId)) return false;
        var player = onlinePlayers.FirstOrDefault(entity =>
            entity.Exists() && entity.IsPlayer() && entity.GetSteamId() == steamId);
        if (!player.Exists() || !FlowController.TryGetUser(player, out var user) || !user.IsConnected)
            return false;

        NotificationHelper.NotifyPlayerRaw(user, BuildCompactMessage());
        return true;
    }

    public string BuildCompactMessage()
    {
        List<ulong> players;
        List<string> actions;
        Dictionary<ulong, int> deaths;
        lock (_sync)
        {
            foreach (var steamId in _context.Players)
                _knownPlayers.Add(steamId);
            foreach (var steamId in _context.Scores.GetAllPlayerScores().Keys)
                _knownPlayers.Add(steamId);

            deaths = new Dictionary<ulong, int>(_deaths);
            players = _knownPlayers
                .OrderByDescending(id => _context.Scores.GetPlayerScore(id))
                .ThenBy(id => deaths.GetValueOrDefault(id))
                .ThenBy(ResolveName, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(_settings.TopPlayers, 1, 6))
                .ToList();
            actions = _recentActions
                .Reverse()
                .Take(Math.Clamp(_settings.RecentActions, 1, 4))
                .ToList();
        }

        var remaining = _context.TimeLimitSeconds > 0
            ? TimeSpan.FromSeconds(Math.Max(0, _context.TimeLimitSeconds - _context.ElapsedSeconds)).ToString(@"mm\:ss")
            : "--:--";
        var sb = new StringBuilder(420);
        sb.Append("<color=#66E3FF>[EVENT CONSOLE] ")
          .Append(_context.ModeId.ToUpperInvariant())
          .Append("  ").Append(remaining).AppendLine("</color>");
        sb.AppendLine("#  PLAYER        SCORE  DEATHS");

        for (var index = 0; index < players.Count; index++)
        {
            var steamId = players[index];
            var name = FormatName(ResolveName(steamId));
            sb.Append(index + 1).Append("  ")
              .Append(name.PadRight(Math.Clamp(_settings.MaxNameLength, 6, 14)))
              .Append("  ")
              .Append(_context.Scores.GetPlayerScore(steamId).ToString(CultureInfo.InvariantCulture).PadLeft(5))
              .Append("  ")
              .Append(deaths.GetValueOrDefault(steamId).ToString(CultureInfo.InvariantCulture).PadLeft(6))
              .AppendLine();
        }

        if (actions.Count > 0)
            sb.Append("LAST: ").Append(string.Join(" | ", actions));
        return ClampUtf8(sb.ToString().TrimEnd(), 500);
    }

    void EnqueueLocked(string action)
    {
        _recentActions.Enqueue(ClampUtf8(action.Replace('\r', ' ').Replace('\n', ' '), 100));
        var limit = Math.Clamp(_settings.RecentActions, 1, 4);
        while (_recentActions.Count > limit)
            _recentActions.Dequeue();
    }

    string FormatName(string name)
    {
        var safe = name.Replace("<", "").Replace(">", "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        var limit = Math.Clamp(_settings.MaxNameLength, 6, 14);
        return safe.Length <= limit ? safe : safe[..limit];
    }

    static string ResolveName(ulong steamId)
    {
        var player = VRisingCore.GetOnlinePlayers().FirstOrDefault(entity =>
            entity.Exists() && entity.IsPlayer() && entity.GetSteamId() == steamId);
        var name = player.Exists() ? player.GetPlayerName() : "";
        if (!string.IsNullOrWhiteSpace(name))
            return name;
        var id = steamId.ToString(CultureInfo.InvariantCulture);
        return $"#{id[Math.Max(0, id.Length - 6)..]}";
    }

    static bool ShouldDisplayFlowAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return false;
        var hiddenPrefixes = new[]
        {
            "snapshot.", "player.snapshot", "inventory.", "equipment.", "kit.",
            "ability.", "passive.", "buff.", "blood.", "level.", "player.heal"
        };
        return !hiddenPrefixes.Any(prefix => action.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    static string CompactAction(string action)
    {
        var value = action.Trim();
        var separator = value.IndexOf(':');
        if (separator >= 0)
            value = value[..separator];
        return value.Replace('_', ' ').ToUpperInvariant();
    }

    static string ClampUtf8(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
            return value;
        while (value.Length > 0 && Encoding.UTF8.GetByteCount(value) > maxBytes - 3)
            value = value[..^1];
        return value + "...";
    }
}
