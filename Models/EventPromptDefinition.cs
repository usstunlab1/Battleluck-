namespace BattleLuck.Models;

/// <summary>
/// Parsed frontmatter from an Events/<modeId>/prompt.txt file.
/// DEPRECATED: This class is kept for legacy compatibility. The canonical
/// AI policy is now in event.json ai block. prompt.txt is optional and
/// contains only narrative override text.
/// </summary>
public sealed class EventPromptDefinition
{
    public string EventId { get; set; } = "";
    public List<string> AllowedActions { get; set; } = new();
    public List<string> BlockedActions { get; set; } = new();
    public List<string> AllowedTechs { get; set; } = new();
    public string Body { get; set; } = "";
}