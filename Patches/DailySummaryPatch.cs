namespace S1AntiCheat.Patches;

internal static class DailySummaryPatch
{
    internal static void Postfix(object __instance)
    {
        PatchContext.Messaging.Register(__instance);
    }
}
