namespace BattleLuck.Services;

public sealed class MerchantCommandService
{
    readonly PlayerStateController _playerState = new();
    readonly Dictionary<PrefabGUID, List<MerchantCommandListing>> _byItem = new();
    readonly Dictionary<string, DateTime> _cooldowns = new(StringComparer.OrdinalIgnoreCase);
    readonly List<MerchantRentalState> _rentals = new();

    MerchantCommandConfig _config = new();
    DateTime _nextScanUtc = DateTime.MinValue;

    public bool IsEnabled => _config.Enabled;
    public IReadOnlyList<MerchantCommandListing> Listings => _config.Listings;
    public IReadOnlyList<MerchantRentalState> Rentals => _rentals;

    public void Configure(MerchantCommandConfig config)
    {
        _config = config ?? new MerchantCommandConfig();
        RebuildIndex();
    }

    public void Reload() => Configure(ConfigLoader.LoadMerchantCommandConfig());

    public void Tick(IReadOnlyList<Entity> onlinePlayers)
    {
        if (!_config.Enabled || !VRisingCore.IsReady)
            return;

        ExpireRentals();

        var now = DateTime.UtcNow;
        if (now < _nextScanUtc)
            return;

        _nextScanUtc = now.AddSeconds(Math.Clamp(_config.ScanIntervalSeconds, 0.5f, 30f));
        foreach (var player in onlinePlayers)
        {
            if (!player.Exists() || !player.IsPlayer())
                continue;
            if (!FlowController.TryGetUser(player, out var user) || !user.IsConnected)
                continue;

            TryActivateInventoryToken(player, user);
        }
    }

    public OperationResult ExecuteListing(Entity character, User user, string listingId, bool consumeInventoryItem)
    {
        var listing = FindListing(listingId);
        if (listing == null)
            return OperationResult.Fail($"Merchant listing '{listingId}' was not found.");
        return ExecuteListing(character, user, listing, consumeInventoryItem);
    }

    public OperationResult GrantToken(Entity character, string listingId, int amount = 1)
    {
        var listing = FindListing(listingId);
        if (listing == null)
            return OperationResult.Fail($"Merchant listing '{listingId}' was not found.");
        if (!TryResolveItem(listing, out var item, out var error))
            return OperationResult.Fail(error);

        amount = Math.Clamp(amount, 1, 999);
        return character.TrySendItemTo(item, amount)
            ? OperationResult.Ok()
            : OperationResult.Fail($"Could not grant merchant item {item.GuidHash}.");
    }

    void TryActivateInventoryToken(Entity character, User user)
    {
        foreach (var (item, listings) in _byItem)
        {
            if (CountInventoryItem(character, item) <= 0)
                continue;

            foreach (var listing in listings.Where(l => l.Enabled))
            {
                if (!CanUseListing(user, listing, notify: true))
                    continue;
                if (IsOnCooldown(user.PlatformId, listing, notifyUser: user))
                    continue;

                var consume = listing.ConsumeItem ?? _config.ConsumeItem;
                if (consume && !character.TryRemoveItem(item, 1))
                {
                    NotificationHelper.NotifyPlayer(user, $"Merchant token '{DisplayName(listing)}' could not be consumed.");
                    continue;
                }

                var result = ExecuteListing(character, user, listing, consumeInventoryItem: false);
                if (!result.Success)
                    NotificationHelper.NotifyPlayer(user, $"Merchant listing failed: {result.Error}");

                return;
            }
        }
    }

    OperationResult ExecuteListing(Entity character, User user, MerchantCommandListing listing, bool consumeInventoryItem)
    {
        if (!character.Exists())
            return OperationResult.Fail("Player character is not available.");
        if (!CanUseListing(user, listing, notify: false))
            return OperationResult.Fail("This merchant listing is admin-only.");
        if (IsOnCooldown(user.PlatformId, listing, notifyUser: null))
            return OperationResult.Fail("This merchant listing is on cooldown.");

        if (consumeInventoryItem)
        {
            if (!TryResolveItem(listing, out var item, out var itemError))
                return OperationResult.Fail(itemError);
            if (!character.TryRemoveItem(item, 1))
                return OperationResult.Fail($"Required merchant item '{DisplayName(listing)}' is not in inventory.");
        }

        var runtimeId = Guid.NewGuid().ToString("N");
        var sessionId = $"_merchant_{user.PlatformId}_{SafeId(listing.Id)}_{runtimeId[..8]}";
        var duration = listing.DurationSeconds ?? _config.DefaultDurationSeconds;
        var playerName = character.GetPlayerName();
        var context = new GameModeContext
        {
            SessionId = sessionId,
            ModeId = "merchant",
            ZoneHash = 0,
            TimeLimitSeconds = Math.Max(0, duration),
            Broadcast = message => NotificationHelper.NotifyPlayer(user, message)
        };
        context.Players.Add(user.PlatformId);

        var executor = new BattleLuck.Services.Flow.FlowActionExecutor(_playerState, BattleLuckPlugin.GameModes);
        var flowContext = new BattleLuck.Services.Flow.FlowActionContext
        {
            PlayerCharacter = character,
            PlayerState = _playerState,
            Registry = BattleLuckPlugin.GameModes,
            Config = new ModeConfig { ModeId = "merchant" },
            GameContext = context,
            ZoneHash = 0
        };

        var actions = BuildActions(listing, sessionId, runtimeId, character, user, duration).ToList();
        if (actions.Count == 0)
            return OperationResult.Fail($"Merchant listing '{listing.Id}' has no actions.");

        var ok = 0;
        var errors = new List<string>();
        foreach (var action in actions)
        {
            var result = executor.Execute(action, flowContext);
            if (result.Success) ok++;
            else errors.Add($"{action}: {result.Error}");
        }

        SetCooldown(user.PlatformId, listing);

        if (IsRentalListing(listing) && duration > 0)
        {
            _rentals.Add(new MerchantRentalState(
                sessionId,
                listing.Id,
                DisplayName(listing),
                user.PlatformId,
                playerName,
                DateTime.UtcNow.AddSeconds(duration)));
        }

        if (ok > 0)
        {
            NotificationHelper.NotifyPlayer(user, $"Merchant activated: {DisplayName(listing)}{(duration > 0 && IsRentalListing(listing) ? $" ({duration}s)" : "")}.");
            BattleLuckPlugin.LogInfo($"[MerchantCommands] {user.PlatformId} activated {listing.Id}: ok={ok}, failed={errors.Count}.");
            if (errors.Count > 0)
                BattleLuckPlugin.LogWarning($"[MerchantCommands] Listing {listing.Id} partial failures: {string.Join("; ", errors.Take(3))}");
            return OperationResult.Ok();
        }

        return OperationResult.Fail(errors.Count > 0 ? string.Join("; ", errors.Take(3)) : "No merchant actions executed.");
    }

    IEnumerable<string> BuildActions(MerchantCommandListing listing, string sessionId, string runtimeId, Entity character, User user, int duration)
    {
        var actions = listing.Actions.Count > 0
            ? listing.Actions
            : BuildDefaultActions(listing);

        var playerName = character.GetPlayerName();
        var rentalId = string.IsNullOrWhiteSpace(listing.BossId)
            ? $"rent_{SafeId(listing.Id)}_{runtimeId[..6]}"
            : listing.BossId;

        foreach (var action in actions)
        {
            var expanded = action
                .Replace("{listingId}", listing.Id, StringComparison.OrdinalIgnoreCase)
                .Replace("{uuid}", listing.Id, StringComparison.OrdinalIgnoreCase)
                .Replace("{sessionId}", sessionId, StringComparison.OrdinalIgnoreCase)
                .Replace("{runtimeId}", runtimeId, StringComparison.OrdinalIgnoreCase)
                .Replace("{rentalId}", rentalId, StringComparison.OrdinalIgnoreCase)
                .Replace("{bossId}", rentalId, StringComparison.OrdinalIgnoreCase)
                .Replace("{steamId}", user.PlatformId.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{playerName}", playerName, StringComparison.OrdinalIgnoreCase)
                .Replace("{durationSeconds}", duration.ToString(), StringComparison.OrdinalIgnoreCase);
            yield return expanded;
        }
    }

    List<string> BuildDefaultActions(MerchantCommandListing listing)
    {
        var actions = new List<string>();
        var kind = listing.Kind.Trim().ToLowerInvariant();

        if (kind is "boss" or "boss_rental" or "rental" || !string.IsNullOrWhiteSpace(listing.BossPrefab))
        {
            var prefab = string.IsNullOrWhiteSpace(listing.BossPrefab)
                ? "CHAR_Forest_Wolf_VBlood"
                : listing.BossPrefab;
            actions.Add($"npc.spawn:prefab={prefab}|npcId={{rentalId}}|offset=3,0,3|homeRadius={Math.Max(1f, listing.HomeRadius).ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            actions.Add($"boss.follow:bossId={{rentalId}}|target=self|followRange={Math.Max(1f, listing.FollowRange).ToString(System.Globalization.CultureInfo.InvariantCulture)}|leashRange={Math.Max(2f, listing.LeashRange).ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            return actions;
        }

        if (kind is "teleport" or "tp" || HasPosition(listing.TeleportPosition))
        {
            actions.Add($"teleport.position:position={FormatVec3(listing.TeleportPosition)}");
            return actions;
        }

        return actions;
    }

    void ExpireRentals()
    {
        if (_rentals.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var rental in _rentals.Where(r => r.ExpiresAtUtc <= now).ToList())
        {
            try { BattleLuckPlugin.NpcService?.DespawnSession(rental.SessionId); } catch { }
            _rentals.Remove(rental);
            BattleLuckPlugin.TryNotifyPlayerBySteamId(rental.SteamId, $"Merchant rental expired: {rental.DisplayName}.");
            BattleLuckPlugin.LogInfo($"[MerchantCommands] Rental expired: {rental.ListingId} owner={rental.SteamId} session={rental.SessionId}.");
        }
    }

    void RebuildIndex()
    {
        _byItem.Clear();
        if (!_config.Enabled)
            return;

        PrefabHelper.ScanLivePrefabs();
        foreach (var listing in _config.Listings.Where(l => l.Enabled))
        {
            if (!TryResolveItem(listing, out var item, out var error))
            {
                BattleLuckPlugin.LogWarning($"[MerchantCommands] Listing '{listing.Id}' skipped: {error}");
                continue;
            }

            if (!_byItem.TryGetValue(item, out var list))
            {
                list = new List<MerchantCommandListing>();
                _byItem[item] = list;
            }
            list.Add(listing);
        }

        BattleLuckPlugin.LogInfo($"[MerchantCommands] Loaded {_config.Listings.Count(l => l.Enabled)} listing(s), item triggers={_byItem.Count}.");
    }

    MerchantCommandListing? FindListing(string listingId)
        => _config.Listings.FirstOrDefault(l =>
            l.Id.Equals(listingId, StringComparison.OrdinalIgnoreCase) ||
            l.DisplayName.Equals(listingId, StringComparison.OrdinalIgnoreCase) ||
            (int.TryParse(listingId, out var number) && l.Number == number));

    bool TryResolveItem(MerchantCommandListing listing, out PrefabGUID item, out string error)
    {
        item = PrefabGUID.Empty;
        error = "";
        if (listing.ItemGuid.HasValue && listing.ItemGuid.Value != 0)
        {
            item = new PrefabGUID(listing.ItemGuid.Value);
            return true;
        }

        if (string.IsNullOrWhiteSpace(listing.ItemPrefab))
        {
            error = "itemPrefab or itemGuid is required.";
            return false;
        }

        if (int.TryParse(listing.ItemPrefab, out var hash))
        {
            item = new PrefabGUID(hash);
            return true;
        }

        var resolved = PrefabHelper.GetLivePrefabGuid(listing.ItemPrefab) ??
                       PrefabHelper.GetPrefabGuidDeep(listing.ItemPrefab);
        if (resolved.HasValue)
        {
            item = resolved.Value;
            return true;
        }

        error = $"item prefab '{listing.ItemPrefab}' was not found.";
        return false;
    }

    bool CanUseListing(User user, MerchantCommandListing listing, bool notify)
    {
        if (!listing.AdminOnly || user.IsAdmin)
            return true;

        if (notify)
            NotificationHelper.NotifyPlayer(user, $"Merchant listing '{DisplayName(listing)}' is admin-only.");
        return false;
    }

    bool IsOnCooldown(ulong steamId, MerchantCommandListing listing, User? notifyUser)
    {
        var key = $"{steamId}:{listing.Id}";
        if (!_cooldowns.TryGetValue(key, out var until) || until <= DateTime.UtcNow)
            return false;

        if (notifyUser.HasValue)
            NotificationHelper.NotifyPlayer(notifyUser.Value, $"Merchant listing '{DisplayName(listing)}' is cooling down ({Math.Ceiling((until - DateTime.UtcNow).TotalSeconds)}s).");
        return true;
    }

    void SetCooldown(ulong steamId, MerchantCommandListing listing)
    {
        var seconds = listing.CooldownSeconds ?? _config.DefaultCooldownSeconds;
        if (seconds > 0)
            _cooldowns[$"{steamId}:{listing.Id}"] = DateTime.UtcNow.AddSeconds(seconds);
    }

    static int CountInventoryItem(Entity player, PrefabGUID item)
    {
        var em = VRisingCore.EntityManager;
        if (!InventoryUtilities.TryGetInventoryEntity(em, player, out var inventoryEntity) ||
            !em.HasBuffer<InventoryBuffer>(inventoryEntity))
            return 0;

        var total = 0;
        var buffer = em.GetBuffer<InventoryBuffer>(inventoryEntity);
        for (var i = 0; i < buffer.Length; i++)
        {
            var slot = buffer[i];
            if (slot.ItemType.GuidHash == item.GuidHash)
                total += slot.Amount;
        }
        return total;
    }

    static bool IsRentalListing(MerchantCommandListing listing)
        => listing.Kind.Contains("boss", StringComparison.OrdinalIgnoreCase) ||
           listing.Kind.Contains("rental", StringComparison.OrdinalIgnoreCase) ||
           !string.IsNullOrWhiteSpace(listing.BossPrefab);

    static string DisplayName(MerchantCommandListing listing)
        => string.IsNullOrWhiteSpace(listing.DisplayName) ? listing.Id : listing.DisplayName;

    static string SafeId(string value)
        => new(value.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());

    static bool HasPosition(Vec3Config value)
        => Math.Abs(value.X) > 0.0001f || Math.Abs(value.Y) > 0.0001f || Math.Abs(value.Z) > 0.0001f;

    static string FormatVec3(Vec3Config value)
        => string.Join(",",
            value.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            value.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            value.Z.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

public sealed record MerchantRentalState(
    string SessionId,
    string ListingId,
    string DisplayName,
    ulong SteamId,
    string PlayerName,
    DateTime ExpiresAtUtc);
