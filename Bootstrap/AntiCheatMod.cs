using MelonLoader;
using S1AntiCheat.Configuration;
using S1AntiCheat.Networking;
using S1AntiCheat.Patches;
using S1AntiCheat.Runtime;
using S1AntiCheat.Verification;

[assembly: MelonInfo(
    typeof(S1AntiCheat.Bootstrap.AntiCheatMod),
    S1AntiCheat.Bootstrap.ModInfo.Name,
    S1AntiCheat.Bootstrap.ModInfo.Version,
    S1AntiCheat.Bootstrap.ModInfo.Author)]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonPriority(int.MinValue + 50)]

namespace S1AntiCheat.Bootstrap;

public sealed class AntiCheatMod : MelonMod
{
    private ConnectionRegistry? _connections;
    private IntegrityMessaging? _messaging;
    private VerificationService? _verification;
    private AntiCheatRuntimeService? _runtime;
    private bool _lobbyLockPending;
    private int _lobbyLockAttemptsRemaining;
    private float _nextLobbyLockAttempt;

    public override void OnInitializeMelon()
    {
        try
        {
            AntiCheatPreferences.Initialize();
            _connections = new ConnectionRegistry();
            _connections.Reset(AntiCheatPreferences.AllowedSteamIds.Value);
            _messaging = new IntegrityMessaging();
            PatchContext.Initialize(_connections, _messaging);
            PatchInstaller.Install(HarmonyInstance);

            _runtime = new AntiCheatRuntimeService(_connections);
            _verification = new VerificationService(_connections, _messaging, new ModManifestService());
            API.AntiCheat.RegisterRuntime(_runtime);

            LoggerInstance.Msg(
                $"{ModInfo.Name} {ModInfo.Version} initialized. " +
                $"ClientVerification={AntiCheatPreferences.RequireClientAntiCheat.Value}, " +
                $"ManifestPolicy={AntiCheatPreferences.ParsedVerificationMode}.");
        }
        catch (Exception exception)
        {
            LoggerInstance.Error($"Initialization failed; integrations will remain disabled. {exception}");
            throw;
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (!AntiCheatPreferences.LockLobbyWhenGameplayStarts.Value ||
            !string.Equals(sceneName, "Main", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sceneName, "Tutorial", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lobbyLockPending = true;
        _lobbyLockAttemptsRemaining = 10;
        _nextLobbyLockAttempt = 0f;
    }

    public override void OnUpdate()
    {
        _connections?.FlushPendingDisconnects();
        _verification?.Tick();
        TickLobbyLock();
    }

    public override void OnApplicationQuit()
    {
        _verification?.Dispose();
        _messaging?.Clear();
        if (_runtime != null)
        {
            API.AntiCheat.UnregisterRuntime(_runtime);
            _runtime.Clear();
        }

        _connections?.Clear();
    }

    private void TickLobbyLock()
    {
        if (!_lobbyLockPending || UnityEngine.Time.unscaledTime < _nextLobbyLockAttempt)
        {
            return;
        }

        _nextLobbyLockAttempt = UnityEngine.Time.unscaledTime + 1f;
        _lobbyLockPending = !LobbyAccess.TryLockCurrentLobby();
        _lobbyLockAttemptsRemaining--;
        if (_lobbyLockAttemptsRemaining <= 0)
        {
            _lobbyLockPending = false;
            MelonLogger.Warning($"{ModInfo.LogPrefix} Could not resolve the current lobby ID to lock late joins.");
        }
    }
}
