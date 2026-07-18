namespace BattleLuck.Commands;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BattleLuckCommandAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public bool AdminOnly { get; }

    public BattleLuckCommandAttribute(string name, string description = "", bool adminOnly = false)
    {
        Name = name ?? "";
        Description = description ?? "";
        AdminOnly = adminOnly;
    }
}
