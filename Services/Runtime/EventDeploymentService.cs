using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BattleLuck.Core.Validation;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// No-code event deployment boundary. Admins can publish the four declarative
/// event files from a HTTPS GitHub Gist without touching C# or the server file
/// system. Files are downloaded into a staging folder, validated, backed up, and
/// only then switched into the live events directory.
/// </summary>
public sealed class EventDeploymentService
{
    public static readonly string[] RequiredFiles = { "flow.json", "zones.json", "kits.json", "prompt.txt" };

    static readonly Regex ValidId = new("^[a-z0-9][a-z0-9_-]{1,31}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly HttpClient Http = CreateHttpClient();
    const int MaxFileBytes = 2 * 1024 * 1024;

    readonly EventDefinitionLoader _definitions = new();
    readonly ActionManifestService _actions = new();

    public async Task<OperationResult<EventDeploymentResult>> DeployFromGistAsync(
        string modeId,
        string gistUrl,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        modeId = NormalizeId(modeId);
        if (modeId.Length == 0)
            return OperationResult<EventDeploymentResult>.Fail("EINVALIDID: Event id must use lowercase letters, numbers, '_' or '-'.");

        var snapshotGuard = OperatorSafetyService.EnsureDeploymentSnapshot(modeId);
        if (!snapshotGuard.Success)
            return OperationResult<EventDeploymentResult>.Fail(snapshotGuard.Error ?? "E_NO_SNAPSHOT: Deployment snapshot required.");

        if (!TryBuildGistFileUrls(gistUrl, out var fileUrls, out var urlError))
            return OperationResult<EventDeploymentResult>.Fail("EGIST: " + urlError);

        Dictionary<string, string> files;
        try
        {
            files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in RequiredFiles)
                files[file] = await DownloadTextAsync(fileUrls[file], cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OperationResult<EventDeploymentResult>.Fail($"EGIST: Gist download failed: {ex.Message}");
        }

        if (!NormalizeFlowMetadata(modeId, files, out var normalizeError))
            return OperationResult<EventDeploymentResult>.Fail("ESCHEMA: " + normalizeError);

        if (IsModeActive(modeId))
            return OperationResult<EventDeploymentResult>.Fail($"EACTIVE: Event '{modeId}' is active. End the event before deploying a new definition.");

        var validation = ValidateBundle(modeId, files, excludeModeId: modeId);
        if (!validation.Success)
            return OperationResult<EventDeploymentResult>.Fail(
                "ESCHEMA: Deployment rejected by validation: " + string.Join("; ", validation.Errors.Take(8)));

        if (dryRun)
        {
            var flow = JsonSerializer.Deserialize<UnifiedEventDefinition>(files["flow.json"], ConfigLoader.JsonOptions)!;
            var zoneHash = flow.Zones.FirstOrDefault()?.Hash ?? 0;
            return OperationResult<EventDeploymentResult>.Ok(new EventDeploymentResult(
                modeId, $"dry-run:{gistUrl.Trim()}", "dry-run (not installed)", flow.Zones.Count, zoneHash));
        }

        return InstallBundle(modeId, files, gistUrl.Trim());
    }

    public OperationResult<EventDeploymentStatus> GetStatus(string? requestedModeId = null)
    {
        var modeId = string.IsNullOrWhiteSpace(requestedModeId) ? "" : NormalizeId(requestedModeId);
        if (!string.IsNullOrWhiteSpace(requestedModeId) && modeId.Length == 0)
            return OperationResult<EventDeploymentStatus>.Fail("EINVALIDID: Event id must use lowercase letters, numbers, '_' or '-'.");

        var modeIds = modeId.Length > 0
            ? new[] { modeId }
            : Directory.Exists(EventsRoot)
                ? Directory.EnumerateDirectories(EventsRoot)
                    .Select(Path.GetFileName)
                    .Where(value => !string.IsNullOrWhiteSpace(value) && !value!.StartsWith('.'))
                    .Select(value => value!)
                    .Where(value => !value.Equals("schemas", StringComparison.OrdinalIgnoreCase))
                    .Where(value => ValidId.IsMatch(value))
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();

        var statuses = modeIds.Select(BuildStatus).ToList();
        if (modeId.Length > 0)
            return OperationResult<EventDeploymentStatus>.Ok(statuses[0]);

        // The command surface renders one status at a time; an empty id uses the
        // first aggregate status and the caller can request a named status for detail.
        var aggregate = new EventDeploymentStatus
        {
            ModeId = "all",
            HasDirectory = statuses.Any(s => s.HasDirectory),
            HasAllFiles = statuses.Count > 0 && statuses.All(s => s.HasAllFiles),
            FlowValid = statuses.Count > 0 && statuses.All(s => s.FlowValid),
            Registered = statuses.Count > 0 && statuses.All(s => s.Registered),
            ZoneHashes = statuses.SelectMany(s => s.ZoneHashes).Distinct().OrderBy(x => x).ToList(),
            LatestBackup = statuses.Select(s => s.LatestBackup).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "none",
            Errors = statuses.SelectMany(s => s.Errors).Take(8).ToList(),
            EventCount = statuses.Count
        };
        return OperationResult<EventDeploymentStatus>.Ok(aggregate);
    }

    public OperationResult<EventDeploymentResult> Rollback(string requestedModeId)
    {
        var modeId = NormalizeId(requestedModeId);
        if (modeId.Length == 0)
            return OperationResult<EventDeploymentResult>.Fail("EINVALIDID: Event id must use lowercase letters, numbers, '_' or '-'.");

        var backup = FindLatestBackup(modeId);
        if (backup == null)
            return OperationResult<EventDeploymentResult>.Fail($"EBACKUP: No known-good backup exists for '{modeId}'.");

        if (!VerifyManifest(backup, modeId, out var manifestError))
            return OperationResult<EventDeploymentResult>.Fail("EBACKUP: " + manifestError);

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in RequiredFiles)
                files[file] = File.ReadAllText(Path.Combine(backup, file));
        }
        catch (Exception ex)
        {
            return OperationResult<EventDeploymentResult>.Fail($"EBACKUP: Backup could not be read: {ex.Message}");
        }

        if (!NormalizeFlowMetadata(modeId, files, out var normalizeError))
            return OperationResult<EventDeploymentResult>.Fail("ESCHEMA: " + normalizeError);

        if (IsModeActive(modeId))
            return OperationResult<EventDeploymentResult>.Fail($"EACTIVE: Event '{modeId}' is active. End the event before rollback.");

        var validation = ValidateBundle(modeId, files, excludeModeId: modeId);
        if (!validation.Success)
            return OperationResult<EventDeploymentResult>.Fail(
                "ESCHEMA: Rollback backup failed validation: " + string.Join("; ", validation.Errors.Take(8)));

        return InstallBundle(modeId, files, $"backup:{Path.GetFileName(backup)}");
    }

    /// <summary>
    /// Delete one BattleLuck deployment backup after an explicit confirmation.
    /// This never targets the V Rising SaveFileManager/world save directory.
    /// </summary>
    public OperationResult<EventDeploymentResult> DeleteBackup(string requestedModeId, string? requestedBackupId, bool confirmed)
    {
        var modeId = NormalizeId(requestedModeId);
        if (modeId.Length == 0)
            return OperationResult<EventDeploymentResult>.Fail("EINVALIDID: Event id must use lowercase letters, numbers, '_' or '-'.");
        if (!confirmed)
            return OperationResult<EventDeploymentResult>.Fail("EPURGE_CONFIRM: Repeat the command with the final 'confirm' token.");
        if (IsModeActive(modeId))
            return OperationResult<EventDeploymentResult>.Fail($"EACTIVE: Event '{modeId}' is active; end it before deleting a recovery backup.");

        var latest = FindLatestBackup(modeId);
        if (latest == null)
            return OperationResult<EventDeploymentResult>.Fail($"EBACKUP: No BattleLuck deployment backup exists for '{modeId}'.");

        var backupId = string.IsNullOrWhiteSpace(requestedBackupId) ? Path.GetFileName(latest) : requestedBackupId.Trim();
        if (backupId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            backupId is "." or ".." || backupId.Contains('/') || backupId.Contains('\\'))
            return OperationResult<EventDeploymentResult>.Fail("EPURGE: Backup id must be a single directory name.");

        var target = Path.Combine(BackupsRoot, modeId, backupId);
        if (!Directory.Exists(target))
            return OperationResult<EventDeploymentResult>.Fail($"EBACKUP: BattleLuck backup '{backupId}' was not found.");

        try
        {
            Directory.Delete(target, recursive: true);
            return OperationResult<EventDeploymentResult>.Ok(new EventDeploymentResult(modeId, $"purge:{backupId}", target, 0, 0));
        }
        catch (Exception ex)
        {
            return OperationResult<EventDeploymentResult>.Fail($"EPURGE: Could not delete BattleLuck backup '{backupId}': {ex.Message}");
        }
    }

    OperationResult<EventDeploymentResult> InstallBundle(
        string modeId,
        Dictionary<string, string> files,
        string source)
    {
        ConfigLoader.EnsureDefaultsDeployed();
        Directory.CreateDirectory(EventsRoot);
        Directory.CreateDirectory(BackupsRoot);

        var target = EventDirectory(modeId);
        var staging = Path.Combine(EventsRoot, $".{modeId}.deploying-{Guid.NewGuid():N}");
        var previous = Path.Combine(EventsRoot, $".{modeId}.previous-{Guid.NewGuid():N}");
        var backupPath = "none (new event)";
        var hadExisting = Directory.Exists(target);

        try
        {
            if (hadExisting)
            {
                backupPath = CreateBackup(modeId, target, source);
            }

            Directory.CreateDirectory(staging);
            foreach (var file in RequiredFiles)
                File.WriteAllText(Path.Combine(staging, file), files[file], new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (hadExisting)
                Directory.Move(target, previous);
            Directory.Move(staging, target);

            ConfigLoader.InvalidateCache();
            var registrationError = "Game mode registry is unavailable.";
            if (BattleLuckPlugin.GameModes == null ||
                !BattleLuckPlugin.GameModes.TryRegisterConfiguredMode(modeId, out registrationError))
            {
                throw new InvalidOperationException(registrationError ?? "Game mode registry is unavailable.");
            }

            if (BattleLuckPlugin.Session is { } session &&
                !session.TryRegisterModeZones(modeId, out var zoneError))
            {
                throw new InvalidOperationException($"Zone registration failed: {zoneError}");
            }

            if (Directory.Exists(previous))
                Directory.Delete(previous, recursive: true);

            var flow = JsonSerializer.Deserialize<UnifiedEventDefinition>(files["flow.json"], ConfigLoader.JsonOptions)!;
            var zoneHash = flow.Zones.FirstOrDefault()?.Hash ?? 0;
            return OperationResult<EventDeploymentResult>.Ok(new EventDeploymentResult(
                modeId, source, backupPath, flow.Zones.Count, zoneHash));
        }
        catch (Exception ex)
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
                if (Directory.Exists(previous))
                    Directory.Move(previous, target);

                ConfigLoader.InvalidateCache();
                if (hadExisting)
                    BattleLuckPlugin.GameModes?.TryRegisterConfiguredMode(modeId, out _);
                else
                    BattleLuckPlugin.GameModes?.Unregister(modeId);
            }
            catch (Exception restoreEx)
            {
                BattleLuckPlugin.LogError($"[EventDeployment] Restore after failed install also failed: {restoreEx.Message}");
            }

            return OperationResult<EventDeploymentResult>.Fail($"Deployment was rolled back: {ex.Message}");
        }
    }

    EventValidationResult ValidateBundle(
        string modeId,
        IReadOnlyDictionary<string, string> files,
        string? excludeModeId)
    {
        var result = new EventValidationResult();
        var schema = EventSchemaValidator.Validate(files);
        result.Errors.AddRange(schema.Errors);

        UnifiedEventDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<UnifiedEventDefinition>(files["flow.json"], ConfigLoader.JsonOptions);
            if (definition == null)
            {
                result.Errors.Add("ESCHEMA: flow.json is empty.");
                return result;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"EJSONPARSE: flow.json is invalid JSON: {ex.Message}");
            return result;
        }

        if (string.IsNullOrWhiteSpace(definition.Metadata.Id))
            definition.Metadata.Id = modeId;
        else if (!definition.Metadata.Id.Equals(modeId, StringComparison.OrdinalIgnoreCase))
            result.Errors.Add($"flow.json metadata.id '{definition.Metadata.Id}' does not match event '{modeId}'.");

        _definitions.Validate(definition, result);
        // Prompt policy is checked against the live folder by EventDefinitionLoader;
        // validate the downloaded prompt independently before it is installed.
        result.Errors.RemoveAll(error => error.Contains("prompt.txt references", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("blocked by", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("not allowed by", StringComparison.OrdinalIgnoreCase));

        ValidatePrompt(files["prompt.txt"], modeId, definition, result);
        ValidateZones(modeId, files["zones.json"], definition, result);
        ValidateKit(modeId, files["kits.json"], result);
        ValidateZoneHashCollisions(modeId, definition, excludeModeId, result);
        return result;
    }

    static bool NormalizeFlowMetadata(string modeId, IDictionary<string, string> files, out string error)
    {
        error = "";
        try
        {
            using (var document = JsonDocument.Parse(files["flow.json"]))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("metadata", out var metadata) ||
                    metadata.ValueKind != JsonValueKind.Object)
                {
                    error = "flow.json requires a metadata object.";
                    return false;
                }
            }

            var definition = JsonSerializer.Deserialize<UnifiedEventDefinition>(files["flow.json"], ConfigLoader.JsonOptions);
            if (definition == null)
            {
                error = "flow.json is empty.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(definition.Metadata.Id) &&
                !definition.Metadata.Id.Equals(modeId, StringComparison.OrdinalIgnoreCase))
            {
                error = $"flow.json metadata.id '{definition.Metadata.Id}' does not match event '{modeId}'.";
                return false;
            }

            definition.Metadata.Id = modeId;
            if (string.IsNullOrWhiteSpace(definition.Metadata.DisplayName))
                definition.Metadata.DisplayName = ToDisplayName(modeId);
            files["flow.json"] = JsonSerializer.Serialize(definition, ConfigLoader.JsonOptions);
            return true;
        }
        catch (Exception ex)
        {
            error = $"flow.json metadata could not be normalized: {ex.Message}";
            return false;
        }
    }

    void ValidatePrompt(string text, string modeId, UnifiedEventDefinition definition, EventValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            result.Errors.Add("ESCHEMA: prompt.txt is empty.");
            return;
        }

        var prompt = new PromptContextLoader().Parse(text);
        if (!string.IsNullOrWhiteSpace(prompt.EventId) &&
            !prompt.EventId.Equals(modeId, StringComparison.OrdinalIgnoreCase))
            result.Errors.Add($"prompt.txt eventId '{prompt.EventId}' does not match event '{modeId}'.");

        var entries = _actions.Entries;
        foreach (var actionName in prompt.AllowedActions.Concat(prompt.BlockedActions).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!entries.ContainsKey(actionName) && !LiveSystemRegistryService.TryGet(actionName, out _))
                result.Errors.Add($"prompt.txt references unknown action '{actionName}'.");
        }

        var blocked = prompt.BlockedActions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowed = prompt.AllowedActions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var action in EnumerateActions(definition))
        {
            var actionName = action.ToActionString().Split(':', 2)[0].Trim();
            if (blocked.Contains(actionName))
                result.Errors.Add($"Event action '{actionName}' is blocked by prompt.txt.");
            else if (allowed.Count > 0 && !allowed.Contains(actionName))
                result.Errors.Add($"Event action '{actionName}' is not allowed by prompt.txt.");
        }
    }

    static void ValidateZones(string modeId, string zonesText, UnifiedEventDefinition definition, EventValidationResult result)
    {
        try
        {
            var zones = JsonSerializer.Deserialize<ZonesConfig>(zonesText, ConfigLoader.JsonOptions);
            if (zones == null || zones.Zones.Count == 0)
            {
                result.Errors.Add("zones.json must contain at least one zone.");
                return;
            }

            var flowHashes = definition.Zones.Select(zone => zone.Hash).ToHashSet();
            var legacyHashes = zones.Zones.Select(zone => zone.Hash).ToHashSet();
            if (!flowHashes.SetEquals(legacyHashes))
                result.Errors.Add($"flow.json and zones.json zone hashes do not match for '{modeId}'.");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"EJSONPARSE: zones.json is invalid: {ex.Message}");
        }
    }

    static void ValidateKit(string modeId, string kitsText, EventValidationResult result)
    {
        try
        {
            var kit = JsonSerializer.Deserialize<KitConfig>(kitsText, ConfigLoader.JsonOptions);
            if (kit == null)
            {
                result.Errors.Add("ESCHEMA: kits.json is empty.");
                return;
            }

            foreach (var issue in KitValidator.Validate(modeId, new ModeConfig { ModeId = modeId, KitConfig = kit }))
                result.Errors.Add(issue);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"EJSONPARSE: kits.json is invalid: {ex.Message}");
        }
    }

    void ValidateZoneHashCollisions(
        string modeId,
        UnifiedEventDefinition definition,
        string? excludeModeId,
        EventValidationResult result)
    {
        var used = new HashSet<int>();
        foreach (var path in EnumerateFlowPaths(excludeModeId))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<UnifiedEventDefinition>(File.ReadAllText(path), ConfigLoader.JsonOptions);
                foreach (var zone in existing?.Zones ?? new List<EventZoneDefinition>())
                    used.Add(zone.Hash);
            }
            catch
            {
                // The existing event validator will report malformed files separately.
            }
        }

        var local = new HashSet<int>();
        foreach (var zone in definition.Zones)
        {
            if (zone.Hash == 0)
                continue;
            if (!local.Add(zone.Hash))
                result.Errors.Add($"Duplicate zone hash {zone.Hash} in '{modeId}'.");
            else if (used.Contains(zone.Hash))
                result.Errors.Add($"EZONEHASH: Zone hash {zone.Hash} is already used by another event.");
        }
    }

    EventDeploymentStatus BuildStatus(string modeId)
    {
        var directory = EventDirectory(modeId);
        var latestBackup = FindLatestBackup(modeId);
        var status = new EventDeploymentStatus
        {
            ModeId = modeId,
            HasDirectory = Directory.Exists(directory),
            Registered = BattleLuckPlugin.GameModes?.IsRegistered(modeId) == true,
            LatestBackup = latestBackup is { } backup ? Path.GetFileName(backup) : "none",
            LatestBackupManifest = latestBackup == null
                ? "none"
                : VerifyManifest(latestBackup, modeId, out _) ? "valid" : "invalid"
        };

        if (!status.HasDirectory)
        {
            status.Errors.Add("Event directory does not exist.");
            return status;
        }

        status.HasAllFiles = RequiredFiles.All(file => File.Exists(Path.Combine(directory, file)));
        if (!status.HasAllFiles)
        {
            status.Errors.Add("One or more required event files are missing.");
            return status;
        }

        var files = RequiredFiles.ToDictionary(file => file, file => File.ReadAllText(Path.Combine(directory, file)), StringComparer.OrdinalIgnoreCase);
        var validation = ValidateBundle(modeId, files, excludeModeId: modeId);
        status.FlowValid = validation.Success;
        status.Errors.AddRange(validation.Errors.Take(8));
        try
        {
            var flow = JsonSerializer.Deserialize<UnifiedEventDefinition>(files["flow.json"], ConfigLoader.JsonOptions);
            status.ZoneHashes = flow?.Zones.Select(zone => zone.Hash).Where(hash => hash != 0).Distinct().OrderBy(hash => hash).ToList() ?? new List<int>();
        }
        catch
        {
            // Validation already contains the parse error.
        }

        return status;
    }

    string CreateBackup(string modeId, string sourceDirectory, string source)
    {
        var root = Path.Combine(BackupsRoot, modeId);
        var destination = Path.Combine(root, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        CopyDirectory(sourceDirectory, destination);
        File.WriteAllText(Path.Combine(destination, "deployment.json"), JsonSerializer.Serialize(new
        {
            modeId,
            source,
            createdUtc = DateTime.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true }));
        WriteManifest(destination, modeId, source);
        return destination;
    }

    static void WriteManifest(string directory, string modeId, string source)
    {
        var files = new Dictionary<string, EventFileManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in RequiredFiles)
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path))
                continue;
            using var stream = File.OpenRead(path);
            files[file] = new EventFileManifest
            {
                Bytes = stream.Length,
                Sha256 = Convert.ToHexString(SHA256.Create().ComputeHash(stream)).ToLowerInvariant()
            };
        }

        var manifest = new EventDeploymentManifest
        {
            SchemaVersion = 1,
            ModeId = modeId,
            Source = source,
            CreatedUtc = DateTime.UtcNow,
            Files = files
        };
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    static bool VerifyManifest(string directory, string modeId, out string error)
    {
        error = "";
        var path = Path.Combine(directory, "manifest.json");
        if (!File.Exists(path))
        {
            error = "Backup has no manifest.json. Create a fresh deployment backup before retrying rollback.";
            return false;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<EventDeploymentManifest>(File.ReadAllText(path));
            if (manifest == null || !manifest.ModeId.Equals(modeId, StringComparison.OrdinalIgnoreCase))
            {
                error = "E_BACKUP_TAMPERED: Backup manifest event id does not match the requested event.";
                return false;
            }

            foreach (var file in RequiredFiles)
            {
                if (!manifest.Files.TryGetValue(file, out var expected))
                {
                    error = $"E_BACKUP_TAMPERED: Backup manifest is missing {file}.";
                    return false;
                }

                var filePath = Path.Combine(directory, file);
                if (!File.Exists(filePath))
                {
                    error = $"E_BACKUP_TAMPERED: Backup file {file} is missing.";
                    return false;
                }

                using var stream = File.OpenRead(filePath);
                var actualHash = Convert.ToHexString(SHA256.Create().ComputeHash(stream)).ToLowerInvariant();
                if (!actualHash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase) || stream.Length != expected.Bytes)
                {
                    error = $"E_BACKUP_TAMPERED: Backup file {file} failed SHA-256/size verification.";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Backup manifest could not be verified: {ex.Message}";
            return false;
        }
    }

    string? FindLatestBackup(string modeId)
    {
        var root = Path.Combine(BackupsRoot, modeId);
        if (!Directory.Exists(root))
            return null;
        return Directory.EnumerateDirectories(root)
            .Where(path => File.Exists(Path.Combine(path, "flow.json")))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    IEnumerable<string> EnumerateFlowPaths(string? excludeModeId)
    {
        if (!Directory.Exists(EventsRoot))
            yield break;

        foreach (var directory in Directory.EnumerateDirectories(EventsRoot))
        {
            var id = Path.GetFileName(directory);
            if (id.StartsWith('.') || (!string.IsNullOrWhiteSpace(excludeModeId) && id.Equals(excludeModeId, StringComparison.OrdinalIgnoreCase)))
                continue;
            var flow = Path.Combine(directory, "flow.json");
            if (File.Exists(flow))
                yield return flow;
        }
    }

    static IEnumerable<EventActionDefinition> EnumerateActions(UnifiedEventDefinition definition)
    {
        foreach (var action in definition.Actions) yield return action;
        foreach (var item in definition.Objects) foreach (var action in item.Actions) yield return action;
        foreach (var item in definition.Glows) foreach (var action in item.Actions) yield return action;
        foreach (var phase in definition.Phases) foreach (var action in phase.Actions) yield return action;
        foreach (var timer in definition.Timers) foreach (var action in timer.OnCompleteActions) yield return action;
        foreach (var trigger in definition.Triggers) foreach (var action in trigger.Actions) yield return action;
    }

    static async Task<string> DownloadTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxFileBytes)
                throw new InvalidDataException($"{uri} exceeds the {MaxFileBytes / 1024} KiB file limit.");
            memory.Write(buffer, 0, read);
        }

        var text = Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF');
        if (text.Contains("<html", StringComparison.OrdinalIgnoreCase) || text.Contains("<!doctype", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{uri} returned HTML instead of an event file.");
        return text;
    }

    static bool TryBuildGistFileUrls(string rawUrl, out Dictionary<string, Uri> fileUrls, out string error)
    {
        fileUrls = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        error = "";
        if (!Uri.TryCreate(rawUrl?.Trim(), UriKind.Absolute, out var gist) || gist.Scheme != Uri.UriSchemeHttps)
        {
            error = "Use an HTTPS GitHub Gist URL.";
            return false;
        }

        if (!gist.Host.Equals("gist.github.com", StringComparison.OrdinalIgnoreCase) &&
            !gist.Host.Equals("gist.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only gist.github.com or gist.githubusercontent.com URLs are accepted.";
            return false;
        }

        var path = gist.AbsolutePath.Trim('/');
        var rawMarker = path.IndexOf("/raw", StringComparison.OrdinalIgnoreCase);
        if (rawMarker >= 0)
            path = path[..rawMarker].Trim('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments.Any(segment => segment is "." or ".." || segment.Any(char.IsWhiteSpace)))
        {
            error = "The Gist URL must include an owner and Gist id.";
            return false;
        }

        var basePath = string.Join('/', segments);
        foreach (var file in RequiredFiles)
            fileUrls[file] = new Uri($"https://gist.githubusercontent.com/{basePath}/raw/{file}");
        return true;
    }

    static string NormalizeId(string? value)
    {
        var trimmed = (value ?? "").Trim().ToLowerInvariant();
        return ValidId.IsMatch(trimmed) ? trimmed : "";
    }

    static string ToDisplayName(string modeId)
    {
        return string.Join(" ", modeId.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    static bool IsModeActive(string modeId)
    {
        return BattleLuckPlugin.Session?.ActiveSessions.Values.Any(session =>
            session.Context?.ModeId.Equals(modeId, StringComparison.OrdinalIgnoreCase) == true) == true;
    }

    static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BattleLuck-EventOrchestrator/1.0");
        return client;
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    static string EventsRoot => Path.Combine(ConfigLoader.ConfigRoot, "events");
    static string BackupsRoot => Path.Combine(ConfigLoader.ConfigRoot, "backups");
    static string EventDirectory(string modeId) => Path.Combine(EventsRoot, modeId);
}

public sealed class EventDeploymentResult
{
    public EventDeploymentResult(string modeId, string source, string backupPath, int zoneCount, int zoneHash)
    {
        ModeId = modeId;
        Source = source;
        BackupPath = backupPath;
        ZoneCount = zoneCount;
        ZoneHash = zoneHash;
    }

    public string ModeId { get; }
    public string Source { get; }
    public string BackupPath { get; }
    public int ZoneCount { get; }
    public int ZoneHash { get; }
}

public sealed class EventDeploymentStatus
{
    public string ModeId { get; set; } = "";
    public bool HasDirectory { get; set; }
    public bool HasAllFiles { get; set; }
    public bool FlowValid { get; set; }
    public bool Registered { get; set; }
    public int EventCount { get; set; }
    public List<int> ZoneHashes { get; set; } = new();
    public string LatestBackup { get; set; } = "none";
    public string LatestBackupManifest { get; set; } = "none";
    public List<string> Errors { get; set; } = new();
}

public sealed class EventDeploymentManifest
{
    public int SchemaVersion { get; set; }
    public string ModeId { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public Dictionary<string, EventFileManifest> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EventFileManifest
{
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = "";
}
