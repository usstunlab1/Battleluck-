/// <summary>
/// Central prefab registry — single source of truth for ALL PrefabGUID constants.
/// Referenced by kit system, glow system, boss spawns, border walls, and special items.
/// </summary>
public static class Prefabs
{
    // ── VBlood Units ─────────────────────────────────────────────────────
    public static readonly PrefabGUID CHAR_Vampire_HighLord_VBlood = new(844833532);
    public static readonly PrefabGUID CHAR_Manticore_VBlood = new(1689402333);
    public static readonly PrefabGUID CHAR_Blackfang_Morgana_VBlood = new(562098749);
    public static readonly PrefabGUID CHAR_Undead_BishopOfShadows_VBlood = new(1739255381);
    public static readonly PrefabGUID CHAR_Winter_Yeti_VBlood = new(1404813455);
    public static readonly PrefabGUID CHAR_Forest_Wolf_VBlood = new(1446249180);
    public static readonly PrefabGUID CHAR_Forest_Bear_VBlood = new(1446249179);
    public static readonly PrefabGUID CHAR_Forest_Gloomrot_VBlood = new(1446249181);
    public static readonly PrefabGUID CHAR_Cursed_Forest_VBlood = new(1446249182);

    // ── Research Items ────────────────────────────────────────────────────
    // ⚠ PLACEHOLDER GUIDs — must be resolved at runtime via ResolveLiveResearchGuids().
    //   These negative placeholder values (-123456789 family) collide and are NOT valid
    //   V Rising prefab GUIDs. Resolve from live data on plugin init.
    public static PrefabGUID Item_Research_Scroll_Tier1 = new(-123456789);
    public static PrefabGUID Item_Research_Scroll_Tier2 = new(-123456788);
    public static PrefabGUID Item_Research_Scroll_Tier3 = new(-123456787);

    public static List<PrefabGUID> GetAllVBloodPrefabs()
    {
        return new List<PrefabGUID>
        {
            CHAR_Vampire_HighLord_VBlood,
            CHAR_Manticore_VBlood,
            CHAR_Blackfang_Morgana_VBlood,
            CHAR_Undead_BishopOfShadows_VBlood,
            CHAR_Winter_Yeti_VBlood,
            CHAR_Forest_Wolf_VBlood,
            CHAR_Forest_Bear_VBlood,
            CHAR_Forest_Gloomrot_VBlood,
            CHAR_Cursed_Forest_VBlood
        };
    }

    public static List<PrefabGUID> GetAllResearchPrefabs()
    {
        return new List<PrefabGUID>
        {
            Item_Research_Scroll_Tier1,
            Item_Research_Scroll_Tier2,
            Item_Research_Scroll_Tier3
        };
    }
    // ── Weapons T09 (Legacy — highest tier) ─────────────────────────────
    public static readonly PrefabGUID Item_Weapon_Sword_T09 = new(-1399352573);
    public static readonly PrefabGUID Item_Weapon_Axe_T09 = new(-1279634570);
    public static readonly PrefabGUID Item_Weapon_Mace_T09 = new(-593473395);
    public static readonly PrefabGUID Item_Weapon_Spear_T09 = new(-1458072513);
    public static readonly PrefabGUID Item_Weapon_Crossbow_T09 = new(-1510225645);
    public static readonly PrefabGUID Item_Weapon_Slashers_T09 = new(-458462834);
    public static readonly PrefabGUID Item_Weapon_Reaper_T09 = new(-1619308567);
    public static readonly PrefabGUID Item_Weapon_Pistols_T09 = new(1278479602);
    public static readonly PrefabGUID Item_Weapon_GreatSword_T09 = new(-79549804);
    public static readonly PrefabGUID Item_Weapon_Whip_T09 = new(1491735583);

    // ── Weapons T08 (Blood Moon tier) ───────────────────────────────────
    public static readonly PrefabGUID Item_Weapon_Sword_T08 = new(-692773302);
    public static readonly PrefabGUID Item_Weapon_Axe_T08 = new(-1920504628);
    public static readonly PrefabGUID Item_Weapon_Mace_T08 = new(-1506352490);
    public static readonly PrefabGUID Item_Weapon_Spear_T08 = new(1366323705);
    public static readonly PrefabGUID Item_Weapon_Crossbow_T08 = new(-1026602400);
    public static readonly PrefabGUID Item_Weapon_Slashers_T08 = new(1395638191);
    public static readonly PrefabGUID Item_Weapon_Reaper_T08 = new(1281815020);
    public static readonly PrefabGUID Item_Weapon_Pistols_T08 = new(659655050);
    public static readonly PrefabGUID Item_Weapon_GreatSword_T08 = new(2025505971);
    public static readonly PrefabGUID Item_Weapon_Whip_T08 = new(-1011921684);

    // ── Weapons T07 (Spectral tier) ─────────────────────────────────────
    public static readonly PrefabGUID Item_Weapon_Sword_T07 = new(-578787205);
    public static readonly PrefabGUID Item_Weapon_Axe_T07 = new(436289921);
    public static readonly PrefabGUID Item_Weapon_Mace_T07 = new(1905296613);
    public static readonly PrefabGUID Item_Weapon_Spear_T07 = new(1626728247);
    public static readonly PrefabGUID Item_Weapon_Crossbow_T07 = new(-656154425);
    public static readonly PrefabGUID Item_Weapon_Slashers_T07 = new(-1576122455);
    public static readonly PrefabGUID Item_Weapon_Reaper_T07 = new(-1013413399);
    public static readonly PrefabGUID Item_Weapon_Pistols_T07 = new(1961535743);
    public static readonly PrefabGUID Item_Weapon_GreatSword_T07 = new(-327456458);
    public static readonly PrefabGUID Item_Weapon_Whip_T07 = new(1600506580);

    // ── Weapons T06 (Sanguine tier) ─────────────────────────────────────
    public static readonly PrefabGUID Item_Weapon_Sword_T06 = new(1776460812);
    public static readonly PrefabGUID Item_Weapon_Axe_T06 = new(-2020710994);
    public static readonly PrefabGUID Item_Weapon_Mace_T06 = new(578591516);
    public static readonly PrefabGUID Item_Weapon_Spear_T06 = new(-2082016373);
    public static readonly PrefabGUID Item_Weapon_Crossbow_T06 = new(-2047454505);
    public static readonly PrefabGUID Item_Weapon_Slashers_T06 = new(1069346713);
    public static readonly PrefabGUID Item_Weapon_Reaper_T06 = new(-2069359776);
    public static readonly PrefabGUID Item_Weapon_Pistols_T06 = new(2053501181);
    public static readonly PrefabGUID Item_Weapon_GreatSword_T06 = new(-1459507701);
    public static readonly PrefabGUID Item_Weapon_Whip_T06 = new(-1163007439);

    // ── Weapons T05 (Dark Silver tier) ──────────────────────────────────
    public static readonly PrefabGUID Item_Weapon_Sword_T05 = new(-1461482750);
    public static readonly PrefabGUID Item_Weapon_Axe_T05 = new(1173770393);
    public static readonly PrefabGUID Item_Weapon_Mace_T05 = new(-1750802913);
    public static readonly PrefabGUID Item_Weapon_Spear_T05 = new(2137749375);
    public static readonly PrefabGUID Item_Weapon_Crossbow_T05 = new(-1484131701);
    public static readonly PrefabGUID Item_Weapon_Slashers_T05 = new(-532315195);
    public static readonly PrefabGUID Item_Weapon_Reaper_T05 = new(-1569515503);
    public static readonly PrefabGUID Item_Weapon_Pistols_T05 = new(-1789588513);
    public static readonly PrefabGUID Item_Weapon_GreatSword_T05 = new(-490847893);
    public static readonly PrefabGUID Item_Weapon_Whip_T05 = new(-1607263773);

    // ── Weapons T04 (Gold tier) ──────────────────────────────────────────
    public static readonly PrefabGUID Item_Weapon_Sword_T04 = new(-1595549885);
    public static readonly PrefabGUID Item_Weapon_Axe_T04 = new(-1297865846);
    public static readonly PrefabGUID Item_Weapon_Mace_T04 = new(-1119858849);
    public static readonly PrefabGUID Item_Weapon_Spear_T04 = new(-1242295565);
    public static readonly PrefabGUID Item_Weapon_Crossbow_T04 = new(-1576469978);
    public static readonly PrefabGUID Item_Weapon_Slashers_T04 = new(2069876700);
    public static readonly PrefabGUID Item_Weapon_Reaper_T04 = new(1861427556);
    public static readonly PrefabGUID Item_Weapon_Pistols_T04 = new(2007145845);
    public static readonly PrefabGUID Item_Weapon_GreatSword_T04 = new(1413735506);
    public static readonly PrefabGUID Item_Weapon_Whip_T04 = new(-1975622229);

    // ── Weapons T03 (Silver tier) ───────────────────────────────────────
    public static readonly PrefabGUID Item_Weapon_Sword_T03 = new(-621619781);
    public static readonly PrefabGUID Item_Weapon_Axe_T03 = new(1391139533);
    public static readonly PrefabGUID Item_Weapon_Mace_T03 = new(-1897914618);
    public static readonly PrefabGUID Item_Weapon_Spear_T03 = new(-2123942374);
    public static readonly PrefabGUID Item_Weapon_Crossbow_T03 = new(-1738226765);
    public static readonly PrefabGUID Item_Weapon_Slashers_T03 = new(-578264528);
    public static readonly PrefabGUID Item_Weapon_Reaper_T03 = new(1397865394);
    public static readonly PrefabGUID Item_Weapon_Pistols_T03 = new(-1882903486);
    public static readonly PrefabGUID Item_Weapon_GreatSword_T03 = new(1484971364);
    public static readonly PrefabGUID Item_Weapon_Whip_T03 = new(327170571);

    // ── Weapons T02 (Iron tier) ─────────────────────────────────────────
    public static readonly PrefabGUID Item_Weapon_Sword_T02 = new(-1466445694);
    public static readonly PrefabGUID Item_Weapon_Axe_T02 = new(-686141501);
    public static readonly PrefabGUID Item_Weapon_Mace_T02 = new(-631502508);
    public static readonly PrefabGUID Item_Weapon_Spear_T02 = new(426146588);
    public static readonly PrefabGUID Item_Weapon_Crossbow_T02 = new(1819151130);
    public static readonly PrefabGUID Item_Weapon_Slashers_T02 = new(1399928802);
    public static readonly PrefabGUID Item_Weapon_Reaper_T02 = new(-1741934774);
    public static readonly PrefabGUID Item_Weapon_Pistols_T02 = new(1580587505);
    public static readonly PrefabGUID Item_Weapon_GreatSword_T02 = new(-2097553490);
    public static readonly PrefabGUID Item_Weapon_Whip_T02 = new(1218356411);

    // ── Weapons T01 (Copper tier) ───────────────────────────────────────
    public static readonly PrefabGUID Item_Weapon_Sword_T01 = new(-1466545709);
    public static readonly PrefabGUID Item_Weapon_Axe_T01 = new(1523920789);
    public static readonly PrefabGUID Item_Weapon_Mace_T01 = new(-2107720338);
    public static readonly PrefabGUID Item_Weapon_Spear_T01 = new(-1750220600);
    public static readonly PrefabGUID Item_Weapon_Crossbow_T01 = new(-977624399);
    public static readonly PrefabGUID Item_Weapon_Slashers_T01 = new(-219768798);
    public static readonly PrefabGUID Item_Weapon_Reaper_T01 = new(-1194017549);
    public static readonly PrefabGUID Item_Weapon_Pistols_T01 = new(1488921922);
    public static readonly PrefabGUID Item_Weapon_GreatSword_T01 = new(-1401761580);
    public static readonly PrefabGUID Item_Weapon_Whip_T01 = new(-580644792);

    // ── Armor T09 ───────────────────────────────────────────────────────
    public static readonly PrefabGUID Item_Armor_Chest_T09 = new(-613439220);
    public static readonly PrefabGUID Item_Armor_Legs_T09 = new(1309832058);
    public static readonly PrefabGUID Item_Armor_Gloves_T09 = new(-1145775028);
    public static readonly PrefabGUID Item_Armor_Boots_T09 = new(-604754835);
    public static readonly PrefabGUID Item_Cloak_T09 = new(1491316284);
    public static readonly PrefabGUID Item_Headgear_T09 = new(-1497408680);

    // ── Armor T08 ───────────────────────────────────────────────────────
    public static readonly PrefabGUID Item_Armor_Chest_T08 = new(1549745532);
    public static readonly PrefabGUID Item_Armor_Legs_T08 = new(342028782);
    public static readonly PrefabGUID Item_Armor_Gloves_T08 = new(-1505055637);
    public static readonly PrefabGUID Item_Armor_Boots_T08 = new(-1296938426);
    public static readonly PrefabGUID Item_Cloak_T08 = new(-613905787);
    public static readonly PrefabGUID Item_Headgear_T08 = new(-1791506920);

    // ── Armor T06 (Sanguine) ─────────────────────────────────────────────
    public static readonly PrefabGUID Item_Armor_Chest_T06 = new(-1944296210);
    public static readonly PrefabGUID Item_Armor_Legs_T06 = new(-1524177100);
    public static readonly PrefabGUID Item_Armor_Gloves_T06 = new(1688388115);
    public static readonly PrefabGUID Item_Armor_Boots_T06 = new(1124710003);
    public static readonly PrefabGUID Item_Cloak_T06 = new(-1064563948);
    public static readonly PrefabGUID Item_Headgear_T06 = new(-388647040);

    // ── Armor T04 (Gold) ────────────────────────────────────────────────
    public static readonly PrefabGUID Item_Armor_Chest_T04 = new(1765206023);
    public static readonly PrefabGUID Item_Armor_Legs_T04 = new(-1391589556);
    public static readonly PrefabGUID Item_Armor_Gloves_T04 = new(736970560);
    public static readonly PrefabGUID Item_Armor_Boots_T04 = new(-404060166);
    public static readonly PrefabGUID Item_Cloak_T04 = new(-1890356173);
    public static readonly PrefabGUID Item_Headgear_T04 = new(-131561130);

    // ── Armor T02 (Iron) ────────────────────────────────────────────────
    public static readonly PrefabGUID Item_Armor_Chest_T02 = new(-2076983759);
    public static readonly PrefabGUID Item_Armor_Legs_T02 = new(-2102876022);
    public static readonly PrefabGUID Item_Armor_Gloves_T02 = new(-204890966);
    public static readonly PrefabGUID Item_Armor_Boots_T02 = new(211882414);
    public static readonly PrefabGUID Item_Cloak_T02 = new(1150136902);
    public static readonly PrefabGUID Item_Headgear_T02 = new(-1908773690);

    // ── Magic Sources T09 ───────────────────────────────────────────────
    public static readonly PrefabGUID Item_MagicSource_Blood_T09 = new(1643462775);
    public static readonly PrefabGUID Item_MagicSource_Unholy_T09 = new(-1680962337);
    public static readonly PrefabGUID Item_MagicSource_Illusion_T09 = new(-1174698422);
    public static readonly PrefabGUID Item_MagicSource_Chaos_T09 = new(-1023232856);
    public static readonly PrefabGUID Item_MagicSource_Frost_T09 = new(1653430798);
    public static readonly PrefabGUID Item_MagicSource_Storm_T09 = new(-1229498740);

    // ── Magic Sources T08 ───────────────────────────────────────────────
    public static readonly PrefabGUID Item_MagicSource_Blood_T08 = new(-1154472616);
    public static readonly PrefabGUID Item_MagicSource_Unholy_T08 = new(-1572706477);
    public static readonly PrefabGUID Item_MagicSource_Illusion_T08 = new(1776519527);
    public static readonly PrefabGUID Item_MagicSource_Chaos_T08 = new(-1015630513);
    public static readonly PrefabGUID Item_MagicSource_Frost_T08 = new(1226756355);
    public static readonly PrefabGUID Item_MagicSource_Storm_T08 = new(-1437693620);

    // ── Magic Sources T06 ───────────────────────────────────────────────
    public static readonly PrefabGUID Item_MagicSource_Blood_T06 = new(1380437739);
    public static readonly PrefabGUID Item_MagicSource_Unholy_T06 = new(-1447840175);
    public static readonly PrefabGUID Item_MagicSource_Illusion_T06 = new(-1430561966);
    public static readonly PrefabGUID Item_MagicSource_Chaos_T06 = new(2080825833);
    public static readonly PrefabGUID Item_MagicSource_Frost_T06 = new(-1988861042);
    public static readonly PrefabGUID Item_MagicSource_Storm_T06 = new(1015918370);

    // ── Blood Types ─────────────────────────────────────────────────────
    public static readonly PrefabGUID BloodType_Scholar = KindredBloodTypes.ToPrefabGuid(KindredBloodType.Scholar);
    public static readonly PrefabGUID BloodType_Warrior = KindredBloodTypes.ToPrefabGuid(KindredBloodType.Warrior);
    public static readonly PrefabGUID BloodType_Rogue = KindredBloodTypes.ToPrefabGuid(KindredBloodType.Rogue);
    public static readonly PrefabGUID BloodType_Brute = KindredBloodTypes.ToPrefabGuid(KindredBloodType.Brute);
    public static readonly PrefabGUID BloodType_Worker = KindredBloodTypes.ToPrefabGuid(KindredBloodType.Worker);
    public static readonly PrefabGUID BloodType_Creature = KindredBloodTypes.ToPrefabGuid(KindredBloodType.Creature);
    public static readonly PrefabGUID BloodType_Draculin = KindredBloodTypes.ToPrefabGuid(KindredBloodType.Draculin);
    public static readonly PrefabGUID BloodType_Mutant = KindredBloodTypes.ToPrefabGuid(KindredBloodType.Mutant);
    public static readonly PrefabGUID BloodType_Frailed = KindredBloodTypes.ToPrefabGuid(KindredBloodType.Frailed);
    public static readonly PrefabGUID BloodType_VBlood = KindredBloodTypes.ToPrefabGuid(KindredBloodType.VBlood);

    // ── Blood Abilities ─────────────────────────────────────────────────
    public static readonly PrefabGUID AB_Blood_BloodFountain = new(2067760264);
    public static readonly PrefabGUID AB_Blood_BloodRage = new(651613264);
    public static readonly PrefabGUID AB_Blood_BloodRite = new(1191439206);
    public static readonly PrefabGUID AB_Blood_BloodStorm = new(-1284243288);
    public static readonly PrefabGUID AB_Blood_CarrionSwarm = new(-1380116221);
    public static readonly PrefabGUID AB_Blood_CrimsonBeam = new(375131842);
    public static readonly PrefabGUID AB_Blood_HeartStrike = new(-1432604486);
    public static readonly PrefabGUID AB_Blood_SanguineCoil = new(189403977);
    public static readonly PrefabGUID AB_Blood_Shadowbolt = new(-880131926);
    public static readonly PrefabGUID AB_Blood_VampiricCurse = new(-326374250);

    // ── Frost Abilities ─────────────────────────────────────────────────
    public static readonly PrefabGUID AB_Frost_ArcticLeap = new(1966330719);
    public static readonly PrefabGUID AB_Frost_ColdSnap = new(-1000260252);
    public static readonly PrefabGUID AB_Frost_CrystalLance = new(295045820);
    public static readonly PrefabGUID AB_Frost_FrostBat = new(78384915);
    public static readonly PrefabGUID AB_Frost_IceBlockVortex = new(1887600892);
    public static readonly PrefabGUID AB_Frost_IceNova = new(91249849);
    public static readonly PrefabGUID AB_Frost_FrostBarrier = new(1293609465);
    public static readonly PrefabGUID AB_Frost_FrostCone = new(1119012588);

    // ── Unholy Abilities ────────────────────────────────────────────────
    public static readonly PrefabGUID AB_Unholy_ArmyOfTheDead = new(-1781779733);
    public static readonly PrefabGUID AB_Unholy_ChainsOfDeath = new(-1845982676);
    public static readonly PrefabGUID AB_Unholy_CorpseExplosion = new(481411985);
    public static readonly PrefabGUID AB_Unholy_CorruptedSkull = new(-1204819086);
    public static readonly PrefabGUID AB_Unholy_DeathKnight = new(1961570821);
    public static readonly PrefabGUID AB_Unholy_Soulburn = new(2138402840);
    public static readonly PrefabGUID AB_Unholy_SummonFallenAngel = new(1297311521);
    public static readonly PrefabGUID AB_Unholy_WardOfTheDamned = new(-1136860480);

    // ── Chaos Abilities ─────────────────────────────────────────────────
    public static readonly PrefabGUID AB_Chaos_Aftershock = new(1575317901);
    public static readonly PrefabGUID AB_Chaos_Barrier = new(-1016145613);
    public static readonly PrefabGUID AB_Chaos_ChaosBarrage = new(1174831223);
    public static readonly PrefabGUID AB_Chaos_MercilessCharge = new(245173408);
    public static readonly PrefabGUID AB_Chaos_PowerSurge = new(1112116762);
    public static readonly PrefabGUID AB_Chaos_RainOfChaos = new(2012523607);
    public static readonly PrefabGUID AB_Chaos_Void = new(-358319417);
    public static readonly PrefabGUID AB_Chaos_Voidquake = new(1383453728);
    public static readonly PrefabGUID AB_Chaos_Volley = new(1019568127);

    // ── Storm Abilities ─────────────────────────────────────────────────
    public static readonly PrefabGUID AB_Storm_BallLightning = new(1249925269);
    public static readonly PrefabGUID AB_Storm_Cyclone = new(-356990326);
    public static readonly PrefabGUID AB_Storm_Discharge = new(1952703098);
    public static readonly PrefabGUID AB_Storm_EyeOfTheStorm = new(1870875931);
    public static readonly PrefabGUID AB_Storm_LightningTendrils = new(-1184139778);
    public static readonly PrefabGUID AB_Storm_LightningTyphoon = new(-914344112);
    public static readonly PrefabGUID AB_Storm_LightningWall = new(1071205195);
    public static readonly PrefabGUID AB_Storm_PolarityShift = new(-987810170);
    public static readonly PrefabGUID AB_Storm_RagingTempest = new(2111431121);

    // ── Illusion Abilities ──────────────────────────────────────────────
    public static readonly PrefabGUID AB_Illusion_Curse = new(-1432758970);
    public static readonly PrefabGUID AB_Illusion_MistTrance = new(110097606);
    public static readonly PrefabGUID AB_Illusion_Mosquito = new(268059675);
    public static readonly PrefabGUID AB_Illusion_PhantomAegis = new(-2053450457);
    public static readonly PrefabGUID AB_Illusion_Serpent = new(-286223348);
    public static readonly PrefabGUID AB_Illusion_SpectralGuardian = new(1650878435);
    public static readonly PrefabGUID AB_Illusion_SpectralWolf = new(247896794);
    public static readonly PrefabGUID AB_Illusion_WispDance = new(-1745021468);
    public static readonly PrefabGUID AB_Illusion_WraithSpear = new(-242769430);

    // ── VBlood Ability Replace ──────────────────────────────────────────
    public static readonly PrefabGUID VBloodAbilityReplace = new(1171608023);

    // ── Enemies Tier 1-4 + Elites ───────────────────────────────────────
    public static readonly PrefabGUID CHAR_Skeleton_Warrior = new(-1584807109);
    public static readonly PrefabGUID CHAR_Skeleton_Mage = new(-539289064);
    public static readonly PrefabGUID CHAR_Skeleton_Archer = new(-1340402506);
    public static readonly PrefabGUID CHAR_Ghoul = new(-1508186605);
    public static readonly PrefabGUID CHAR_Bandit_Thug = new(1458281806);
    public static readonly PrefabGUID CHAR_Bandit_Hunter = new(-1000550829);
    public static readonly PrefabGUID CHAR_Militia_Guard = new(-1101895538);
    public static readonly PrefabGUID CHAR_Militia_Devoted = new(1820387430);
    public static readonly PrefabGUID CHAR_Church_Paladin = new(-1791316508);
    public static readonly PrefabGUID CHAR_Vampire_Cultist = new(-707081968);
    public static readonly PrefabGUID CHAR_Bandit_Bomber = new(-1090756563);
    public static readonly PrefabGUID CHAR_Church_Captain = new(1090737596);
    public static readonly PrefabGUID CHAR_Bear_Dire = new(-1391546585);
    public static readonly PrefabGUID CHAR_Werewolf = new(1885959949);
    public static readonly PrefabGUID CHAR_Golem_Stone = CHAR_Church_Captain; // Legacy golem GUID is absent in current server builds.

    // ── Wildlife Enemies ────────────────────────────────────────────────
    public static readonly PrefabGUID CHAR_Wildlife_Wolf = new(587052543);
    public static readonly PrefabGUID CHAR_Wildlife_Wolf_Alpha = new(1905430134);
    public static readonly PrefabGUID CHAR_Wildlife_Bear = new(-1152594882);
    public static readonly PrefabGUID CHAR_Wildlife_Moose = new(-2145483875);
    public static readonly PrefabGUID CHAR_Wildlife_Spider_Forest = new(-1617081958);
    public static readonly PrefabGUID CHAR_Wildlife_Bat = new(1446499602);
    public static readonly PrefabGUID CHAR_Cursed_Mosquito = new(1929801777);
    public static readonly PrefabGUID CHAR_Undead_ZombieVillager = new(-209892213);

    // ── Human Faction Enemies ────────────────────────────────────────────
    public static readonly PrefabGUID CHAR_Bandit_Miner = new(-838005762);
    public static readonly PrefabGUID CHAR_Bandit_Leader = new(385266649);
    public static readonly PrefabGUID CHAR_Militia_Crossbowman = new(1649578802);
    public static readonly PrefabGUID CHAR_Church_Paladin_VHunter = new(-1872869628);
    public static readonly PrefabGUID CHAR_Undead_BishopOfShadows = new(-576525740);
    public static readonly PrefabGUID CHAR_Militia_Mage = new(910299647);
    public static readonly PrefabGUID CHAR_Undead_ZombieSoldier = new(894576878);

    // ── VBlood Bosses — Tier 1 (Farbane Woods) ──────────────────────────
    public static readonly PrefabGUID VBlood_Errol = new(-484556888);       // Errol the Stonebreaker
    public static readonly PrefabGUID VBlood_Grayson = new(1106149033);     // Grayson the Armourer
    public static readonly PrefabGUID VBlood_Putrid = new(-1905691330);     // Putrid Rat
    public static readonly PrefabGUID VBlood_Keely = new(-1065970933);      // Keely the Frost Archer
    public static readonly PrefabGUID VBlood_Nicholaus = new(153390636);    // Nicholaus the Fallen
    public static readonly PrefabGUID VBlood_Quincey = new(-680831417);     // Quincey the Bandit King
    public static readonly PrefabGUID VBlood_Clive = new(1896428638);       // Clive the Firestarter
    public static readonly PrefabGUID VBlood_Lidia = new(763273073);        // Lidia the Chaos Archer
    public static readonly PrefabGUID VBlood_Rufus = new(2122229952);       // Rufus the Foreman
    public static PrefabGUID VBlood_Alpha_Wolf = new(-1905691330); // Alpha Wolf (resolved at runtime via ResolveLiveBossGuids())
    public static readonly PrefabGUID VBlood_Beatrice = new(-1208888966);   // Beatrice the Tailor

    // ── VBlood Bosses — Tier 2 (Dunley Farmlands) ───────────────────────
    public static readonly PrefabGUID VBlood_Jade = new(-1968372384);       // Jade the Vampire Hunter
    public static readonly PrefabGUID VBlood_Vincent = new(-106490747);     // Vincent the Frostbringer
    public static readonly PrefabGUID VBlood_Christina = new(1491494991);   // Christina the Sun Priestess
    public static readonly PrefabGUID VBlood_Tristan = new(-1449631170);    // Tristan the Vampire Hunter
    public static PrefabGUID VBlood_Leandra = new(763273073); // Leandra the Shadow Priestess (resolved at runtime via ResolveLiveBossGuids())
    public static readonly PrefabGUID VBlood_Meredith = new(850622034);     // Meredith the Bright Archer

    // ── VBlood Bosses — Tier 3 (Silverlight Hills / Cursed Forest) ──────
    public static readonly PrefabGUID VBlood_Octavian = new(1688478381);    // Octavian the Militia Captain
    public static readonly PrefabGUID VBlood_Styx = new(1062702006);        // Styx the Sunderer
    public static readonly PrefabGUID VBlood_Foulrot = new(1059731655);     // Foulrot the Soultaker
    public static readonly PrefabGUID VBlood_Gorecrusher = new(1182226883); // Gorecrusher the Behemoth
    public static readonly PrefabGUID VBlood_Ungora = new(-548489519);      // Ungora the Spider Queen
    public static readonly PrefabGUID VBlood_Nightmarshal = new(114912615); // Nightmarshal Styx

    // ── VBlood Bosses — Tier 4 / Endgame ────────────────────────────────
    public static readonly PrefabGUID VBlood_Dracula = new(-327335305);     // Dracula the Immortal King
    public static readonly PrefabGUID VBlood_SolarusTheImmaculate = new(-740796338); // Solarus the Immaculate
    public static readonly PrefabGUID VBlood_TheWinged_Horror = new(-1885959248);    // The Winged Horror
    public static readonly PrefabGUID VBlood_Morian = new(-1942352521);              // Morian the Stormwing Matriarch

    // ── Buffs — Combat ───────────────────────────────────────────────────
    public static readonly PrefabGUID Buff_BloodMend = new(-1918486886);
    public static readonly PrefabGUID Buff_InCombat = new(581443919);
    public static readonly PrefabGUID Buff_Cloak_Mounted = new(1533800424);
    public static PrefabGUID Buff_General_Ignite = new(-1576592033);
    public static readonly PrefabGUID Buff_General_Slow = new(-1374780085);
    public static readonly PrefabGUID Buff_General_Freeze = new(1083666249);
    public static readonly PrefabGUID Buff_General_Stun = new(-508086356);
    public static readonly PrefabGUID Buff_General_Weaken = new(-1436523498);
    public static readonly PrefabGUID Buff_General_Disease = new(-1571061352);
    public static readonly PrefabGUID Buff_General_Chill = new(-325758519);
    public static readonly PrefabGUID Buff_General_Shock = new(1580463213);
    public static readonly PrefabGUID Buff_General_Condemned = new(476366531);

    // ── Object / NPC Buffs ───────────────────────────────────────────────
    public static PrefabGUID Admin_Invulnerable_Buff = new(532440764);
    public static readonly PrefabGUID Buff_InCombat_PvPVampire = new(697095869);
    public static readonly PrefabGUID Buff_General_Garlic_Area = new(722928856);
    public static readonly PrefabGUID Buff_General_Silver_Sickness = new(-1204150716);
    public static readonly PrefabGUID Buff_General_Holy_T01 = new(-1694644790);
    public static readonly PrefabGUID Buff_SunDamageDebuff = new(-1315531444);
    public static readonly PrefabGUID Buff_General_PvPProtected = new(-1052685298);
    public static readonly PrefabGUID Buff_General_Phasing = new(1688015088);
    public static readonly PrefabGUID Buff_CombatStance = new(731266764);
    public static readonly PrefabGUID Buff_InvisibilityAndImmaterial = new(-1320950878);
    public static readonly PrefabGUID Buff_Emote_Default_NoAnimation = new(-1426788653);

    // ── Buffs — Vampire Passives / Forms ────────────────────────────────
    public static readonly PrefabGUID Buff_Vampire_BatForm = new(-931201464);
    public static readonly PrefabGUID Buff_Vampire_WolfForm = new(-351718282);
    public static readonly PrefabGUID Buff_Vampire_BearForm = new(2144782508);
    public static readonly PrefabGUID Buff_Vampire_BroadwingForm = new(1205505492);
    public static readonly PrefabGUID Buff_Vampire_Shapeshift_Human = new(1106149733);
    public static readonly PrefabGUID Buff_Vampire_Exposed = new(697095869); // resolved at runtime via ResolveLiveBuffGuids()

    // ── Consumables (loot crate rewards) ────────────────────────────────
    public static readonly PrefabGUID Item_Consumable_BloodRoseBrewV01 = new(429052660);
    public static readonly PrefabGUID Item_Consumable_BloodRoseBrewV02 = new(-1840170490);
    public static readonly PrefabGUID Item_Consumable_PhysicalPowerBrew = new(-1768752501);
    public static readonly PrefabGUID Item_Consumable_SpellPowerBrew = new(-880687279);
    public static readonly PrefabGUID Item_Consumable_HealingPotion_T01 = new(1279778001);
    public static readonly PrefabGUID Item_Consumable_HealingPotion_T02 = new(-1535671546);
    public static readonly PrefabGUID Item_Consumable_Vermin_Salve_T01 = new(476260855);
    public static readonly PrefabGUID Item_Consumable_PvPToken = new(-2038524544);
    public static readonly PrefabGUID Item_Consumable_Jewel_Flawless = new(1946922174);
    public static readonly PrefabGUID Item_Consumable_Jewel_Greater = new(-66355149);

    // ── Special Items ────────────────────────────────────────────────────
    public static readonly PrefabGUID Item_Bag_Dracula = new(1886741080);
    public static readonly PrefabGUID Item_NewbieResource_Lore = new(-1632001232);
    public static readonly PrefabGUID Item_Ingredient_BloodCrystal_Major = new(-1065861325);
    public static readonly PrefabGUID Item_Ingredient_BloodCrystal_Minor = new(178196126);

    // ── Vampire Base Ability Groups (resolved at runtime, GUIDs for reference) ──
    public static PrefabGUID AB_Vampire_PrimaryAttack_AbilityGroup = new(-740796338); // resolved at runtime via ResolveLiveBossGuids()
    public static readonly PrefabGUID AB_Vampire_VampireDash_AbilityGroup = new(-2089458811);
    public static readonly PrefabGUID AB_Vampire_VeilOfBlood_AbilityGroup = new(-1055144663);

    // ── Passive Ability Pools ────────────────────────────────────────────
    public static readonly PrefabGUID AB_Passive_BloodType_Warrior = new(1984706510);
    public static readonly PrefabGUID AB_Passive_BloodType_Rogue = new(1767997792);
    public static readonly PrefabGUID AB_Passive_BloodType_Scholar = new(820910512);
    public static readonly PrefabGUID AB_Passive_BloodType_Brute = new(-491388361);
    public static readonly PrefabGUID AB_Passive_BloodType_Draculin = new(-1651706207);
    public static readonly PrefabGUID AB_Passive_BloodType_Creature = new(-1307712694);

    /// <summary>
    /// Attempt to resolve buff GUIDs from live data if the hardcoded ones are invalid.
    /// Call after PrefabHelper.ScanLivePrefabs().
    /// </summary>
    public static void ResolveLiveBuffGuids()
    {
        // Try to find General_Ignite in live data (name may vary by game version)
        var ignite = PrefabHelper.GetLivePrefabGuid("Buff_General_Ignite")
                  ?? PrefabHelper.GetLivePrefabGuid("Buff_Ignite")
                  ?? PrefabHelper.GetLivePrefabGuid("AB_Ignite");

        if (ignite.HasValue)
        {
            Buff_General_Ignite = ignite.Value;
            BattleLuckPlugin.LogInfo($"[Prefabs] Resolved Buff_General_Ignite from live data: {ignite.Value.GuidHash}");
        }
        else
        {
            // Check if the hardcoded GUID is valid
            if (!PrefabHelper.ValidatePrefab(Buff_General_Ignite))
            {
                BattleLuckPlugin.LogWarning($"[Prefabs] Buff_General_Ignite ({Buff_General_Ignite.GuidHash}) is INVALID and not found in live data. Burning effects will use HP drain fallback.");
                Buff_General_Ignite = PrefabGUID.Empty;
            }
        }

        if (Admin_Invulnerable_Buff != PrefabGUID.Empty && !PrefabHelper.ValidatePrefab(Admin_Invulnerable_Buff))
        {
            var adminInvulnerable = PrefabHelper.GetLivePrefabGuid("Admin_Invulnerable_Buff")
                                   ?? PrefabHelper.GetLivePrefabGuid("Buff_Admin_Invulnerable")
                                   ?? PrefabHelper.GetLivePrefabGuid("Buff_General_Invulnerable");

            if (adminInvulnerable.HasValue && PrefabHelper.ValidatePrefab(adminInvulnerable.Value))
            {
                Admin_Invulnerable_Buff = adminInvulnerable.Value;
                BattleLuckPlugin.LogInfo($"[Prefabs] Resolved Admin_Invulnerable_Buff from live data: {adminInvulnerable.Value.GuidHash}");
            }
            else
            {
                BattleLuckPlugin.LogWarning($"[Prefabs] Admin_Invulnerable_Buff (532440764) is invalid in this server build. Static event objects will use nondismantle/cleanup protection without that buff.");
                Admin_Invulnerable_Buff = PrefabGUID.Empty;
            }
        }
    }

    /// <summary>
    /// Attempt to resolve VBlood boss GUIDs that are suspected duplicates or reference values
    /// from live data. Targets: VBlood_Alpha_Wolf, VBlood_Leandra, AB_Vampire_PrimaryAttack_AbilityGroup.
    /// Call after PrefabHelper.ScanLivePrefabs().
    /// </summary>
    public static void ResolveLiveBossGuids()
    {
        // VBlood_Alpha_Wolf: suspect duplicate of VBlood_Putrid. Try several name variants.
        var alphaWolf = PrefabHelper.GetLivePrefabGuid("VBlood_Alpha_Wolf")
                     ?? PrefabHelper.GetLivePrefabGuid("CHAR_Winter_Wolf")
                     ?? PrefabHelper.GetLivePrefabGuid("CHAR_Wildlife_Wolf_VBlood");
        if (alphaWolf.HasValue && alphaWolf.Value.GuidHash != VBlood_Putrid.GuidHash)
        {
            VBlood_Alpha_Wolf = alphaWolf.Value;
            BattleLuckPlugin.LogInfo($"[Prefabs] Resolved VBlood_Alpha_Wolf from live data: {alphaWolf.Value.GuidHash} (was: {(-1905691330)})");
        }
        else if (VBlood_Alpha_Wolf == VBlood_Putrid)
        {
            BattleLuckPlugin.LogWarning($"[Prefabs] VBlood_Alpha_Wolf shares GUID with VBlood_Putrid; treating as alias. Will spawn Putrid when Alpha Wolf is requested.");
        }

        // VBlood_Leandra: suspect duplicate of VBlood_Lidia. Try several name variants.
        var leandra = PrefabHelper.GetLivePrefabGuid("VBlood_Leandra")
                   ?? PrefabHelper.GetLivePrefabGuid("CHAR_Leandra_VBlood")
                   ?? PrefabHelper.GetLivePrefabGuid("CHAR_Undead_Leandra_VBlood");
        if (leandra.HasValue && leandra.Value.GuidHash != VBlood_Lidia.GuidHash)
        {
            VBlood_Leandra = leandra.Value;
            BattleLuckPlugin.LogInfo($"[Prefabs] Resolved VBlood_Leandra from live data: {leandra.Value.GuidHash} (was: {(763273073)})");
        }
        else if (VBlood_Leandra == VBlood_Lidia)
        {
            BattleLuckPlugin.LogWarning($"[Prefabs] VBlood_Leandra shares GUID with VBlood_Lidia; treating as alias. Will spawn Lidia when Leandra is requested.");
        }

        // AB_Vampire_PrimaryAttack_AbilityGroup: identical to VBlood_SolarusTheImmaculate (likely wrong reference).
        var primaryAttack = PrefabHelper.GetLivePrefabGuid("AB_Vampire_PrimaryAttack_AbilityGroup")
                         ?? PrefabHelper.GetLivePrefabGuid("AB_Vampire_BasicAttack_AbilityGroup")
                         ?? PrefabHelper.GetLivePrefabGuid("VampireBasicAttack");
        if (primaryAttack.HasValue && primaryAttack.Value.GuidHash != VBlood_SolarusTheImmaculate.GuidHash)
        {
            AB_Vampire_PrimaryAttack_AbilityGroup = primaryAttack.Value;
            BattleLuckPlugin.LogInfo($"[Prefabs] Resolved AB_Vampire_PrimaryAttack_AbilityGroup from live data: {primaryAttack.Value.GuidHash} (was: {(-740796338)})");
        }
        else if (AB_Vampire_PrimaryAttack_AbilityGroup == VBlood_SolarusTheImmaculate)
        {
            BattleLuckPlugin.LogWarning($"[Prefabs] AB_Vampire_PrimaryAttack_AbilityGroup shares GUID with VBlood_SolarusTheImmaculate; treating as alias. Primary attack ability grants will use the Solarus GUID until a corrected one is found.");
        }
    }

    /// <summary>
    /// Attempt to resolve research scroll GUIDs from live data. The hardcoded -123456789-family
    /// placeholders are NOT valid V Rising prefab GUIDs. Call after PrefabHelper.ScanLivePrefabs().
    /// </summary>
    public static void ResolveLiveResearchGuids()
    {
        // Tier 1 scroll — try common naming variants.
        var t1 = PrefabHelper.GetLivePrefabGuid("Item_Research_Scroll_Tier01")
              ?? PrefabHelper.GetLivePrefabGuid("Research_Scroll_T1")
              ?? PrefabHelper.GetLivePrefabGuid("Item_Research_Scroll_T01");
        if (t1.HasValue)
        {
            Item_Research_Scroll_Tier1 = t1.Value;
            BattleLuckPlugin.LogInfo($"[Prefabs] Resolved Item_Research_Scroll_Tier1 from live data: {t1.Value.GuidHash}");
        }
        else if (!PrefabHelper.ValidatePrefab(Item_Research_Scroll_Tier1))
        {
            BattleLuckPlugin.LogWarning($"[Prefabs] Item_Research_Scroll_Tier1 ({Item_Research_Scroll_Tier1.GuidHash}) is the placeholder value and was not found in live data. Research unlock effects will be no-ops.");
            Item_Research_Scroll_Tier1 = PrefabGUID.Empty;
        }

        // Tier 2 scroll.
        var t2 = PrefabHelper.GetLivePrefabGuid("Item_Research_Scroll_Tier02")
              ?? PrefabHelper.GetLivePrefabGuid("Research_Scroll_T2")
              ?? PrefabHelper.GetLivePrefabGuid("Item_Research_Scroll_T02");
        if (t2.HasValue)
        {
            Item_Research_Scroll_Tier2 = t2.Value;
            BattleLuckPlugin.LogInfo($"[Prefabs] Resolved Item_Research_Scroll_Tier2 from live data: {t2.Value.GuidHash}");
        }
        else if (!PrefabHelper.ValidatePrefab(Item_Research_Scroll_Tier2))
        {
            BattleLuckPlugin.LogWarning($"[Prefabs] Item_Research_Scroll_Tier2 ({Item_Research_Scroll_Tier2.GuidHash}) is the placeholder value and was not found in live data.");
            Item_Research_Scroll_Tier2 = PrefabGUID.Empty;
        }

        // Tier 3 scroll.
        var t3 = PrefabHelper.GetLivePrefabGuid("Item_Research_Scroll_Tier03")
              ?? PrefabHelper.GetLivePrefabGuid("Research_Scroll_T3")
              ?? PrefabHelper.GetLivePrefabGuid("Item_Research_Scroll_T03");
        if (t3.HasValue)
        {
            Item_Research_Scroll_Tier3 = t3.Value;
            BattleLuckPlugin.LogInfo($"[Prefabs] Resolved Item_Research_Scroll_Tier3 from live data: {t3.Value.GuidHash}");
        }
        else if (!PrefabHelper.ValidatePrefab(Item_Research_Scroll_Tier3))
        {
            BattleLuckPlugin.LogWarning($"[Prefabs] Item_Research_Scroll_Tier3 ({Item_Research_Scroll_Tier3.GuidHash}) is the placeholder value and was not found in live data.");
            Item_Research_Scroll_Tier3 = PrefabGUID.Empty;
        }
    }

    /// <summary>
    /// One-shot entry point: resolves every place where the hardcoded GUID set is incomplete or
    /// known to collide. Safe to call multiple times.
    /// </summary>
    public static void ResolveAllLiveGuids()
    {
        ResolveLiveBuffGuids();
        ResolveLiveBossGuids();
        ResolveLiveResearchGuids();
    }

    /// <summary>
    /// Returns a de-duplicated list of all VBlood boss prefabs. Useful when callers need to
    /// enumerate bosses for unlocks / kit grants without double-counting the alias entries
    /// (VBlood_Alpha_Wolf = VBlood_Putrid, VBlood_Leandra = VBlood_Lidia).
    /// </summary>
    public static List<PrefabGUID> GetAllVBloodPrefabsUnique()
    {
        var raw = new List<PrefabGUID>
        {
            CHAR_Vampire_HighLord_VBlood,
            CHAR_Manticore_VBlood,
            CHAR_Blackfang_Morgana_VBlood,
            CHAR_Undead_BishopOfShadows_VBlood,
            CHAR_Winter_Yeti_VBlood,
            CHAR_Forest_Wolf_VBlood,
            CHAR_Forest_Bear_VBlood,
            CHAR_Forest_Gloomrot_VBlood,
            CHAR_Cursed_Forest_VBlood,
            VBlood_Errol,
            VBlood_Grayson,
            VBlood_Putrid,
            VBlood_Keely,
            VBlood_Nicholaus,
            VBlood_Quincey,
            VBlood_Clive,
            VBlood_Lidia,
            VBlood_Rufus,
            VBlood_Alpha_Wolf,
            VBlood_Beatrice,
            VBlood_Jade,
            VBlood_Vincent,
            VBlood_Christina,
            VBlood_Tristan,
            VBlood_Leandra,
            VBlood_Meredith,
            VBlood_Octavian,
            VBlood_Styx,
            VBlood_Foulrot,
            VBlood_Gorecrusher,
            VBlood_Ungora,
            VBlood_Nightmarshal,
            VBlood_Dracula,
            VBlood_SolarusTheImmaculate,
            VBlood_TheWinged_Horror,
            VBlood_Morian,
        };

        // De-dup by GuidHash while preserving order.
        var seen = new HashSet<int>();
        var unique = new List<PrefabGUID>(raw.Count);
        foreach (var guid in raw)
        {
            if (guid == PrefabGUID.Empty) continue;
            if (seen.Add(guid.GuidHash)) unique.Add(guid);
        }
        return unique;
    }

    // ── Floor Tiles ──────────────────────────────────────────────────────
    public static readonly PrefabGUID TM_Castle_Floor_Tier01_Wood = new(-1629026507);
    public static readonly PrefabGUID TM_Castle_Floor_Tier02_Stone = new(-286344253);
    public static readonly PrefabGUID TM_Castle_Floor_Tier03_Marble = new(-951873891);

    // ── Arena / Environment Tiles ────────────────────────────────────────
    public static readonly PrefabGUID TM_Castle_Floor_Graveyard = new(-1724796459);
    public static readonly PrefabGUID TM_Castle_Floor_BloodyStone = new(-773992595);

    // ── Weapon Tier Lookup Helpers ───────────────────────────────────────

    /// <summary>Returns the sword prefab for a given tier (1–9).</summary>
    public static PrefabGUID GetSwordForTier(int tier) => tier switch
    {
        1 => Item_Weapon_Sword_T01,
        2 => Item_Weapon_Sword_T02,
        3 => Item_Weapon_Sword_T03,
        4 => Item_Weapon_Sword_T04,
        5 => Item_Weapon_Sword_T05,
        6 => Item_Weapon_Sword_T06,
        7 => Item_Weapon_Sword_T07,
        8 => Item_Weapon_Sword_T08,
        _ => Item_Weapon_Sword_T09,
    };

    /// <summary>Returns the chest armor prefab for a given tier (2/4/6/8/9).</summary>
    public static PrefabGUID GetChestForTier(int tier) => tier switch
    {
        2 => Item_Armor_Chest_T02,
        4 => Item_Armor_Chest_T04,
        6 => Item_Armor_Chest_T06,
        8 => Item_Armor_Chest_T08,
        _ => Item_Armor_Chest_T09,
    };

    /// <summary>Returns magic source for a given school and tier (6/8/9).</summary>
    public static PrefabGUID GetMagicSourceForSchool(string school, int tier = 9) =>
        (school.ToLowerInvariant(), tier) switch
        {
            ("blood",   6) => Item_MagicSource_Blood_T06,
            ("blood",   8) => Item_MagicSource_Blood_T08,
            ("unholy",  6) => Item_MagicSource_Unholy_T06,
            ("unholy",  8) => Item_MagicSource_Unholy_T08,
            ("chaos",   6) => Item_MagicSource_Chaos_T06,
            ("chaos",   8) => Item_MagicSource_Chaos_T08,
            ("frost",   6) => Item_MagicSource_Frost_T06,
            ("frost",   8) => Item_MagicSource_Frost_T08,
            ("storm",   6) => Item_MagicSource_Storm_T06,
            ("storm",   8) => Item_MagicSource_Storm_T08,
            ("illusion",6) => Item_MagicSource_Illusion_T06,
            ("illusion",8) => Item_MagicSource_Illusion_T08,
            ("blood",   _) => Item_MagicSource_Blood_T09,
            ("unholy",  _) => Item_MagicSource_Unholy_T09,
            ("chaos",   _) => Item_MagicSource_Chaos_T09,
            ("frost",   _) => Item_MagicSource_Frost_T09,
            ("storm",   _) => Item_MagicSource_Storm_T09,
            ("illusion",_) => Item_MagicSource_Illusion_T09,
            _ => Item_MagicSource_Blood_T09,
        };

    // ── Per-weapon-type tier helpers (T01–T09) ───────────────────────────

    /// <summary>Returns the axe prefab for a given tier (1–9).</summary>
    public static PrefabGUID GetAxeForTier(int tier) => tier switch
    {
        1 => Item_Weapon_Axe_T01, 2 => Item_Weapon_Axe_T02, 3 => Item_Weapon_Axe_T03,
        4 => Item_Weapon_Axe_T04, 5 => Item_Weapon_Axe_T05, 6 => Item_Weapon_Axe_T06,
        7 => Item_Weapon_Axe_T07, 8 => Item_Weapon_Axe_T08, _ => Item_Weapon_Axe_T09,
    };

    /// <summary>Returns the mace prefab for a given tier (1–9).</summary>
    public static PrefabGUID GetMaceForTier(int tier) => tier switch
    {
        1 => Item_Weapon_Mace_T01, 2 => Item_Weapon_Mace_T02, 3 => Item_Weapon_Mace_T03,
        4 => Item_Weapon_Mace_T04, 5 => Item_Weapon_Mace_T05, 6 => Item_Weapon_Mace_T06,
        7 => Item_Weapon_Mace_T07, 8 => Item_Weapon_Mace_T08, _ => Item_Weapon_Mace_T09,
    };

    /// <summary>Returns the spear prefab for a given tier (1–9).</summary>
    public static PrefabGUID GetSpearForTier(int tier) => tier switch
    {
        1 => Item_Weapon_Spear_T01, 2 => Item_Weapon_Spear_T02, 3 => Item_Weapon_Spear_T03,
        4 => Item_Weapon_Spear_T04, 5 => Item_Weapon_Spear_T05, 6 => Item_Weapon_Spear_T06,
        7 => Item_Weapon_Spear_T07, 8 => Item_Weapon_Spear_T08, _ => Item_Weapon_Spear_T09,
    };

    /// <summary>Returns the crossbow prefab for a given tier (1–9).</summary>
    public static PrefabGUID GetCrossbowForTier(int tier) => tier switch
    {
        1 => Item_Weapon_Crossbow_T01, 2 => Item_Weapon_Crossbow_T02, 3 => Item_Weapon_Crossbow_T03,
        4 => Item_Weapon_Crossbow_T04, 5 => Item_Weapon_Crossbow_T05, 6 => Item_Weapon_Crossbow_T06,
        7 => Item_Weapon_Crossbow_T07, 8 => Item_Weapon_Crossbow_T08, _ => Item_Weapon_Crossbow_T09,
    };

    /// <summary>Returns the slashers prefab for a given tier (1–9).</summary>
    public static PrefabGUID GetSlashersForTier(int tier) => tier switch
    {
        1 => Item_Weapon_Slashers_T01, 2 => Item_Weapon_Slashers_T02, 3 => Item_Weapon_Slashers_T03,
        4 => Item_Weapon_Slashers_T04, 5 => Item_Weapon_Slashers_T05, 6 => Item_Weapon_Slashers_T06,
        7 => Item_Weapon_Slashers_T07, 8 => Item_Weapon_Slashers_T08, _ => Item_Weapon_Slashers_T09,
    };

    /// <summary>Returns the reaper prefab for a given tier (1–9).</summary>
    public static PrefabGUID GetReaperForTier(int tier) => tier switch
    {
        1 => Item_Weapon_Reaper_T01, 2 => Item_Weapon_Reaper_T02, 3 => Item_Weapon_Reaper_T03,
        4 => Item_Weapon_Reaper_T04, 5 => Item_Weapon_Reaper_T05, 6 => Item_Weapon_Reaper_T06,
        7 => Item_Weapon_Reaper_T07, 8 => Item_Weapon_Reaper_T08, _ => Item_Weapon_Reaper_T09,
    };

    /// <summary>Returns the pistols prefab for a given tier (1–9).</summary>
    public static PrefabGUID GetPistolsForTier(int tier) => tier switch
    {
        1 => Item_Weapon_Pistols_T01, 2 => Item_Weapon_Pistols_T02, 3 => Item_Weapon_Pistols_T03,
        4 => Item_Weapon_Pistols_T04, 5 => Item_Weapon_Pistols_T05, 6 => Item_Weapon_Pistols_T06,
        7 => Item_Weapon_Pistols_T07, 8 => Item_Weapon_Pistols_T08, _ => Item_Weapon_Pistols_T09,
    };

    /// <summary>Returns the great sword prefab for a given tier (1–9).</summary>
    public static PrefabGUID GetGreatSwordForTier(int tier) => tier switch
    {
        1 => Item_Weapon_GreatSword_T01, 2 => Item_Weapon_GreatSword_T02, 3 => Item_Weapon_GreatSword_T03,
        4 => Item_Weapon_GreatSword_T04, 5 => Item_Weapon_GreatSword_T05, 6 => Item_Weapon_GreatSword_T06,
        7 => Item_Weapon_GreatSword_T07, 8 => Item_Weapon_GreatSword_T08, _ => Item_Weapon_GreatSword_T09,
    };

    /// <summary>Returns the whip prefab for a given tier (1–9).</summary>
    public static PrefabGUID GetWhipForTier(int tier) => tier switch
    {
        1 => Item_Weapon_Whip_T01, 2 => Item_Weapon_Whip_T02, 3 => Item_Weapon_Whip_T03,
        4 => Item_Weapon_Whip_T04, 5 => Item_Weapon_Whip_T05, 6 => Item_Weapon_Whip_T06,
        7 => Item_Weapon_Whip_T07, 8 => Item_Weapon_Whip_T08, _ => Item_Weapon_Whip_T09,
    };

    /// <summary>
    /// Returns any weapon prefab by type name and tier (1–9).
    /// Recognised types: sword, axe, mace, spear, crossbow, slashers, reaper, pistols, greatsword, whip.
    /// Falls back to sword for unknown type strings.
    /// </summary>
    public static PrefabGUID GetWeaponForTypeAndTier(string weaponType, int tier) =>
        weaponType.ToLowerInvariant() switch
        {
            "axe"        => GetAxeForTier(tier),
            "mace"       => GetMaceForTier(tier),
            "spear"      => GetSpearForTier(tier),
            "crossbow"   => GetCrossbowForTier(tier),
            "slashers"   => GetSlashersForTier(tier),
            "reaper"     => GetReaperForTier(tier),
            "pistols"    => GetPistolsForTier(tier),
            "greatsword" => GetGreatSwordForTier(tier),
            "whip"       => GetWhipForTier(tier),
            _            => GetSwordForTier(tier),
        };

    // ── Per-slot armor tier helpers (available tiers: 2/4/6/8/9) ────────

    /// <summary>Returns the legs armor prefab for a given tier (2/4/6/8/9).</summary>
    public static PrefabGUID GetLegsForTier(int tier) => tier switch
    {
        2 => Item_Armor_Legs_T02, 4 => Item_Armor_Legs_T04,
        6 => Item_Armor_Legs_T06, 8 => Item_Armor_Legs_T08,
        _ => Item_Armor_Legs_T09,
    };

    /// <summary>Returns the gloves armor prefab for a given tier (2/4/6/8/9).</summary>
    public static PrefabGUID GetGlovesForTier(int tier) => tier switch
    {
        2 => Item_Armor_Gloves_T02, 4 => Item_Armor_Gloves_T04,
        6 => Item_Armor_Gloves_T06, 8 => Item_Armor_Gloves_T08,
        _ => Item_Armor_Gloves_T09,
    };

    /// <summary>Returns the boots armor prefab for a given tier (2/4/6/8/9).</summary>
    public static PrefabGUID GetBootsForTier(int tier) => tier switch
    {
        2 => Item_Armor_Boots_T02, 4 => Item_Armor_Boots_T04,
        6 => Item_Armor_Boots_T06, 8 => Item_Armor_Boots_T08,
        _ => Item_Armor_Boots_T09,
    };

    /// <summary>Returns the cloak prefab for a given tier (2/4/6/8/9).</summary>
    public static PrefabGUID GetCloakForTier(int tier) => tier switch
    {
        2 => Item_Cloak_T02, 4 => Item_Cloak_T04,
        6 => Item_Cloak_T06, 8 => Item_Cloak_T08,
        _ => Item_Cloak_T09,
    };

    /// <summary>Returns the headgear prefab for a given tier (2/4/6/8/9).</summary>
    public static PrefabGUID GetHeadgearForTier(int tier) => tier switch
    {
        2 => Item_Headgear_T02, 4 => Item_Headgear_T04,
        6 => Item_Headgear_T06, 8 => Item_Headgear_T08,
        _ => Item_Headgear_T09,
    };

    /// <summary>
    /// Returns a complete armor set (all 6 slots) for a given tier (2/4/6/8/9).
    /// </summary>
    public static (PrefabGUID Chest, PrefabGUID Legs, PrefabGUID Gloves, PrefabGUID Boots, PrefabGUID Cloak, PrefabGUID Headgear)
        GetFullArmorSetForTier(int tier) => (
            GetChestForTier(tier),
            GetLegsForTier(tier),
            GetGlovesForTier(tier),
            GetBootsForTier(tier),
            GetCloakForTier(tier),
            GetHeadgearForTier(tier)
        );

    // ── AI Hologram Display ─────────────────────────────────────────────────────
    public static readonly PrefabGUID AI_Hologram_Entity = PrefabGUID.Empty; // Reserved for future use
    public static readonly PrefabGUID Obj_TextMarker = PrefabGUID.Empty; // Resolved at runtime if available
}
