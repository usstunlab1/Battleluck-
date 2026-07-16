using Unity.Mathematics;

public sealed class DeliveryObjectiveState
{
    public string ObjectiveId { get; set; } = "delivery";
    public int? ItemGuidHash { get; set; }
    public int Amount { get; set; } = 1;
    public float3 Position { get; set; }
    public float Radius { get; set; } = 4f;
    public int RewardPoints { get; set; } = 25;
    public int? TeamId { get; set; }
    public string Message { get; set; } = "";
    public bool Repeatable { get; set; }
    public bool Enabled { get; set; } = true;
    public HashSet<ulong> CompletedSteamIds { get; set; } = new();
}
