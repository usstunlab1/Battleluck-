using System;
using System.Collections.Generic;

namespace BattleLuck.Commands;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BattleLuckCommandAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public bool AdminOnly { get; }

    /// <summary>Canonical catalog action this command maps to (e.g. "npc.spawn"). Empty for query-only commands.</summary>
    public string ActionId { get; set; } = "";

    /// <summary>Alternative names that trigger the same command.</summary>
    public string[] Aliases { get; set; } = Array.Empty<string>();

    /// <summary>Whether the server console may invoke this command.</summary>
    public bool ConsoleAllowed { get; set; } = true;

    /// <summary>Whether the in-game chat UI may invoke this command.</summary>
    public bool InGameAllowed { get; set; } = true;

    /// <summary>Whether the AI planner may discover and use this command.</summary>
    public bool AiVisible { get; set; } = true;

    /// <summary>Short usage syntax string (e.g. ".npc spawn &lt;prefab&gt; [homeRadius=40]").</summary>
    public string Usage { get; set; } = "";

    public BattleLuckCommandAttribute(string name, string description = "", bool adminOnly = false)
    {
        Name = name ?? "";
        Description = description ?? "";
        AdminOnly = adminOnly;
    }
}
