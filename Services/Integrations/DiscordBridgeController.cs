using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using ProjectM.Terrain;
using Stunlock.Network;

/// <summary>
/// Discord Interactions endpoint for slash commands. Runs HttpListener on port 25581,
/// verifies signatures, handles PING, and dispatches commands with deferred responses.
/// Supported commands: join, leave, status, kit, heal, stats, leaderboard.
/// </summary>
public sealed class DiscordBridgeController : IDisposable
{
    HttpListener? _listener;
    DiscordBridgeConfig? _config;
    bool _running;
    readonly HttpClient _http = new();
    readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    readonly ConcurrentDictionary<string, ulong> _playerMap = new();
    readonly ConcurrentDictionary<string, PendingServantOrder> _pendingServantOrders = new();
    const int PendingOrderTtlMinutes = 10;

    static readonly string[] ServantRegionHints =
    {
        "Farbane", "Dunley", "Silverlight", "CursedForest",
        "HallowedMountains", "GloomrotNorth", "GloomrotSouth", "Mortium"
    };

    static readonly FieldInfo[] NetworkIdNumericFields = typeof(NetworkId)
        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(field =>
            field.FieldType == typeof(byte) || field.FieldType == typeof(sbyte) ||
            field.FieldType == typeof(short) || field.FieldType == typeof(ushort) ||
            field.FieldType == typeof(int) || field.FieldType == typeof(uint) ||
            field.FieldType == typeof(long) || field.FieldType == typeof(ulong))
        .ToArray();

    public void Configure(DiscordBridgeConfig? config)
    {
        if (config == null || !config.Enabled ||
            string.IsNullOrWhiteSpace(config.PublicKey) ||
            string.IsNullOrWhiteSpace(config.Token))
        {
            _config = null;
            return;
        }
        _config = config;

        // Build lookup for Discord → Steam ID
        _playerMap.Clear();
        foreach (var m in config.PlayerMappings)
            _playerMap[m.DiscordId] = m.SteamId;
    }

    public void Start()
    {
        if (_config == null || _running) return;

        try
        {
            _listener = new HttpListener();
            var bindAddress = string.IsNullOrWhiteSpace(_config.BindAddress) ? "+" : _config.BindAddress;
            _listener.Prefixes.Add($"http://{bindAddress}:{_config.Port}/discord/");
            _listener.Start();
            _running = true;
            Task.Run(ListenLoop);
            BattleLuckPlugin.LogInfo($"[DiscordBridge] Listening on {bindAddress}:{_config.Port}");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[DiscordBridge] Failed to start: {ex.Message}");
            _running = false;
        }
    }

    /// <summary>Call from main game thread each tick to drain queued actions.</summary>
    public void DrainMainThreadQueue()
    {
        int processed = 0;
        while (processed < 10 && _mainThreadQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[DiscordBridge] Main-thread action failed: {ex.Message}");
            }
            processed++;
        }
    }

    async Task ListenLoop()
    {
        while (_running && _listener != null)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(ctx));
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[DiscordBridge] Listen error: {ex.Message}");
            }
        }
    }

    void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var resp = ctx.Response;

            if (req.HttpMethod != "POST")
            {
                SendJson(resp, 405, new DiscordInteractionResponse { Type = 4, Data = new DiscordResponseData { Content = "POST only" } });
                return;
            }

            string body;
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                body = reader.ReadToEnd();

            // signature verification
            var signature = req.Headers["X-Signature"];
            var timestamp = req.Headers["X-Signature-Timestamp"];
            if (!Verif(signature, timestamp, body))
            {
                resp.StatusCode = 401;
                resp.Close();
                return;
            }

            var interaction = JsonSerializer.Deserialize<DiscordInteraction>(body);
            if (interaction == null)
            {
                resp.StatusCode = 400;
                resp.Close();
                return;
            }

            // Type 1 = PING
            if (interaction.Type == 1)
            {
                SendJson(resp, 200, new { type = 1 });
                return;
            }

            // Type 2 = APPLICATION_COMMAND — send deferred response (type 5), then process
            if (interaction.Type == 2)
            {
                SendJson(resp, 200, new DiscordInteractionResponse { Type = 5 });

                var commandName = interaction.Data?.Name ?? "";
                var discordUserId = interaction.Member?.User?.Id ?? "";
                var interactionToken = interaction.Token;
                var args = BuildArgs(interaction.Data?.Options);

                if (string.Equals(commandName, "ai", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(commandName, "ask", StringComparison.OrdinalIgnoreCase))
                {
                    _ = Task.Run(async () =>
                    {
                        var result = await HandleAiCommandAsync(discordUserId, args).ConfigureAwait(false);
                        await SendFollowUpAsync(interactionToken, result).ConfigureAwait(false);
                    });
                    return;
                }

                _mainThreadQueue.Enqueue(() =>
                {
                    var result = ProcessCommand(commandName, discordUserId, interaction.Data?.Options, args);
                    _ = Task.Run(() => SendFollowUpAsync(interactionToken, result));
                });
                return;
            }

            resp.StatusCode = 400;
            resp.Close();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[DiscordBridge] Request error: {ex.Message}");
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    bool Verif(string? signature, string? timestamp, string body)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(timestamp))
        {
            BattleLuckPlugin.LogWarning("[DiscordBridge] Missing signature headers — rejecting request");
            return false;
        }

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tsUnix) ||
            Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - tsUnix) > 300)
        {
            BattleLuckPlugin.LogWarning("[DiscordBridge] Stale/invalid timestamp — rejecting request");
            return false;
        }

        if (_config == null || string.IsNullOrWhiteSpace(_config.PublicKey))
        {
            BattleLuckPlugin.LogWarning("[DiscordBridge] Public key not configured — rejecting request");
            return false;
        }

        try
        {
            // Discord signature format: signature = timestamp + body
            var message = timestamp + body;
            var signatureBytes = Convert.FromHexString(signature);

            // Decode the public key (hex string)
            var publicKeyBytes = Convert.FromHexString(_config.PublicKey);

            // Ed25519 verification using BouncyCastle (not available in .NET 6 built-in crypto)
            var edParams = new Ed25519PublicKeyParameters(publicKeyBytes, 0);
            var verifier = new Ed25519Signer();
            verifier.Init(false, edParams);
            var msgBytes = System.Text.Encoding.UTF8.GetBytes(message);
            verifier.BlockUpdate(msgBytes, 0, msgBytes.Length);
            var isValid = verifier.VerifySignature(signatureBytes);

            if (!isValid)
            {
                BattleLuckPlugin.LogWarning("[DiscordBridge] Invalid signature — rejecting request");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[DiscordBridge] verification failed: {ex.Message} — rejecting request");
            return false;
        }
    }

    static Dictionary<string, string> BuildArgs(List<DiscordOption>? options)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options == null)
            return args;

        foreach (var option in options)
        {
            if (!string.IsNullOrWhiteSpace(option.Name) && option.Value != null)
                args[option.Name] = option.Value.ToString() ?? "";
        }

        return args;
    }

    string ProcessCommand(string command, string discordUserId, List<DiscordOption>? options, Dictionary<string, string> args)
    {
        BattleLuckPlugin.LogInfo($"[DiscordBridge] Processing command: {command} from {discordUserId}");

        var mappedSteamId = _playerMap.TryGetValue(discordUserId, out var sid) ? sid : (ulong?)null;

        // Respect command enable/disable flags from config
        if (_config != null && _config.Commands.Count > 0)
        {
            var cfg = _config.Commands.FirstOrDefault(c =>
                string.Equals(c.Name, command, StringComparison.OrdinalIgnoreCase));

            if (cfg != null && !cfg.Enabled)
                return $"🚫 Command '{command}' is disabled by server config.";
        }

        // Fire event for external handlers
        GameEvents.OnDiscordCommand?.Invoke(new DiscordCommandEvent
        {
            Command = command,
            DiscordUserId = discordUserId,
            MappedSteamId = mappedSteamId,
            Args = args
        });

        return command.ToLowerInvariant() switch
        {
            "status" => GetStatusResponse(),
            "join" => HandleJoinCommand(discordUserId, options),
            "leave" => HandleLeaveCommand(discordUserId),
            "kit" => HandleKitCommand(discordUserId, options),
            "heal" => HandleHealCommand(discordUserId),
            "stats" => HandleStatsCommand(discordUserId),
            "leaderboard" => GetLeaderboardResponse(),
            "servantdebug" => HandleServantDebugCommand(discordUserId, args),
            "servant" => HandleServantCommand(discordUserId, args),
            "flow_list" => HandleFlowListCommand(discordUserId, args),
            "flow_status" => HandleFlowStatusCommand(discordUserId),
            "flow_persist" => HandleFlowPersistCommand(discordUserId, args),
            _ => $"Unknown command: {command}"
        };
    }

    async Task<string> HandleAiCommandAsync(string discordUserId, Dictionary<string, string> args)
    {
        var aiAssistant = BattleLuckPlugin.AIAssistant;
        if (aiAssistant == null)
            return "AI Assistant is not initialized.";

        var query = GetArg(args, "query", "q", "prompt", "message");
        if (string.IsNullOrWhiteSpace(query))
            return "❌ Missing query. Example: /ai query:How do I improve in colosseum?";

        var steamId = _playerMap.TryGetValue(discordUserId, out var sid)
            ? sid
            : CreatePseudoSteamId(discordUserId);

        try
        {
            var response = await aiAssistant.HandleDirectQuery(steamId, query, "discord", broadcastToInGameChat: true).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response))
                return "⚠ AI returned no response.";

            PostToCommands($"🤖 Discord AI ({steamId}): {TrimForDiscord(response, 1700)}");
            return TrimForDiscord(response, 1800);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[DiscordBridge] AI command failed: {ex.Message}");
            return "❌ AI processing failed. Check server logs for details.";
        }
    }

    string HandleServantCommand(string discordUserId, Dictionary<string, string> args)
    {
        foreach (var kv in _pendingServantOrders)
            if (DateTime.UtcNow - kv.Value.CreatedUtc > TimeSpan.FromMinutes(PendingOrderTtlMinutes))
                _pendingServantOrders.TryRemove(kv.Key, out _);

        if (!_playerMap.TryGetValue(discordUserId, out var steamId))
            return "❌ Your Discord account is not linked to a Steam account.";

        var region = GetArg(args, "region", "zone", "area");
        if (string.IsNullOrWhiteSpace(region))
        {
            _pendingServantOrders[discordUserId] = new PendingServantOrder
            {
                SteamId = steamId,
                CreatedUtc = DateTime.UtcNow
            };

            return "🧭 Which region should your servants target? " +
                   $"Pick one of: {string.Join(", ", ServantRegionHints)}. " +
                   "Then run /servant again with region:<name>.";
        }

        _pendingServantOrders[discordUserId] = new PendingServantOrder
        {
            SteamId = steamId,
            Region = region,
            CreatedUtc = DateTime.UtcNow
        };

        var missionId = GetArg(args, "missionid", "mission", "mission_id");
        var throneId = GetArg(args, "thronenetid", "throne", "throne_id");
        var servantIds = GetArg(args, "servants", "servantnetids", "servant_ids");

        if (string.IsNullOrWhiteSpace(missionId) || string.IsNullOrWhiteSpace(throneId) || string.IsNullOrWhiteSpace(servantIds))
        {
            return "✅ Region selected: " + region + ". " +
                   "Next step: provide missionid, thronenetid, and servants (comma-separated network IDs). " +
                   "This works even if the player is offline because the command binds to entity network IDs. " +
                   "Example: /servant region:Dunley missionid:12 thronenetid:123456 servants:222,333,444";
        }

        if (!TryFindOnlinePlayer(steamId, out var playerEntity))
            return "❌ Your mapped Steam account must be online to authorize servant mission dispatch.";

        if (!playerEntity.Has<PlayerCharacter>())
            return "❌ Could not resolve your active player character.";

        var userEntity = playerEntity.Read<PlayerCharacter>().UserEntity;
        if (!userEntity.Exists() || !userEntity.Has<ProjectM.Network.User>())
            return "❌ Could not resolve your user entity for authorization.";

        if (!int.TryParse(missionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var missionDataId) || missionDataId <= 0)
            return "❌ Invalid missionid. Expected a positive integer mission data id.";

        if (!TryResolveMapZoneId(region, out var mapZoneId, out var mapZoneError))
            return $"❌ Invalid region/map zone: {mapZoneError}";

        if (!TryParseUnsignedId(throneId, out var throneNumericId))
            return "❌ Invalid thronenetid. Expected a numeric network id.";

        var servantTokens = servantIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (servantTokens.Length is < 1 or > 3)
            return "❌ servants must contain 1 to 3 comma-separated network ids.";

        var servantNumericIds = new List<ulong>(3);
        foreach (var token in servantTokens)
        {
            if (!TryParseUnsignedId(token, out var parsed))
                return $"❌ Invalid servant network id: '{token}'.";

            servantNumericIds.Add(parsed);
        }

        if (servantNumericIds.Distinct().Count() != servantNumericIds.Count)
            return "❌ Duplicate servant network ids are not allowed.";

        if (servantNumericIds.Contains(throneNumericId))
            return "❌ Throne network id cannot also be listed as a servant id.";

        var requestedNetworkIds = new HashSet<ulong>(servantNumericIds) { throneNumericId };
        if (!TryResolveNetworkIds(requestedNetworkIds, out var resolved, out var resolveError))
            return $"❌ Network id resolution failed: {resolveError}";

        if (!resolved.TryGetValue(throneNumericId, out var throneTarget))
            return "❌ Could not resolve throne entity from thronenetid.";

        var servantTargets = new List<(Entity Entity, NetworkId NetworkId)>();
        foreach (var servantNumericId in servantNumericIds)
        {
            if (!resolved.TryGetValue(servantNumericId, out var servantTarget))
                return $"❌ Could not resolve servant entity from id {servantNumericId}.";

            servantTargets.Add(servantTarget);
        }

        if (!TryValidateOwnershipByTeam(playerEntity, throneTarget.Entity, servantTargets.Select(target => target.Entity), out var ownershipError))
            return $"❌ Authorization failed: {ownershipError}";

        var sendOnMissionEvent = new SendOnMissionEvent
        {
            Throne = throneTarget.NetworkId,
            Servant1 = servantTargets.Count > 0 ? servantTargets[0].NetworkId : default,
            Servant2 = servantTargets.Count > 1 ? servantTargets[1].NetworkId : default,
            Servant3 = servantTargets.Count > 2 ? servantTargets[2].NetworkId : default,
            MissionDataID = missionDataId,
            MapZoneId = mapZoneId
        };

        if (!EcbHelper.TryGetEcb(out var ecb))
        {
            BattleLuckPlugin.LogWarning("[DiscordBridge] Servant mission NOT dispatched: server command buffer unavailable.");
            return "❌ Server command buffer is unavailable; could not dispatch servant mission. Try again shortly.";
        }

        var eventEntity = ecb.CreateEntity();
        ecb.AddComponent(eventEntity, sendOnMissionEvent);
        ecb.AddComponent(eventEntity, new FromCharacter
        {
            Character = playerEntity,
            User = userEntity
        });

        var details = $"steam={steamId}, region={region}, mission={missionDataId}, throne={throneNumericId}, servants={string.Join(',', servantNumericIds)}";
        BattleLuckPlugin.LogInfo($"[DiscordBridge] Servant mission dispatched: {details}");
        _pendingServantOrders.TryRemove(discordUserId, out _);
        PostToLogs($"🛡️ Servant mission dispatched: {details}");

        return "✅ Servant mission dispatched. Check throne state in-game and logs for follow-up validation.";
    }

    string HandleServantDebugCommand(string discordUserId, Dictionary<string, string> args)
    {
        if (!_playerMap.TryGetValue(discordUserId, out var steamId))
            return "❌ Your Discord account is not linked to a Steam account.";

        if (!TryFindOnlinePlayer(steamId, out var playerEntity))
            return "❌ Your mapped Steam account must be online to inspect servant candidates.";

        if (!playerEntity.Has<Team>())
            return "❌ Player has no team component; cannot inspect throne/servant ownership.";

        var playerTeam = playerEntity.Read<Team>().Value;
        var includeAllTeamEntities = ParseBoolArg(GetArg(args, "all", "full", "verbose"));

        var limit = 20;
        if (int.TryParse(GetArg(args, "limit", "max"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestedLimit))
            limit = Math.Clamp(requestedLimit, 1, 60);

        PrefabHelper.ScanLivePrefabs();

        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<Team>());
        var entities = query.ToEntityArray(Allocator.Temp);

        var throneCandidates = new List<string>();
        var servantCandidates = new List<string>();
        var fallbackCandidates = new List<string>();

        try
        {
            foreach (var entity in entities)
            {
                if (!entity.Exists())
                    continue;

                var team = entity.Read<Team>().Value;
                if (team != playerTeam)
                    continue;

                var networkId = entity.Read<NetworkId>();
                if (!TryExtractNetworkIdKeys(networkId, out var numericKeys))
                    continue;

                var numeric = numericKeys.OrderByDescending(value => value).FirstOrDefault();
                var prefabName = TryGetEntityPrefabName(entity);
                var lowered = prefabName.ToLowerInvariant();
                var descriptor = $"nid={numeric} net={networkId} prefab={prefabName}";

                var isThrone = lowered.Contains("throne", StringComparison.Ordinal);
                var isServant = lowered.Contains("servant", StringComparison.Ordinal);

                if (isThrone)
                    throneCandidates.Add(descriptor);

                if (isServant)
                    servantCandidates.Add(descriptor);

                if ((includeAllTeamEntities && !isThrone && !isServant) || (!isThrone && !isServant && fallbackCandidates.Count < 8))
                    fallbackCandidates.Add(descriptor);
            }
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
            query.Dispose();
        }

        if (throneCandidates.Count == 0 && servantCandidates.Count == 0 && fallbackCandidates.Count == 0)
            return "⚠ No team-owned entities with network ids were found for debug output.";

        var sb = new StringBuilder();
        sb.AppendLine($"🧭 Servant debug for steam {steamId} team {playerTeam}");

        if (throneCandidates.Count > 0)
        {
            sb.AppendLine("Throne candidates:");
            foreach (var line in throneCandidates.Take(limit))
                sb.AppendLine("- " + line);
        }

        if (servantCandidates.Count > 0)
        {
            sb.AppendLine("Servant candidates:");
            foreach (var line in servantCandidates.Take(limit))
                sb.AppendLine("- " + line);
        }

        if (fallbackCandidates.Count > 0)
        {
            sb.AppendLine(includeAllTeamEntities ? "Additional team entities:" : "Other team entities (sample):");
            foreach (var line in fallbackCandidates.Take(limit))
                sb.AppendLine("- " + line);
        }

        sb.AppendLine("Use /servant region:<name> missionid:<id> thronenetid:<nid> servants:<nid1,nid2,nid3>");

        var output = TrimForDiscord(sb.ToString(), 1800);
        PostToLogs($"[ServantDebug] {output}");
        return output;
    }

    static bool ParseBoolArg(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    static string TryGetEntityPrefabName(Entity entity)
    {
        if (!entity.Has<PrefabGUID>())
            return "(no-prefab-guid)";

        var guid = entity.Read<PrefabGUID>();
        var name = PrefabHelper.GetLivePrefabName(guid) ?? PrefabHelper.GetName(guid);
        return string.IsNullOrWhiteSpace(name) ? $"GUID_{guid.GuidHash}" : name;
    }

    static bool TryValidateOwnershipByTeam(Entity playerEntity, Entity throneEntity, IEnumerable<Entity> servantEntities, out string error)
    {
        error = string.Empty;

        if (!playerEntity.Has<Team>())
        {
            error = "Player has no team component; cannot validate ownership.";
            return false;
        }

        var playerTeam = playerEntity.Read<Team>().Value;

        if (!throneEntity.Has<Team>())
        {
            error = "Resolved throne has no team component.";
            return false;
        }

        var throneTeam = throneEntity.Read<Team>().Value;
        if (throneTeam != playerTeam)
        {
            error = "Throne does not belong to the mapped player's team.";
            return false;
        }

        foreach (var servantEntity in servantEntities)
        {
            if (!servantEntity.Has<Team>())
            {
                error = "One or more servant entities have no team component.";
                return false;
            }

            var servantTeam = servantEntity.Read<Team>().Value;
            if (servantTeam != playerTeam)
            {
                error = "One or more servant entities are outside the mapped player's team.";
                return false;
            }
        }

        return true;
    }

    static bool TryResolveMapZoneId(string regionOrZone, out MapZoneId mapZoneId, out string error)
    {
        mapZoneId = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(regionOrZone))
        {
            error = "region is required.";
            return false;
        }

        var mapZoneType = typeof(MapZoneId);
        if (!mapZoneType.IsEnum)
        {
            error = "MapZoneId runtime type is not an enum on this server build.";
            return false;
        }

        if (TryParseUnsignedId(regionOrZone, out var numericZone))
        {
            mapZoneId = (MapZoneId)Enum.ToObject(mapZoneType, unchecked((int)numericZone));
            return true;
        }

        var normalizedInput = NormalizeToken(regionOrZone);
        var exactName = Enum.GetNames(mapZoneType)
            .FirstOrDefault(name => string.Equals(NormalizeToken(name), normalizedInput, StringComparison.OrdinalIgnoreCase));

        if (exactName != null)
        {
            mapZoneId = (MapZoneId)Enum.Parse(mapZoneType, exactName, ignoreCase: true);
            return true;
        }

        var prefixMatch = Enum.GetNames(mapZoneType)
            .FirstOrDefault(name => NormalizeToken(name).Contains(normalizedInput, StringComparison.OrdinalIgnoreCase));

        if (prefixMatch != null)
        {
            mapZoneId = (MapZoneId)Enum.Parse(mapZoneType, prefixMatch, ignoreCase: true);
            return true;
        }

        error = $"'{regionOrZone}' did not match any known MapZoneId value.";
        return false;
    }

    static string NormalizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        var chars = token.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToLowerInvariant();
    }

    static bool TryResolveNetworkIds(
        HashSet<ulong> requested,
        out Dictionary<ulong, (Entity Entity, NetworkId NetworkId)> resolved,
        out string error)
    {
        resolved = new Dictionary<ulong, (Entity Entity, NetworkId NetworkId)>();
        error = string.Empty;

        if (requested.Count == 0)
        {
            error = "No network ids requested.";
            return false;
        }

        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
        var entities = query.ToEntityArray(Allocator.Temp);

        var ambiguous = new HashSet<ulong>();
        try
        {
            foreach (var entity in entities)
            {
                if (!entity.Exists())
                    continue;

                var networkId = entity.Read<NetworkId>();
                if (!TryExtractNetworkIdKeys(networkId, out var candidates))
                    continue;

                foreach (var key in candidates)
                {
                    if (!requested.Contains(key))
                        continue;

                    if (resolved.TryGetValue(key, out var existing) && existing.Entity != entity)
                    {
                        ambiguous.Add(key);
                        continue;
                    }

                    resolved[key] = (entity, networkId);
                }
            }
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
            query.Dispose();
        }

        if (ambiguous.Count > 0)
        {
            error = $"Ambiguous network ids matched multiple entities: {string.Join(", ", ambiguous.OrderBy(value => value))}";
            return false;
        }

        var resolvedSnapshot = resolved;
        var missing = requested.Where(key => !resolvedSnapshot.ContainsKey(key)).OrderBy(value => value).ToArray();
        if (missing.Length > 0)
        {
            error = $"No entity found for network ids: {string.Join(", ", missing)}";
            return false;
        }

        return true;
    }

    static bool TryExtractNetworkIdKeys(NetworkId networkId, out HashSet<ulong> keys)
    {
        keys = new HashSet<ulong>();

        var asString = networkId.ToString();
        if (TryParseUnsignedId(asString, out var direct))
            keys.Add(direct);

        object boxed = networkId;
        foreach (var field in NetworkIdNumericFields)
        {
            var value = field.GetValue(boxed);
            if (value == null)
                continue;

            switch (value)
            {
                case byte b:
                    keys.Add(b);
                    break;
                case sbyte sb when sb >= 0:
                    keys.Add((ulong)sb);
                    break;
                case short s when s >= 0:
                    keys.Add((ulong)s);
                    break;
                case ushort us:
                    keys.Add(us);
                    break;
                case int i when i >= 0:
                    keys.Add((ulong)i);
                    break;
                case uint ui:
                    keys.Add(ui);
                    break;
                case long l when l >= 0:
                    keys.Add((ulong)l);
                    break;
                case ulong ul:
                    keys.Add(ul);
                    break;
            }
        }

        return keys.Count > 0;
    }

    static bool TryParseUnsignedId(string value, out ulong parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            return true;

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
        {
            return true;
        }

        return false;
    }

    static string? GetArg(Dictionary<string, string> args, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (args.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    static ulong CreatePseudoSteamId(string discordUserId)
    {
        if (string.IsNullOrWhiteSpace(discordUserId))
            return 0;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(discordUserId.Trim()));
        return BitConverter.ToUInt64(hash, 0);
    }

    static string TrimForDiscord(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(empty)";

        var compact = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (compact.Length <= maxLength)
            return compact;

        return compact[..Math.Max(1, maxLength - 3)] + "...";
    }

    string GetStatusResponse()
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
            return "⚠ Session controller is not initialized.";

        var active = session.ActiveSessions.Count;
        var players = session.ActiveSessions.Values.Sum(s => s.Context.Players.Count);
        return $"🏟️ BattleLuck online. Active sessions: {active}, active players: {players}.";
    }

    string HandleJoinCommand(string discordUserId, List<DiscordOption>? options)
    {
        if (!_playerMap.TryGetValue(discordUserId, out var steamId))
            return "❌ Your Discord account is not linked to a Steam account. Ask an admin to add your mapping.";

        if (!TryFindOnlinePlayer(steamId, out var playerEntity))
            return "❌ Your mapped Steam account is not currently online in V Rising.";

        var session = BattleLuckPlugin.Session;
        if (session == null)
            return "⚠ Session controller is not initialized.";

        var modeId = options?.FirstOrDefault(o => o.Name == "mode")?.Value?.ToString();
        var result = session.ToggleEnter(steamId, playerEntity, string.IsNullOrWhiteSpace(modeId) ? null : modeId);
        return result.Success
            ? "✅ You joined the arena (kit applied, timer started if ready)."
            : $"❌ Join failed: {result.Error}";
    }

    string HandleLeaveCommand(string discordUserId)
    {
        if (!_playerMap.TryGetValue(discordUserId, out var steamId))
            return "❌ Your Discord account is not linked.";

        if (!TryFindOnlinePlayer(steamId, out var playerEntity))
            return "❌ Your mapped Steam account is not currently online in V Rising.";

        var session = BattleLuckPlugin.Session;
        if (session == null)
            return "⚠ Session controller is not initialized.";

        var result = session.ToggleLeave(steamId, playerEntity);
        return result.Success
            ? "✅ You left the arena and your snapshot was restored."
            : $"❌ Leave failed: {result.Error}";
    }

    string HandleKitCommand(string discordUserId, List<DiscordOption>? options)
    {
        if (!_playerMap.TryGetValue(discordUserId, out var steamId))
            return "❌ Your Discord account is not linked.";

        if (!TryFindOnlinePlayer(steamId, out var playerEntity))
            return "❌ Your mapped Steam account is not currently online in V Rising.";

        var kitName = options?.FirstOrDefault(o => o.Name == "name")?.Value?.ToString() ?? "default";

        // Current implementation applies the full competitive kit.
        KitController.ApplyFullKit(playerEntity);
        KitController.SetMaxLevel(playerEntity);
        AbilityController.UnlockAllAbilities(playerEntity);
        return $"✅ Applied BattleLuck kit '{kitName}' to your character.";
    }

    string HandleHealCommand(string discordUserId)
    {
        if (!_playerMap.TryGetValue(discordUserId, out var steamId))
            return "❌ Your Discord account is not linked.";

        if (!TryFindOnlinePlayer(steamId, out var playerEntity))
            return "❌ Your mapped Steam account is not currently online in V Rising.";

        if (!playerEntity.TryGetComponent(out Health health))
            return "❌ Could not resolve your health component.";

        health.Value = health.MaxHealth;
        playerEntity.Write(health);
        return "✅ You have been healed to full health.";
    }

    string HandleStatsCommand(string discordUserId)
    {
        if (!_playerMap.TryGetValue(discordUserId, out var steamId))
            return "❌ Your Discord account is not linked.";

        var session = BattleLuckPlugin.Session;
        if (session == null)
            return "⚠ Session controller is not initialized.";

        foreach (var kv in session.ActiveSessions)
        {
            var s = kv.Value;
            if (s.Context.Players.Contains(steamId))
            {
                var score = s.Context.Scores.GetPlayerScore(steamId);
                var rank = s.Context.Scores.GetLeaderboard().Take(20).ToList().IndexOf(steamId) + 1;
                var rankText = rank > 0 ? rank.ToString() : "unranked";
                return $"📊 Mode: {s.Context.ModeId} | Score: {score} | Rank: {rankText}";
            }
        }

        return "📊 You are not currently in an active session.";
    }

    string GetLeaderboardResponse()
    {
        var session = BattleLuckPlugin.Session;
        if (session == null || session.ActiveSessions.Count == 0)
            return "🏆 No active sessions right now.";

        var lines = new List<string>();
        foreach (var kv in session.ActiveSessions)
        {
            var s = kv.Value;
            var top = s.Context.Scores.GetLeaderboard().Take(3).ToList();
            if (top.Count == 0)
            {
                lines.Add($"{s.Context.ModeId}: no scores yet");
                continue;
            }

            var topLine = string.Join(", ", top.Select(id => $"{id}:{s.Context.Scores.GetPlayerScore(id)}"));
            lines.Add($"{s.Context.ModeId}: {topLine}");
        }

        return "🏆 " + string.Join(" | ", lines);
    }

    bool TryFindOnlinePlayer(ulong steamId, out Entity playerEntity)
    {
        foreach (var player in VRisingCore.GetOnlinePlayers())
        {
            if (player.Exists() && player.IsPlayer() && player.GetSteamId() == steamId)
            {
                playerEntity = player;
                return true;
            }
        }

        playerEntity = Entity.Null;
        return false;
    }

    async Task SendFollowUpAsync(string interactionToken, string content)
    {
        if (_config == null) return;
        try
        {
            var url = $"https://discord.com/api/v10/webhooks/{_config.ApplicationId}/{interactionToken}/messages/@original";
            var payload = JsonSerializer.Serialize(new { content });
            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            request.Headers.Add("Authorization", $"Bot {_config.Token}");
            await _http.SendAsync(request);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[DiscordBridge] Follow-up failed: {ex.Message}");
        }
    }

    static void SendJson(HttpListenerResponse resp, int status, object data)
    {
        resp.StatusCode = status;
        resp.ContentType = "application/json";
        var json = JsonSerializer.Serialize(data);
        var buffer = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = buffer.Length;
        resp.OutputStream.Write(buffer, 0, buffer.Length);
        resp.Close();
    }

    // ── Flow control commands ─────────────────────────────────────────────

    string HandleFlowListCommand(string discordUserId, Dictionary<string, string> args)
    {
        var modeId = GetArg(args, "mode", "modeid");
        var flowType = GetArg(args, "flow", "flowtype", "type");

        if (string.IsNullOrWhiteSpace(modeId))
            return "❌ Missing mode parameter. Example: /flow_list mode:bloodbath flow:enter";

        if (!TryParseFlowType(flowType, out var flowTypeValue))
            return "❌ Invalid flow type. Use 'enter' or 'exit'. Example: /flow_list mode:bloodbath flow:enter";

        var registry = BattleLuckPlugin.GameModes;
        if (registry?.Resolve(modeId) == null)
            return $"❌ Unknown mode: {modeId}";

        var flow = FlowOverrideManager.Instance.GetEffectiveFlow(modeId, flowTypeValue);
        var hasOverride = FlowOverrideManager.Instance.HasOverride(modeId, flowTypeValue);

        var sb = new StringBuilder();
        sb.AppendLine($"📋 Flow actions for {modeId}/{flowType} {(hasOverride ? "(OVERRIDE)" : "(BASE)")}:");
        sb.AppendLine($"Execution order: {string.Join(", ", flow.ExecutionOrder)}");

        int actionIndex = 0;
        foreach (var flowName in flow.ExecutionOrder)
        {
            if (!flow.Flows.TryGetValue(flowName, out var flowDef))
                continue;

            sb.AppendLine($"Flow: {flowName}");
            if (!string.IsNullOrEmpty(flowDef.Description))
                sb.AppendLine($"  Description: {flowDef.Description}");

            foreach (var action in flowDef.Actions.Take(20))
            {
                sb.AppendLine($"  [{actionIndex}] {action}");
                actionIndex++;
            }

            if (flowDef.Actions.Count > 20)
            {
                sb.AppendLine($"  ... and {flowDef.Actions.Count - 20} more actions");
                actionIndex += flowDef.Actions.Count - 20;
            }
        }

        sb.AppendLine($"Total actions: {actionIndex}");
        return TrimForDiscord(sb.ToString(), 1800);
    }

    string HandleFlowStatusCommand(string discordUserId)
    {
        var overrides = FlowOverrideManager.Instance.GetAllOverrides();

        if (overrides.Count == 0)
            return "✅ No pending flow overrides.";

        var sb = new StringBuilder();
        sb.AppendLine($"⚠ Pending overrides ({overrides.Count}):");
        foreach (var kvp in overrides.OrderBy(k => k.Key))
        {
            var parts = kvp.Key.Split(':');
            if (parts.Length == 2)
            {
                var timestamp = kvp.Value.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                sb.AppendLine($"  {parts[0]}/{parts[1]} (modified: {timestamp})");
            }
        }

        sb.AppendLine($"Summary: {FlowPersistence.GetPendingOverridesSummary()}");
        sb.AppendLine("Use in-game 'flow.persist <modeId>' to write changes to disk.");
        return TrimForDiscord(sb.ToString(), 1800);
    }

    string HandleFlowPersistCommand(string discordUserId, Dictionary<string, string> args)
    {
        var modeId = GetArg(args, "mode", "modeid");
        var flowType = GetArg(args, "flow", "flowtype", "type");
        var allFlag = GetArg(args, "all");

        bool persistAll = allFlag?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        if (persistAll)
        {
            var result = FlowPersistence.PersistAll();
            if (result.Success)
            {
                PostToLogs($"📝 Discord user {discordUserId} persisted all flow overrides");
                return $"✅ Persisted all flow overrides to config files.\nSummary: {FlowPersistence.GetPendingOverridesSummary()}";
            }
            else
            {
                return $"❌ Failed to persist: {result.Error}";
            }
        }

        if (!string.IsNullOrWhiteSpace(flowType))
        {
            if (!TryParseFlowType(flowType, out var flowTypeValue))
                return "❌ Invalid flow type. Use 'enter' or 'exit'.";
            if (string.IsNullOrWhiteSpace(modeId))
                return "❌ Missing mode parameter.";

            var registry = BattleLuckPlugin.GameModes;
            if (registry?.Resolve(modeId!) == null)
                return $"❌ Unknown mode: {modeId}";

            if (!FlowOverrideManager.Instance.HasOverride(modeId, flowTypeValue))
                return $"❌ No override exists for {modeId}/{flowType}.";

            var result = FlowPersistence.PersistFlow(modeId, flowTypeValue);
            if (result.Success)
            {
                PostToLogs($"📝 Discord user {discordUserId} persisted {modeId}/{flowType}");
                return $"✅ Persisted {modeId}/{flowType} to session.json.\nConfig reloaded.";
            }
            else
            {
                return $"❌ Failed to persist: {result.Error}";
            }
        }

        if (!string.IsNullOrWhiteSpace(modeId))
        {
            var registry = BattleLuckPlugin.GameModes;
            if (registry?.Resolve(modeId) == null)
                return $"❌ Unknown mode: {modeId}";

            if (!FlowPersistence.HasPendingOverrides(modeId))
                return $"❌ No pending overrides for {modeId}.";

            var result = FlowPersistence.PersistMode(modeId);
            if (result.Success)
            {
                PostToLogs($"📝 Discord user {discordUserId} persisted all flows for {modeId}");
                return $"✅ Persisted all flows for {modeId} to session.json.\nConfig reloaded.";
            }
            else
            {
                return $"❌ Failed to persist: {result.Error}";
            }
        }

        return "❌ Missing parameters. Use mode:<id> or all:true. Example: /flow_persist mode:bloodbath or /flow_persist all:true";
    }

    static bool TryParseFlowType(string? flowTypeStr, out FlowType flowType)
    {
        flowType = FlowType.Enter;
        if (string.IsNullOrWhiteSpace(flowTypeStr))
            return false;

        if (flowTypeStr.Equals("enter", StringComparison.OrdinalIgnoreCase))
        {
            flowType = FlowType.Enter;
            return true;
        }

        if (flowTypeStr.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            flowType = FlowType.Exit;
            return true;
        }

        return false;
    }

    // ── Channel webhook posting ────────────────────────────────────────

    /// <summary>Post a message to the configured "logs" Discord channel.</summary>
    public void PostToLogs(string message) => PostToChannel(_config?.Channels?.Logs, message);

    /// <summary>Post a message to the configured "chatvip" Discord channel.</summary>
    public void PostToChatVip(string message) => PostToChannel(_config?.Channels?.ChatVip, message);

    /// <summary>Post a message to the configured "commands" Discord channel.</summary>
    public void PostToCommands(string message) => PostToChannel(_config?.Channels?.Commands, message);

    /// <summary>Post a message to the configured "cmd" Discord channel.</summary>
    public void PostToCmd(string message) => PostToChannel(_config?.Channels?.Cmd, message);

    void PostToChannel(string? webhookUrl, string message)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { content = message });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                await _http.PostAsync(webhookUrl, content).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[DiscordBridge] Channel post failed: {ex.Message}");
            }
        });
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }

    sealed class PendingServantOrder
    {
        public ulong SteamId { get; init; }
        public string? Region { get; init; }
        public DateTime CreatedUtc { get; init; }
    }
}

