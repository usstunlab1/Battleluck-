namespace BattleLuck.Models;

public sealed class AiConfigEditResult
{
    public Dictionary<string, JsonElement> UpdatedFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

