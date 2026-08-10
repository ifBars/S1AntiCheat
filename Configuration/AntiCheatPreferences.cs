using MelonLoader;
using S1AntiCheat.API.Models;

namespace S1AntiCheat.Configuration;

internal static class AntiCheatPreferences
{
    internal static MelonPreferences_Entry<bool> EnableAdmissionGate { get; private set; } = null!;
    internal static MelonPreferences_Entry<bool> FailClosedWhenLobbyUnavailable { get; private set; } = null!;
    internal static MelonPreferences_Entry<string> AllowedSteamIds { get; private set; } = null!;
    internal static MelonPreferences_Entry<bool> TrustSteamFriendsInLobby { get; private set; } = null!;
    internal static MelonPreferences_Entry<bool> TrustAllCurrentLobbyMembers { get; private set; } = null!;
    internal static MelonPreferences_Entry<bool> RequireClientAntiCheat { get; private set; } = null!;
    internal static MelonPreferences_Entry<string> VerificationMode { get; private set; } = null!;
    internal static MelonPreferences_Entry<int> VerificationTimeoutSeconds { get; private set; } = null!;
    internal static MelonPreferences_Entry<string> IgnoredModNames { get; private set; } = null!;
    internal static MelonPreferences_Entry<string> DeniedModNames { get; private set; } = null!;
    internal static MelonPreferences_Entry<string> DeniedModHashes { get; private set; } = null!;
    internal static MelonPreferences_Entry<bool> DisconnectOnExploitAttempt { get; private set; } = null!;
    internal static MelonPreferences_Entry<bool> EnableRpcOwnershipGuards { get; private set; } = null!;
    internal static MelonPreferences_Entry<bool> EnableClientMutationGuards { get; private set; } = null!;
    internal static MelonPreferences_Entry<string> AllowedClientMutationAssemblies { get; private set; } = null!;
    internal static MelonPreferences_Entry<bool> LockLobbyWhenGameplayStarts { get; private set; } = null!;

    internal static ModVerificationMode ParsedVerificationMode
    {
        get
        {
            return Enum.TryParse(VerificationMode.Value, true, out ModVerificationMode mode)
                ? mode
                : ModVerificationMode.BlockKnownRisky;
        }
    }

    internal static void Initialize()
    {
        MelonPreferences_Category category = MelonPreferences.CreateCategory("S1AntiCheat");
        EnableAdmissionGate = category.CreateEntry("EnableAdmissionGate", true,
            "Reject remote Steam identities that are not current lobby members or explicitly allowed.");
        FailClosedWhenLobbyUnavailable = category.CreateEntry("FailClosedWhenLobbyUnavailable", true,
            "Reject remote joins when lobby membership cannot be verified.");
        AllowedSteamIds = category.CreateEntry("AllowedSteamIds", string.Empty,
            "Comma-separated SteamID64 values allowed without current lobby membership.");
        TrustSteamFriendsInLobby = category.CreateEntry("TrustSteamFriendsInLobby", true,
            "Allow transport-verified Steam friends who are also in the current lobby.");
        TrustAllCurrentLobbyMembers = category.CreateEntry("TrustAllCurrentLobbyMembers", false,
            "Compatibility mode that allows every current lobby member.");
        RequireClientAntiCheat = category.CreateEntry("RequireClientAntiCheat", true,
            "Require each remote player to answer the S1 Anti-Cheat verification challenge.");
        VerificationMode = category.CreateEntry("VerificationMode", "BlockKnownRisky",
            "Client manifest policy: RequiredOnly, BlockKnownRisky, or MatchHost.");
        VerificationTimeoutSeconds = category.CreateEntry("VerificationTimeoutSeconds", 12,
            "Seconds allowed for a remote player to complete verification.");
        IgnoredModNames = category.CreateEntry("IgnoredModNames", "S1AntiCheat.API,S1AntiCheat_Mono,S1AntiCheat_Il2Cpp",
            "Comma-separated assembly or display names ignored when manifests are compared.");
        DeniedModNames = category.CreateEntry("DeniedModNames", string.Empty,
            "Comma-separated assembly or display names rejected by the host.");
        DeniedModHashes = category.CreateEntry("DeniedModHashes", string.Empty,
            "Comma-separated SHA-256 assembly hashes rejected by the host.");
        DisconnectOnExploitAttempt = category.CreateEntry("DisconnectOnExploitAttempt", true,
            "Disconnect a remote player after a consuming mod reports an exploit attempt.");
        EnableRpcOwnershipGuards = category.CreateEntry("EnableRpcOwnershipGuards", true,
            "Reject sensitive player RPCs when the sender does not own the targeted player object.");
        EnableClientMutationGuards = category.CreateEntry("EnableClientMutationGuards", true,
            "Block direct client mod calls into protected cash, inventory, health, and movement methods.");
        AllowedClientMutationAssemblies = category.CreateEntry("AllowedClientMutationAssemblies", string.Empty,
            "Comma-separated assembly names allowed to call protected client mutation methods.");
        LockLobbyWhenGameplayStarts = category.CreateEntry("LockLobbyWhenGameplayStarts", false,
            "Mark the Steam lobby non-joinable after gameplay starts.");
    }
}
