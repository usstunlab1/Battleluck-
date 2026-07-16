using Unity.Mathematics;

public sealed class ManualShrinkState
{
    public bool Enabled { get; set; } = true;
    public float3 Center { get; set; }
    public float CurrentRadius { get; set; }
    public float TargetRadius { get; set; }
    public float ShrinkRatePerSecond { get; set; }
    public float ExitBuffer { get; set; } = 5f;
    public bool DamageOnly { get; set; } = true;
    public DateTime NextBroadcastUtc { get; set; } = DateTime.UtcNow;
    public int LastRadiusBucket { get; set; } = -1;
}
