namespace BattleLuck.Models;

/// <summary>
/// Current BloodType prefab ids mirrored from KindredCommands.Models.BloodType.
/// Keep this enum as the single source of truth for blood.change / SetBloodTypeCommand.
/// </summary>
public enum KindredBloodType
{
    Frailed = 447918373,
    Creature = 524822543,
    Warrior = -516976528,
    Rogue = -1620185637,
    Brute = 804798592,
    Scholar = 1476452791,
    Worker = -1776904174,
    Mutant = 1821108694,
    Dracula = 2010023718,
    Immortal = 2010023718,
    Draculin = 1328126535,
    BloodSoul = 910644396,
    VBlood = -338774148,
    Corrupted = -1382693416,
}

public static class KindredBloodTypes
{
    static readonly Dictionary<string, KindredBloodType> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["frail"] = KindredBloodType.Frailed,
        ["frailed"] = KindredBloodType.Frailed,
        ["creature"] = KindredBloodType.Creature,
        ["warrior"] = KindredBloodType.Warrior,
        ["rogue"] = KindredBloodType.Rogue,
        ["brute"] = KindredBloodType.Brute,
        ["scholar"] = KindredBloodType.Scholar,
        ["worker"] = KindredBloodType.Worker,
        ["mutant"] = KindredBloodType.Mutant,
        ["dracula"] = KindredBloodType.Dracula,
        ["immortal"] = KindredBloodType.Immortal,
        ["draculin"] = KindredBloodType.Draculin,
        ["bloodsoul"] = KindredBloodType.BloodSoul,
        ["blood_soul"] = KindredBloodType.BloodSoul,
        ["blood-soul"] = KindredBloodType.BloodSoul,
        ["vblood"] = KindredBloodType.VBlood,
        ["v_blood"] = KindredBloodType.VBlood,
        ["v-blood"] = KindredBloodType.VBlood,
        ["corrupted"] = KindredBloodType.Corrupted,
    };

    public static IReadOnlyDictionary<string, KindredBloodType> Known => Aliases;

    public static bool TryResolve(string? value, out PrefabGUID prefab)
    {
        prefab = PrefabGUID.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (int.TryParse(normalized, out var guidHash))
        {
            prefab = new PrefabGUID(guidHash);
            return true;
        }

        if (Aliases.TryGetValue(normalized, out var bloodType) ||
            Enum.TryParse(normalized, ignoreCase: true, out bloodType))
        {
            prefab = ToPrefabGuid(bloodType);
            return true;
        }

        return false;
    }

    public static PrefabGUID ResolveOrDefault(string? value, KindredBloodType fallback = KindredBloodType.Scholar) =>
        TryResolve(value, out var prefab) ? prefab : ToPrefabGuid(fallback);

    public static PrefabGUID ToPrefabGuid(KindredBloodType bloodType) =>
        new((int)bloodType);

    public static string KnownNames =>
        string.Join(", ", Enum.GetNames<KindredBloodType>().Distinct(StringComparer.OrdinalIgnoreCase));
}

public struct KindredPlayerData
{
    public FixedString64Bytes CharacterName { get; set; }
    public ulong SteamID { get; set; }
    public bool IsOnline { get; set; }
    public Entity UserEntity { get; set; }
    public Entity CharEntity { get; set; }

    public KindredPlayerData(
        FixedString64Bytes characterName = default,
        ulong steamID = 0,
        bool isOnline = false,
        Entity userEntity = default,
        Entity charEntity = default)
    {
        CharacterName = characterName;
        SteamID = steamID;
        IsOnline = isOnline;
        UserEntity = userEntity;
        CharEntity = charEntity;
    }
}

public sealed class KindredPlayer
{
    public string Name { get; set; } = string.Empty;
    public ulong SteamID { get; set; }
    public bool IsOnline { get; set; }
    public bool IsAdmin { get; set; }
    public Entity User { get; set; }
    public Entity Character { get; set; }

    public KindredPlayer(Entity userEntity = default)
    {
        User = userEntity;
        if (!userEntity.Exists() || !userEntity.Has<User>())
            return;

        var user = userEntity.Read<User>();
        Character = user.LocalCharacter._Entity;
        Name = user.CharacterName.ToString();
        IsOnline = user.IsConnected;
        IsAdmin = user.IsAdmin;
        SteamID = user.PlatformId;
    }

    public KindredPlayerData ToData() => new(
        new FixedString64Bytes(Name),
        SteamID,
        IsOnline,
        User,
        Character);
}

public enum ProgressionUnlockKind
{
    VBlood,
    Research,
    Ability,
    Region,
}

public enum ProgressionUnlockScope
{
    Session,
    Permanent,
}

public sealed class ProgressionUnlockDefinition
{
    public ProgressionUnlockKind Kind { get; set; } = ProgressionUnlockKind.VBlood;
    public ProgressionUnlockScope Scope { get; set; } = ProgressionUnlockScope.Session;
    public bool RestoreOnCleanup { get; set; } = true;
    public bool IncludeBossLocks { get; set; }
    public List<int> PrefabGuids { get; set; } = new();
}

public static class KindredSpawnSafety
{
    static readonly Dictionary<string, string> DefaultNoSpawn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CHAR_VampireMale"] = "Player character prefab; spawning it as an NPC can create invalid users.",
        ["CHAR_Mount_Horse_Gloomrot"] = "Known unsafe horse prefab in KindredCommands no-spawn defaults.",
        ["CHAR_Mount_Horse_Vampire"] = "Known unsafe horse prefab in KindredCommands no-spawn defaults.",
        ["CHAR_Vampire_Ghost"] = "Known unsafe ghost prefab in KindredCommands no-spawn defaults.",
    };

    public static IReadOnlyDictionary<string, string> NoSpawnPrefabs => DefaultNoSpawn;

    public static bool IsSpawnBanned(string? prefabName, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(prefabName))
            return false;

        if (!DefaultNoSpawn.TryGetValue(prefabName.Trim(), out var match))
            return false;

        reason = match;
        return true;
    }
}
