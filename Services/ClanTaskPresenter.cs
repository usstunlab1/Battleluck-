using System.Text;
using BattleLuck.Models;

namespace BattleLuck.Services;

public static class ClanTaskPresenter
{
    public const int PageSize = 5;

    public static string BuildPage(IReadOnlyList<ClanTask> tasks, int requestedPage)
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(tasks.Count / (double)PageSize));
        var page = Math.Clamp(requestedPage, 1, pageCount);
        var selected = tasks.Skip((page - 1) * PageSize).Take(PageSize).ToList();
        var sb = new StringBuilder(500);
        sb.Append("<color=#66E3FF>[CLAN ACTIVITY] ").Append(page).Append('/').Append(pageCount).AppendLine("</color>");
        if (selected.Count == 0)
            sb.Append("No active clan tasks.");
        foreach (var task in selected)
        {
            var scope = task.Scope == ClanTaskScope.Event ? $"EVENT:{task.EventId}" : "WORLD";
            sb.Append("<color=#FFD166>[").Append(scope).Append("]</color> ")
              .Append(task.Description).Append(" — ")
              .Append(task.CurrentAmount).Append('/').Append(task.TargetAmount);
            if (task.AssignedSteamIds.Count > 0)
                sb.Append(" (assigned)");
            sb.AppendLine();
        }
        if (pageCount > 1) sb.Append("Use .ac ").Append(page + 1 > pageCount ? 1 : page + 1).Append(" for the next page.");
        return ClampUtf8(sb.ToString().TrimEnd(), 500);
    }

    static string ClampUtf8(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;
        while (value.Length > 0 && Encoding.UTF8.GetByteCount(value) > maxBytes - 3) value = value[..^1];
        return value + "...";
    }
}
