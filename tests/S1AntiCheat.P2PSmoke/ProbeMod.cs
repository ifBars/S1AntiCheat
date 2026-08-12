using System.Collections;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using S1AntiCheat.API;
using S1AntiCheat.API.Authorization;
using S1AntiCheat.API.Peers;
#if MONO
using FishNet;
using FishNet.Connection;
using ScheduleOne.DevUtilities;
using ScheduleOne.Networking;
using ScheduleOne.Persistence;
using ScheduleOne.Persistence.Datas;
using ScheduleOne.PlayerScripts;
using Steamworks;
#else
using Il2CppFishNet;
using Il2CppFishNet.Connection;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppSteamworks;
using SteamManager = Il2Cpp.SteamManager;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(S1AntiCheat.P2PSmoke.ProbeMod), "S1 Anti-Cheat P2P Smoke", "0.1.0", "Bars")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonPriority(int.MaxValue - 100)]

namespace S1AntiCheat.P2PSmoke;

public sealed class ProbeMod : MelonMod
{
    private const string Prefix = "[S1AC-Smoke]";
    private static readonly object EvidenceLock = new();
    private static string _outputDirectory = string.Empty;
    private static string _role = string.Empty;
    private static string _scenario = string.Empty;
    private static ulong _expectedPeerSteamId;
    private static int _timeoutSeconds = 150;
    private static bool _completed;
    private static bool _ownershipReadyWritten;
    private bool _started;

    public override void OnInitializeMelon()
    {
        ParseArguments();
        if (string.IsNullOrWhiteSpace(_outputDirectory) ||
            (_role != "host" && _role != "client") ||
            (_scenario != "clean" && _scenario != "risky" && _scenario != "ownership"))
        {
            LoggerInstance.Error($"{Prefix} Missing or invalid smoke arguments.");
            return;
        }

        Directory.CreateDirectory(_outputDirectory);
        File.WriteAllText(EventPath, string.Empty);
        DeleteIfExists(ResultPath);
        Record("probe_initialized", ("applicationVersion", Application.version));
    }

    public override void OnUpdate()
    {
        if (_started || _completed || string.IsNullOrWhiteSpace(_role))
        {
            return;
        }

        if (!SteamManager.Initialized || !Singleton<Lobby>.InstanceExists || !Singleton<LoadManager>.InstanceExists)
        {
            return;
        }

        _started = true;
        Record(
            "steam_ready",
            ("localSteamId", SteamUser.GetSteamID().m_SteamID),
            ("expectedPeerSteamId", _expectedPeerSteamId),
            ("scene", SceneManager.GetActiveScene().name));
        MelonCoroutines.Start(_role == "host" ? RunHost() : RunClient());
    }

    private static IEnumerator RunHost()
    {
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "Menu", 30, "host_menu_ready");
        if (_completed)
        {
            yield break;
        }

        Record("host_create_lobby_requested");
        Singleton<Lobby>.Instance.CreateLobby();
        yield return WaitFor(
            () => Singleton<Lobby>.Instance.IsInLobby && ReadLobbyId() != 0UL,
            30,
            "host_lobby_ready");
        if (_completed)
        {
            yield break;
        }

        ulong lobbyId = ReadLobbyId();
        if (lobbyId == 0UL)
        {
            Fail("Host lobby ID was unavailable.");
            yield break;
        }

        File.WriteAllText(LobbyReadyPath, $"{SteamUser.GetSteamID().m_SteamID}|{lobbyId}");
        Record("host_lobby_ready", ("lobbyId", lobbyId), ("memberCount", Singleton<Lobby>.Instance.PlayerCount));

        yield return WaitFor(() => Singleton<Lobby>.Instance.PlayerCount >= 2, 60, "host_observed_client_lobby_member");
        if (_completed)
        {
            yield break;
        }

        Record(
            "host_client_joined_before_load",
            ("lobbyId", lobbyId),
            ("memberCount", Singleton<Lobby>.Instance.PlayerCount),
            ("members", string.Join(",", Singleton<Lobby>.Instance.GetLobbyMemberIDs())));

        string savePath = Path.Combine(_outputDirectory, "host-save");
        if (!LoadManager.TryLoadSaveInfo(savePath, -1, out SaveInfo saveInfo, requireGameFile: false))
        {
            Fail($"TryLoadSaveInfo failed for {savePath}.");
            yield break;
        }

        Record("host_start_game", ("savePath", savePath));
        Singleton<LoadManager>.Instance.StartGame(saveInfo, allowLoadStacking: false, allowSaveBackup: false);
        yield return WaitFor(
            () => Singleton<LoadManager>.Instance.IsGameLoaded && InstanceFinder.IsServer && InstanceFinder.IsClient,
            90,
            "host_game_ready");
        if (_completed)
        {
            yield break;
        }

        Record("host_game_ready", ("scene", SceneManager.GetActiveScene().name));
        yield return WaitFor(() => FindRemoteConnection() != null, 75, "host_remote_connection_ready");
        if (_completed)
        {
            yield break;
        }

        AntiCheatHandle antiCheat;
        try
        {
            antiCheat = global::S1AntiCheat.API.AntiCheat.Require(
                "bars.s1anticheat.p2p-smoke",
                new System.Version(0, 1, 0));
        }
        catch (Exception exception)
        {
            Fail($"Anti-cheat dependency failed: {exception.Message}");
            yield break;
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(45);
        DateTime nextPeerRecord = DateTime.MinValue;
        while (!_completed && DateTime.UtcNow < deadline)
        {
            NetworkConnection? connection = FindRemoteConnection();
            if (connection != null && antiCheat.TryGetPeer(connection.ClientId, out AntiCheatPeer peer))
            {
                if (DateTime.UtcNow >= nextPeerRecord)
                {
                    Record(
                        "host_peer_state",
                        ("connectionId", peer.ConnectionId),
                        ("steamId", peer.SteamId),
                        ("admitted", peer.IsAdmitted),
                        ("verified", peer.IsVerified),
                        ("denied", peer.IsDenied));
                    nextPeerRecord = DateTime.UtcNow.AddSeconds(1);
                }

                if (_scenario == "clean" && peer.IsAdmitted && peer.IsVerified && !peer.IsDenied)
                {
                    Pass(peer, "verified");
                    yield break;
                }

                if (_scenario == "ownership" && peer.IsAdmitted && peer.IsVerified && !peer.IsDenied)
                {
                    if (!_ownershipReadyWritten)
                    {
                        File.WriteAllText(OwnershipReadyPath, peer.ConnectionId.ToString(CultureInfo.InvariantCulture));
                        Record("host_ownership_attack_ready", ("connectionId", peer.ConnectionId));
                        _ownershipReadyWritten = true;
                    }

                    yield return null;
                    continue;
                }

                if (_scenario == "risky" && peer.IsDenied)
                {
                    Pass(peer, "denied");
                    yield break;
                }
            }

            yield return null;
        }

        Fail($"Host did not observe the expected {_scenario} peer state.");
    }

    private static IEnumerator RunClient()
    {
        yield return WaitFor(() => File.Exists(LobbyReadyPath), 90, "client_wait_host_lobby");
        if (_completed)
        {
            yield break;
        }

        string[] ready = File.ReadAllText(LobbyReadyPath).Trim().Split('|');
        if (ready.Length != 2 || !ulong.TryParse(ready[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong lobbyId))
        {
            Fail("Client could not parse the host lobby file.");
            yield break;
        }

        Record("client_join_lobby_requested", ("lobbyId", lobbyId));
        SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
        yield return WaitFor(() => Singleton<Lobby>.Instance.IsInLobby, 45, "client_lobby_joined");
        if (_completed)
        {
            yield break;
        }

        Record(
            "client_lobby_joined",
            ("lobbyId", lobbyId),
            ("memberCount", Singleton<Lobby>.Instance.PlayerCount),
            ("members", string.Join(",", Singleton<Lobby>.Instance.GetLobbyMemberIDs())));
        File.WriteAllText(ClientJoinedPath, $"READY|{SteamUser.GetSteamID().m_SteamID}|{lobbyId}");

        yield return WaitFor(
            () => Singleton<LoadManager>.Instance.IsGameLoaded && InstanceFinder.IsClient && Player.Local != null,
            120,
            "client_followed_host_into_game");
        if (_completed)
        {
            yield break;
        }

        Record(
            "client_game_ready",
            ("scene", SceneManager.GetActiveScene().name),
            ("fishNetClient", InstanceFinder.IsClient),
            ("gameLoaded", Singleton<LoadManager>.Instance.IsGameLoaded));

        if (_scenario == "clean")
        {
            WriteResult(
                $"PASS|S1AntiCheat.P2P|Scenario=clean|Role=client|SteamId={SteamUser.GetSteamID().m_SteamID}|" +
                $"Scene={SceneManager.GetActiveScene().name}|GameLoaded=true");
            _completed = true;
            yield break;
        }

        if (_scenario == "ownership")
        {
            yield return WaitFor(() => File.Exists(OwnershipReadyPath), 45, "client_wait_host_verified");
            if (_completed)
            {
                yield break;
            }

            Player? remotePlayer = null;
            foreach (Player player in Player.PlayerList)
            {
                if (player != null && !player.IsLocalPlayer)
                {
                    remotePlayer = player;
                    break;
                }
            }

            if (remotePlayer?.Health == null)
            {
                Fail("Client could not resolve the host player health component for the ownership probe.");
                yield break;
            }

            Record("client_send_non_owner_die", ("target", remotePlayer.PlayerName));
            remotePlayer.Health.SendDie();
            File.WriteAllText(
                OwnershipAttackSentPath,
                SteamUser.GetSteamID().m_SteamID.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static IEnumerator WaitFor(Func<bool> condition, int timeoutSeconds, string phase)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Min(timeoutSeconds, _timeoutSeconds));
        DateTime nextProgress = DateTime.MinValue;
        while (!_completed && DateTime.UtcNow < deadline)
        {
            bool satisfied;
            try
            {
                satisfied = condition();
            }
            catch (Exception exception)
            {
                Fail($"{phase} threw {exception.GetType().Name}: {exception.Message}");
                yield break;
            }

            if (satisfied)
            {
                Record("phase_complete", ("phase", phase));
                yield break;
            }

            if (DateTime.UtcNow >= nextProgress)
            {
                Record("phase_wait", ("phase", phase), ("scene", SceneManager.GetActiveScene().name));
                nextProgress = DateTime.UtcNow.AddSeconds(5);
            }

            yield return null;
        }

        if (!_completed)
        {
            Fail($"Timed out in {phase}.");
        }
    }

    private static NetworkConnection? FindRemoteConnection()
    {
        if (!InstanceFinder.IsServer || InstanceFinder.ServerManager == null)
        {
            return null;
        }

        foreach (NetworkConnection connection in InstanceFinder.ServerManager.Clients.Values)
        {
            if (connection != null &&
                connection.ClientId != 32767 &&
                (_expectedPeerSteamId == 0UL ||
                 connection.GetAddress() == _expectedPeerSteamId.ToString(CultureInfo.InvariantCulture)))
            {
                return connection;
            }
        }

        return null;
    }

    private static ulong ReadLobbyId()
    {
        try
        {
            Lobby lobby = Singleton<Lobby>.Instance;
            if (lobby.LobbyID != 0UL)
            {
                return lobby.LobbyID;
            }

#if IL2CPP
            SteamLobbyService? steamLobbyService = lobby._lobbyService?.TryCast<SteamLobbyService>();
            if (steamLobbyService != null && steamLobbyService._lobbyID != 0UL)
            {
                return steamLobbyService._lobbyID;
            }
#endif

            object? service = AccessTools.Field(typeof(Lobby), "_lobbyService")?.GetValue(lobby);
            if (service == null)
            {
                return 0UL;
            }

            PropertyInfo? property = AccessTools.Property(service.GetType(), "_lobbyID");
            FieldInfo? field = AccessTools.Field(service.GetType(), "_lobbyID");
            object? value = property?.GetValue(service) ?? field?.GetValue(service);
            return value == null ? 0UL : Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0UL;
        }
    }

    private static void Pass(AntiCheatPeer peer, string outcome)
    {
        WriteResult(
            $"PASS|S1AntiCheat.P2P|Scenario={_scenario}|Role=host|Outcome={outcome}|" +
            $"ConnectionId={peer.ConnectionId}|SteamId={peer.SteamId}|Admitted={peer.IsAdmitted}|" +
            $"Verified={peer.IsVerified}|Denied={peer.IsDenied}|Scene={SceneManager.GetActiveScene().name}");
        Record("probe_pass", ("outcome", outcome));
        _completed = true;
    }

    private static void Fail(string reason)
    {
        if (_completed)
        {
            return;
        }

        WriteResult($"FAIL|S1AntiCheat.P2P|Scenario={_scenario}|Role={_role}|Reason={Sanitize(reason)}");
        Record("probe_fail", ("reason", reason));
        _completed = true;
    }

    private static void WriteResult(string result)
    {
        File.WriteAllText(ResultPath, result + Environment.NewLine);
        MelonLogger.Msg($"{Prefix} {result}");
    }

    private static void Record(string eventName, params (string Key, object? Value)[] fields)
    {
        string line = string.Join(
            "|",
            new[]
            {
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                eventName,
                $"role={_role}",
                $"scenario={_scenario}"
            }.Concat(fields.Select(field => $"{field.Key}={Sanitize(field.Value)}")));

        lock (EvidenceLock)
        {
            File.AppendAllText(EventPath, line + Environment.NewLine);
        }

        MelonLogger.Msg($"{Prefix} {line}");
    }

    private static string Sanitize(object? value)
    {
        return (value?.ToString() ?? "null").Replace("|", "%7C").Replace("\r", " ").Replace("\n", " ");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void ParseArguments()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--s1ac-smoke-role" && index + 1 < args.Length)
            {
                _role = args[++index].Trim().ToLowerInvariant();
            }
            else if (args[index] == "--s1ac-smoke-scenario" && index + 1 < args.Length)
            {
                _scenario = args[++index].Trim().ToLowerInvariant();
            }
            else if (args[index] == "--s1ac-smoke-dir" && index + 1 < args.Length)
            {
                _outputDirectory = Path.GetFullPath(args[++index]);
            }
            else if (args[index] == "--s1ac-smoke-peer" && index + 1 < args.Length)
            {
                ulong.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out _expectedPeerSteamId);
            }
            else if (args[index] == "--s1ac-smoke-timeout" && index + 1 < args.Length)
            {
                if (int.TryParse(args[++index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
                {
                    _timeoutSeconds = Math.Max(30, seconds);
                }
            }
        }
    }

    private static string EventPath => Path.Combine(_outputDirectory, $"events-{_role}.txt");

    private static string ResultPath => Path.Combine(_outputDirectory, $"result-{_role}.txt");

    private static string LobbyReadyPath => Path.Combine(_outputDirectory, "lobby-ready.txt");

    private static string ClientJoinedPath => Path.Combine(_outputDirectory, "client-joined.txt");

    private static string OwnershipReadyPath => Path.Combine(_outputDirectory, "ownership-ready.txt");

    private static string OwnershipAttackSentPath => Path.Combine(_outputDirectory, "ownership-attack-sent.txt");
}
