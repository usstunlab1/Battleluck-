using BattleLuck.Models;
using BattleLuck.Services.Flow;
using BattleLuck.Services.Runtime;
using HarmonyLib;

namespace BattleLuck.Patches;

/// <summary>
/// Extends the existing action runtime without adding another executor. The
/// original FlowActionExecutor still owns parsing, logging, policy, and reports;
/// this patch supplies handlers for the request-scoped effect action family.
/// </summary>
[HarmonyPatch]
public static class RuntimeEffectActionPatch
{
    [HarmonyPatch(typeof(FlowActionExecutor), "IsRegisteredAction")]
    [HarmonyPostfix]
    static void IsRegisteredActionPostfix(string actionName, ref bool __result)
    {
        if (!__result && RuntimeEffectActionCatalog.IsRuntimeEffectAction(actionName))
            __result = true;
    }

    [HarmonyPatch(typeof(FlowActionExecutor), "ExecuteParsed")]
    [HarmonyPrefix]
    static bool ExecuteParsedPrefix(
        string actionName,
        Dictionary<string, string> p,
        FlowActionContext c,
        ref OperationResult __result)
    {
        if (!RuntimeEffectActionCatalog.IsRuntimeEffectAction(actionName))
            return true;

        __result = RuntimeEffectActionService.Execute(actionName, p, c);
        return false;
    }
}
