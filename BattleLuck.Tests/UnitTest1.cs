using BattleLuck.Services.AI;
using BattleLuck.Services.Placement;

namespace BattleLuck.Tests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        Assert.True(true);
    }
}

public class CategoryResolversTests
{
    [Fact]
    public void EmptyName_ReturnsNotFound()
    {
        var result = CategoryResolvers.ResolveBoss("");
        Assert.Equal(CategoryResolvers.ResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void NullName_ReturnsNotFound()
    {
        var result = CategoryResolvers.ResolveBoss(null!);
        Assert.Equal(CategoryResolvers.ResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void Dracula_ResolvesToCanonicalAlias()
    {
        var result = CategoryResolvers.ResolveBoss("dracula");
        Assert.Equal(CategoryResolvers.ResolutionKind.Resolved, result.Kind);
        Assert.Equal("CHAR_Vampire_HighLord_VBlood", result.CanonicalName);
    }

    [Fact]
    public void Morgana_ResolvesCorrectly()
    {
        var result = CategoryResolvers.ResolveBoss("morgana");
        Assert.Equal(CategoryResolvers.ResolutionKind.Resolved, result.Kind);
        Assert.Equal("CHAR_Blackfang_Morgana_VBlood", result.CanonicalName);
    }

    [Fact]
    public void UnknownBoss_ReturnsNotFound()
    {
        var result = CategoryResolvers.ResolveBoss("completely_fake_boss_that_does_not_exist_12345");
        Assert.Equal(CategoryResolvers.ResolutionKind.NotFound, result.Kind);
    }

    [Fact]
    public void ResolveBoss_DoesNotReturnDefaultOrRandom()
    {
        var result = CategoryResolvers.ResolveBoss("floors");
        Assert.NotEqual(CategoryResolvers.ResolutionKind.Resolved, result.Kind);
    }

    [Fact]
    public void Normalization_HandlesPrefixes()
    {
        var result1 = CategoryResolvers.ResolveBoss("dracula");
        var result2 = CategoryResolvers.ResolveBoss("CHAR_Vampire_HighLord_VBlood");
        Assert.Equal(CategoryResolvers.ResolutionKind.Resolved, result1.Kind);
        Assert.Equal(CategoryResolvers.ResolutionKind.Resolved, result2.Kind);
    }
}

public class ActionProposalStoreTests
{
    [Fact]
    public void CreateProposal_ReturnsValidId()
    {
        var proposal = ActionProposalStore.CreateProposal(
            12345, "spawn dracula", "spawn.boss", "boss",
            "CHAR_Vampire_HighLord_VBlood", 123456,
            new Dictionary<string, string> { { "prefab", "CHAR_Vampire_HighLord_VBlood" } },
            requiresApproval: true);
        Assert.NotNull(proposal);
        Assert.False(string.IsNullOrWhiteSpace(proposal.ProposalId));
        Assert.Equal(12345UL, proposal.SteamId);
    }

    [Fact]
    public void ConfirmProposal_ExactId_Succeeds()
    {
        var proposal = ActionProposalStore.CreateProposal(
            12345, "spawn dracula", "spawn.boss", "boss",
            "CHAR_Vampire_HighLord_VBlood", 123456,
            new Dictionary<string, string>(),
            requiresApproval: true);
        var confirmed = ActionProposalStore.TryConfirm(12345, proposal.ProposalId, out var result);
        Assert.True(confirmed);
        Assert.NotNull(result);
    }

    [Fact]
    public void ConfirmProposal_WrongSteamId_Fails()
    {
        var proposal = ActionProposalStore.CreateProposal(
            12345, "spawn dracula", "spawn.boss", "boss",
            "CHAR_Vampire_HighLord_VBlood", 123456,
            new Dictionary<string, string>(),
            requiresApproval: true);
        var confirmed = ActionProposalStore.TryConfirm(99999, proposal.ProposalId, out var result);
        Assert.False(confirmed);
        Assert.Null(result);
    }

    [Fact]
    public void ConfirmProposal_Twice_Fails()
    {
        var proposal = ActionProposalStore.CreateProposal(
            12345, "spawn dracula", "spawn.boss", "boss",
            "CHAR_Vampire_HighLord_VBlood", 123456,
            new Dictionary<string, string>(),
            requiresApproval: true);
        Assert.True(ActionProposalStore.TryConfirm(12345, proposal.ProposalId, out _));
        Assert.False(ActionProposalStore.TryConfirm(12345, proposal.ProposalId, out _));
    }

    [Fact]
    public void NewProposal_ReplacesOldProposal()
    {
        var first = ActionProposalStore.CreateProposal(
            12345, "spawn morgana", "spawn.boss", "boss",
            "CHAR_Blackfang_Morgana_VBlood", 654321,
            new Dictionary<string, string>(),
            requiresApproval: true);
        var second = ActionProposalStore.CreateProposal(
            12345, "spawn dracula", "spawn.boss", "boss",
            "CHAR_Vampire_HighLord_VBlood", 123456,
            new Dictionary<string, string>(),
            requiresApproval: true);
        Assert.False(ActionProposalStore.TryConfirm(12345, first.ProposalId, out _));
        Assert.True(ActionProposalStore.TryConfirm(12345, second.ProposalId, out var result));
        Assert.NotNull(result);
        Assert.Equal("CHAR_Vampire_HighLord_VBlood", result!.ResolvedCanonicalName);
    }

    [Fact]
    public void CancelLatest_RemovesProposal()
    {
        ActionProposalStore.CreateProposal(
            12345, "spawn dracula", "spawn.boss", "boss",
            "CHAR_Vampire_HighLord_VBlood", 123456,
            new Dictionary<string, string>(),
            requiresApproval: true);
        Assert.True(ActionProposalStore.CancelLatest(12345, out var label));
        Assert.Equal("spawn.boss", label);
        Assert.False(ActionProposalStore.HasPending(12345));
    }

    [Fact]
    public void FloorRequest_DoesNotLeakToBossProposal()
    {
        var floorProposal = ActionProposalStore.CreateProposal(
            12345, "place floors here", "schematic.loadatpos", "floor_schematic",
            "Arena Floor", 0,
            new Dictionary<string, string> { { "schematicId", "Arena Floor" } },
            requiresApproval: true);
        Assert.Equal("schematic.loadatpos", floorProposal.ActionName);
        Assert.Equal("floor_schematic", floorProposal.ResolvedCategory);
        var bossProposal = ActionProposalStore.CreateProposal(
            12345, "spawn dracula", "spawn.boss", "boss",
            "CHAR_Vampire_HighLord_VBlood", 123456,
            new Dictionary<string, string> { { "prefab", "CHAR_Vampire_HighLord_VBlood" } },
            requiresApproval: true);
        Assert.Equal("spawn.boss", bossProposal.ActionName);
        Assert.Equal("boss", bossProposal.ResolvedCategory);
        Assert.DoesNotContain("schematicId", bossProposal.Arguments.Keys);
    }
}

public class PlacementAreaResolverTests
{
    [Fact]
    public void ExplicitRadius_Wins()
    {
        var area = PlacementAreaResolver.Resolve(
            requestedRadius: 30f,
            requestedCenter: null,
            activeZone: null,
            senderPosition: default,
            requestExplicitlySaysHere: true);
        Assert.True(area.Valid);
        Assert.Equal(30f, area.EffectiveRadius);
        Assert.Equal(PlacementAreaResolver.RadiusSourceType.ExplicitRadius, area.RadiusSource);
    }

    [Fact]
    public void NoRadiusAndNoZone_ValidationFails()
    {
        var area = PlacementAreaResolver.Resolve(
            requestedRadius: null,
            requestedCenter: null,
            activeZone: null,
            senderPosition: default,
            requestExplicitlySaysHere: false);
        Assert.False(area.Valid);
        Assert.Contains("No placement center", area.Error);
    }

    [Fact]
    public void EstimateTileCount_ReturnsZeroForInvalid()
    {
        var count = PlacementAreaResolver.EstimateTileCount(
            new PlacementAreaResolver.PlacementArea { Valid = false },
            tileSpacing: 2.5f,
            tileHalfDiagonal: 1.0f);
        Assert.Equal(0, count);
    }
}

public class BoundedWorkQueueTests
{
    [Fact]
    public void EnqueueAndProcess_Placement()
    {
        BoundedWorkQueue.Clear();
        BoundedWorkQueue.EnqueuePlacement("session1", 2006, "TM_Floor_Tile", 12345, default, "batch1");
        Assert.Equal(1, BoundedWorkQueue.PendingCount);
        var processed = BoundedWorkQueue.ProcessBatch();
        Assert.True(processed >= 1);
    }

    [Fact]
    public void EnqueueAndProcess_Spawn()
    {
        BoundedWorkQueue.Clear();
        BoundedWorkQueue.EnqueueSpawn("session1", 2006, "CHAR_Wolf", 54321, default, "batch1");
        Assert.Equal(1, BoundedWorkQueue.PendingCount);
        var processed = BoundedWorkQueue.ProcessBatch();
        Assert.True(processed >= 1);
    }

    [Fact]
    public void CancelSession_RemovesPendingItems()
    {
        BoundedWorkQueue.Clear();
        BoundedWorkQueue.EnqueuePlacement("session1", 2006, "TM_Floor_Tile", 12345, default, "batch1");
        BoundedWorkQueue.EnqueuePlacement("session1", 2006, "TM_Floor_Tile", 12345, default, "batch1");
        BoundedWorkQueue.EnqueuePlacement("session2", 2007, "TM_Wall", 67890, default, "batch2");
        Assert.Equal(3, BoundedWorkQueue.PendingCount);
        var cancelled = BoundedWorkQueue.CancelSession("session1");
        Assert.Equal(2, cancelled);
        Assert.Equal(1, BoundedWorkQueue.PendingCount);
    }

    [Fact]
    public void Clear_RemovesAllItems()
    {
        BoundedWorkQueue.Clear();
        BoundedWorkQueue.EnqueuePlacement("session1", 2006, "TM_Floor_Tile", 12345, default, "batch1");
        BoundedWorkQueue.EnqueueSpawn("session1", 2006, "CHAR_Wolf", 54321, default, "batch2");
        BoundedWorkQueue.Clear();
        Assert.Equal(0, BoundedWorkQueue.PendingCount);
    }
}