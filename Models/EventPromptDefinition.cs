namespace BattleLuck.Models;

/// <summary>
/// Parsed frontmatter from an Events/&lt;modeId&gt;/prompt.txt file.
/// Captures the machine-readable directives (allowed/blocked actions, allowed techs)
/// that the AI pipeline and Phase 6/7 validation consume, separate from the
/// human-readable narrative body.
/// </summary>
public sealed class EventPromptDefinition
{
    public string EventId { get; set; } = "";
    public List<string> AllowedActions { get; set; } = new();
    public List<string> BlockedActions { get; set; } = new();
    public List<string> AllowedTechs { get; set; } = new();
    public string Body { get; set; } = "";
}
