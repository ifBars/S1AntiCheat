using S1AntiCheat.Bootstrap;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
#if MONO
using RemoteConnectionStateArgs = FishNet.Transporting.RemoteConnectionStateArgs;
using ServerManager = FishNet.Managing.Server.ServerManager;
#else
using RemoteConnectionStateArgs = Il2CppFishNet.Transporting.RemoteConnectionStateArgs;
using ServerManager = Il2CppFishNet.Managing.Server.ServerManager;
#endif

namespace S1AntiCheat.Patches;

internal static class PatchInstaller
{
#if MONO
    private const string DailySummaryTypeName = "ScheduleOne.UI.DailySummary";
    private const string ConsoleTypeName = "ScheduleOne.Console";
    private const string PlayerTypeName = "ScheduleOne.PlayerScripts.Player";
    private const string PlayerHealthTypeName = "ScheduleOne.PlayerScripts.Health.PlayerHealth";
    private const string PlayerInventoryTypeName = "ScheduleOne.PlayerScripts.PlayerInventory";
    private const string PlayerMovementTypeName = "ScheduleOne.PlayerScripts.PlayerMovement";
    private const string MoneyManagerTypeName = "ScheduleOne.Money.MoneyManager";
#else
    private const string DailySummaryTypeName = "Il2CppScheduleOne.UI.DailySummary";
    private const string ConsoleTypeName = "Il2CppScheduleOne.Console";
    private const string PlayerTypeName = "Il2CppScheduleOne.PlayerScripts.Player";
    private const string PlayerHealthTypeName = "Il2CppScheduleOne.PlayerScripts.Health.PlayerHealth";
    private const string PlayerInventoryTypeName = "Il2CppScheduleOne.PlayerScripts.PlayerInventory";
    private const string PlayerMovementTypeName = "Il2CppScheduleOne.PlayerScripts.PlayerMovement";
    private const string MoneyManagerTypeName = "Il2CppScheduleOne.Money.MoneyManager";
#endif

    private static readonly (string TypeName, string MethodName)[] ClientMutationMethods =
    {
        (MoneyManagerTypeName, "ChangeCashBalance"),
        (MoneyManagerTypeName, "CreateOnlineTransaction"),
        (PlayerInventoryTypeName, "AddItemToInventory"),
        (PlayerMovementTypeName, "Teleport"),
        (PlayerHealthTypeName, "SetHealth"),
        (PlayerHealthTypeName, "RecoverHealth"),
        (PlayerHealthTypeName, "Revive")
    };

    private static readonly (string TypeName, string MethodName)[] OwnershipGuardedRpcReaders =
    {
        (PlayerHealthTypeName, "RpcReader___Server_SendDie_2166136261"),
        (PlayerHealthTypeName, "RpcReader___Server_SendRevive_3848837105"),
        (PlayerTypeName, "RpcReader___Server_set_CameraPosition_4276783012"),
        (PlayerTypeName, "RpcReader___Server_set_CameraRotation_3429297120"),
        (PlayerTypeName, "RpcReader___Server_RequestSavePlayer_2166136261"),
        (PlayerTypeName, "RpcReader___Server_SetFlashlightOn_Server_1140765316"),
        (PlayerTypeName, "RpcReader___Server_SendCrouched_1140765316"),
        (PlayerTypeName, "RpcReader___Server_SendEquippable_Networked_3615296227"),
        (PlayerTypeName, "RpcReader___Server_SendConsumeProduct_2622925554"),
        (PlayerTypeName, "RpcReader___Server_SendValue_3589193952"),
        (PlayerTypeName, "RpcReader___Server_SendWorldSpaceDialogue_606697822")
    };

    private static readonly string[] SensitiveConsoleCommands =
    {
        "AddItemToInventoryCommand",
        "ClearInventoryCommand",
        "ChangeCashCommand",
        "ChangeOnlineBalanceCommand",
        "SetMoveSpeedCommand",
        "SetJumpMultiplier",
        "SetPropertyOwned",
        "Teleport",
        "PackageProduct",
        "SetStaminaReserve",
        "RaisedWanted",
        "LowerWanted",
        "ClearWanted",
        "SetHealth",
        "SetTimeScale",
        "SetVariableValue",
        "SetQuestState",
        "SetQuestEntryState",
        "SetEmotion",
        "SetUnlocked",
        "SetRelationship",
        "AddEmployeeCommand",
        "SetDiscovered",
        "GrowPlants",
        "SetLawIntensity",
        "SetQuality",
        "SetQuantity",
        "GiveXP",
        "Disable",
        "Enable",
        "EndTutorial",
        "SetGravityMultiplier",
        "SetRegionUnlocked",
        "ForceSleep",
        "SetPoliceIgnorePlayers"
    };

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        MethodInfo admissionMethod = AccessTools.Method(
            typeof(ServerManager),
            "Transport_OnRemoteConnectionState",
            new[] { typeof(RemoteConnectionStateArgs) }) ??
            throw new MissingMethodException(typeof(ServerManager).FullName, "Transport_OnRemoteConnectionState");
        harmony.Patch(
            admissionMethod,
            prefix: HarmonyMethod(typeof(ServerAdmissionPatch), nameof(ServerAdmissionPatch.Prefix)));

        Type dailySummaryType = RequireType(DailySummaryTypeName);
        MethodInfo dailySummaryAwake = AccessTools.Method(dailySummaryType, "Awake", Type.EmptyTypes) ??
            throw new MissingMethodException(dailySummaryType.FullName, "Awake");
        harmony.Patch(
            dailySummaryAwake,
            postfix: HarmonyMethod(typeof(DailySummaryPatch), nameof(DailySummaryPatch.Postfix)));

        int consoleMethods = InstallConsoleGuards(harmony);
        int rpcReaders = InstallRpcOwnershipGuards(harmony);
        int mutationMethods = InstallClientMutationGuards(harmony);
        MelonLogger.Msg(
            $"{ModInfo.LogPrefix} Installed admission, verification, {consoleMethods} console guards, " +
            $"{rpcReaders} RPC ownership guards, and {mutationMethods} client mutation guards.");
    }

    private static int InstallClientMutationGuards(HarmonyLib.Harmony harmony)
    {
        HarmonyMethod guard = HarmonyMethod(typeof(ClientMutationGuardPatch), nameof(ClientMutationGuardPatch.Prefix));
        int installed = 0;
        foreach ((string typeName, string methodName) in ClientMutationMethods)
        {
            Type declaringType = RequireType(typeName);
            MethodInfo[] methods = AccessTools.GetDeclaredMethods(declaringType)
                .Where(candidate => candidate.Name == methodName)
                .ToArray();
            if (methods.Length == 0)
            {
                throw new MissingMethodException(declaringType.FullName, methodName);
            }

            foreach (MethodInfo method in methods)
            {
                harmony.Patch(method, prefix: guard);
                installed++;
            }
        }

        return installed;
    }

    private static int InstallRpcOwnershipGuards(HarmonyLib.Harmony harmony)
    {
        HarmonyMethod guard = HarmonyMethod(typeof(SensitiveRpcOwnershipPatch), nameof(SensitiveRpcOwnershipPatch.Prefix));
        foreach ((string typeName, string methodName) in OwnershipGuardedRpcReaders)
        {
            Type declaringType = RequireType(typeName);
            MethodInfo method = AccessTools.GetDeclaredMethods(declaringType)
                .SingleOrDefault(candidate => candidate.Name == methodName) ??
                throw new MissingMethodException(declaringType.FullName, methodName);
            harmony.Patch(method, prefix: guard);
        }

        return OwnershipGuardedRpcReaders.Length;
    }

    private static int InstallConsoleGuards(HarmonyLib.Harmony harmony)
    {
        Type consoleType = RequireType(ConsoleTypeName);
        HarmonyMethod guard = HarmonyMethod(typeof(ConsoleGuardPatch), nameof(ConsoleGuardPatch.Prefix));
        MethodInfo[] submitMethods = AccessTools.GetDeclaredMethods(consoleType)
            .Where(method => method.Name == "SubmitCommand")
            .ToArray();
        if (submitMethods.Length == 0)
        {
            throw new MissingMethodException(consoleType.FullName, "SubmitCommand");
        }

        foreach (MethodInfo submitMethod in submitMethods)
        {
            harmony.Patch(submitMethod, prefix: guard);
        }

        int installed = submitMethods.Length;
        foreach (string commandName in SensitiveConsoleCommands)
        {
            Type commandType = AccessTools.Inner(consoleType, commandName) ??
                RequireType($"{ConsoleTypeName}.{commandName}");
            MethodInfo execute = AccessTools.GetDeclaredMethods(commandType)
                .SingleOrDefault(method => method.Name == "Execute") ??
                throw new MissingMethodException(commandType.FullName, "Execute");
            harmony.Patch(execute, prefix: guard);
            installed++;
        }

        return installed;
    }

    private static Type RequireType(string typeName)
    {
        return AccessTools.TypeByName(typeName) ?? throw new TypeLoadException($"Required game type {typeName} was not found.");
    }

    private static HarmonyMethod HarmonyMethod(Type type, string methodName)
    {
        MethodInfo method = AccessTools.Method(type, methodName) ??
            throw new MissingMethodException(type.FullName, methodName);
        return new HarmonyMethod(method);
    }
}
