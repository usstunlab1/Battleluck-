using System.IO;
using System.Text.RegularExpressions;
using BattleLuck.Core.Loaders;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Loads and parses prompt.txt files with YAML frontmatter for AI context injection.
/// </summary>
public sealed class PromptContextLoader
{
    public sealed class PromptContext
    {
        public string EventId { get; set; } = "";
        public List<string> AllowedActions { get; set; } = new();
        public List<string> BlockedActions { get; set; } = new();
        public List<string> AllowedTechs { get; set; } = new();
        public string Narrative { get; set; } = "";
    }

    /// <summary>
    /// Loads a prompt.txt file and extracts YAML frontmatter + narrative.
    /// </summary>
    public PromptContext? Load(string modeId)
    {
        var candidates = new[]
        {
            Path.Combine(ConfigLoader.ConfigRoot, "events", modeId, "prompt.txt"),
            Path.Combine(AppContext.BaseDirectory, "Events", modeId, "prompt.txt"),
            Path.Combine(Environment.CurrentDirectory, "Events", modeId, "prompt.txt")
        };
        var promptPath = candidates.FirstOrDefault(File.Exists);
        if (promptPath == null)
            return null;

        try
        {
            var content = File.ReadAllText(promptPath);
            return Parse(content);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[PromptContextLoader] Failed to load {promptPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses prompt content with YAML frontmatter.
    /// Format:
    /// ---
    /// eventId: bloodbath
    /// allowedActions:
    ///   - action1
    /// blockedActions:
    ///   - action2
    /// ...
    /// ---
    /// Narrative text here.
    /// </summary>
    public PromptContext Parse(string content)
    {
        var context = new PromptContext();

        if (string.IsNullOrWhiteSpace(content))
            return context;

        // Extract YAML frontmatter
        var yamlMatch = Regex.Match(content, @"^---\s*\n(.*?)\n---\s*\n", RegexOptions.Singleline);
        if (!yamlMatch.Success)
        {
            // No frontmatter, treat entire content as narrative
            context.Narrative = content.Trim();
            return context;
        }

        var yamlContent = yamlMatch.Groups[1].Value;
        var narrative = content[yamlMatch.Length..].Trim();

        // Parse key-value pairs
        var lines = yamlContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                continue;

            // Handle list properties
            if (trimmed.EndsWith(":"))
            {
                var listKey = trimmed.TrimEnd(':').Trim().ToLowerInvariant();
                continue;
            }

            if (trimmed.StartsWith("- "))
            {
                var value = trimmed[2..].Trim();
                // Find which list we're in (next preceding key that ends with :)
                continue;
            }

            // Handle key: value pairs
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx > 0)
            {
                var key = trimmed[..colonIdx].Trim().ToLowerInvariant();
                var value = trimmed[(colonIdx + 1)..].Trim();

                switch (key)
                {
                    case "eventid":
                        context.EventId = value;
                        break;
                    case "narrative":
                        context.Narrative = value;
                        break;
                }
            }
        }

        // Parse lists more robustly
        ParseLists(yamlContent, context);

        // If narrative is empty, use the remaining content
        if (string.IsNullOrWhiteSpace(context.Narrative))
            context.Narrative = narrative;

        return context;
    }

    void ParseLists(string yaml, PromptContext context)
    {
        var allowedMatch = Regex.Matches(yaml, @"allowedActions:\s*\n((?:  - .+\n?)+)");
        foreach (Match match in allowedMatch)
        {
            var listContent = match.Groups[1].Value;
            foreach (var line in listContent.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("- "))
                    context.AllowedActions.Add(trimmed[2..].Trim().Trim('"'));
            }
        }

        var blockedMatch = Regex.Matches(yaml, @"blockedActions:\s*\n((?:  - .+\n?)+)");
        foreach (Match match in blockedMatch)
        {
            var listContent = match.Groups[1].Value;
            foreach (var line in listContent.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("- "))
                    context.BlockedActions.Add(trimmed[2..].Trim().Trim('"'));
            }
        }

        var techsMatch = Regex.Matches(yaml, @"allowedTechs:\s*\n((?:  - .+\n?)+)");
        foreach (Match match in techsMatch)
        {
            var listContent = match.Groups[1].Value;
            foreach (var line in listContent.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("- "))
                    context.AllowedTechs.Add(trimmed[2..].Trim().Trim('"'));
            }
        }
    }

    /// <summary>
    /// Builds a prompt string for AI from context and runtime state.
    /// </summary>
    public string BuildPrompt(PromptContext context, RuntimeActionContext runtimeContext)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"Event: {context.EventId}");
        sb.AppendLine();

        if (context.AllowedActions?.Count > 0)
        {
            sb.AppendLine("Allowed Actions:");
            foreach (var action in context.AllowedActions)
                sb.AppendLine($"  - {action}");
            sb.AppendLine();
        }

        if (context.BlockedActions?.Count > 0)
        {
            sb.AppendLine("Blocked Actions:");
            foreach (var action in context.BlockedActions)
                sb.AppendLine($"  - {action}");
            sb.AppendLine();
        }

        if (context.AllowedTechs?.Count > 0)
        {
            sb.AppendLine("Allowed Techs:");
            foreach (var tech in context.AllowedTechs)
                sb.AppendLine($"  - {tech}");
            sb.AppendLine();
        }

        sb.AppendLine("Narrative:");
        sb.AppendLine(context.Narrative);

        if (runtimeContext.ZoneDefinition != null)
        {
            sb.AppendLine();
            sb.AppendLine($"Current Zone: {runtimeContext.ZoneDefinition.Name}");
            sb.AppendLine($"Zone Coordinates: ({runtimeContext.ZoneDefinition.Position.X}, {runtimeContext.ZoneDefinition.Position.Y}, {runtimeContext.ZoneDefinition.Position.Z})");
            sb.AppendLine($"Zone Radius: {runtimeContext.ZoneDefinition.Radius}");
        }

        return sb.ToString();
    }
}
