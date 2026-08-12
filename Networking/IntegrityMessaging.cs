using S1AntiCheat.Bootstrap;
using MelonLoader;
#if MONO
using Channel = FishNet.Transporting.Channel;
using ClientRpcDelegate = FishNet.Object.Delegating.ClientRpcDelegate;
using DataOrderType = FishNet.Object.DataOrderType;
using NetworkBehaviour = FishNet.Object.NetworkBehaviour;
using NetworkConnection = FishNet.Connection.NetworkConnection;
using PooledReader = FishNet.Serializing.PooledReader;
using PooledWriter = FishNet.Serializing.PooledWriter;
using Reader = FishNet.Serializing.Reader;
using ServerRpcDelegate = FishNet.Object.Delegating.ServerRpcDelegate;
using Writer = FishNet.Serializing.Writer;
using WriterPool = FishNet.Serializing.WriterPool;
#else
using Channel = Il2CppFishNet.Transporting.Channel;
using ClientRpcDelegate = Il2CppFishNet.Object.Delegating.ClientRpcDelegate;
using DataOrderType = Il2CppFishNet.Object.DataOrderType;
using NetworkBehaviour = Il2CppFishNet.Object.NetworkBehaviour;
using NetworkConnection = Il2CppFishNet.Connection.NetworkConnection;
using PooledReader = Il2CppFishNet.Serializing.PooledReader;
using PooledWriter = Il2CppFishNet.Serializing.PooledWriter;
using Reader = Il2CppFishNet.Serializing.Reader;
using ServerRpcDelegate = Il2CppFishNet.Object.Delegating.ServerRpcDelegate;
using Writer = Il2CppFishNet.Serializing.Writer;
using WriterPool = Il2CppFishNet.Serializing.WriterPool;
#endif

namespace S1AntiCheat.Networking;

internal sealed class IntegrityMessaging
{
    private const uint MessageId = 211u;

    private NetworkBehaviour? _networkBehaviour;

    internal event Action<string>? ClientMessageReceived;

    internal event Action<NetworkConnection, string>? ServerMessageReceived;

    internal bool IsReady => _networkBehaviour?.IsSpawned == true;

    internal void Register(object instance)
    {
        try
        {
            var networkBehaviour = (NetworkBehaviour)instance;
            networkBehaviour.RegisterTargetRpc(MessageId, CreateClientDelegate());
            networkBehaviour.RegisterServerRpc(MessageId, CreateServerDelegate());
            _networkBehaviour = networkBehaviour;
            MelonLogger.Msg($"{ModInfo.LogPrefix} Registered verification messaging on DailySummary.");
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"{ModInfo.LogPrefix} Could not register verification messaging: {exception}");
        }
    }

    internal bool SendToServer(string payload)
    {
        NetworkBehaviour? networkBehaviour = _networkBehaviour;
        if (networkBehaviour?.IsSpawned != true)
        {
            return false;
        }

        PooledWriter writer = WriterPool.Retrieve();
        try
        {
            ((Writer)writer).WriteString(payload);
            networkBehaviour.SendServerRpc(MessageId, writer, Channel.Reliable, DataOrderType.Default);
            return true;
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"{ModInfo.LogPrefix} Could not send verification report: {exception.Message}");
            return false;
        }
        finally
        {
            writer.Store();
        }
    }

    internal bool SendToClient(NetworkConnection connection, string payload)
    {
        NetworkBehaviour? networkBehaviour = _networkBehaviour;
        if (networkBehaviour?.IsSpawned != true || connection == null)
        {
            return false;
        }

        PooledWriter writer = WriterPool.Retrieve();
        try
        {
            ((Writer)writer).WriteString(payload);
            networkBehaviour.SendTargetRpc(
                MessageId,
                writer,
                Channel.Reliable,
                DataOrderType.Default,
                connection,
                false,
                true);
            return true;
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"{ModInfo.LogPrefix} Could not send verification challenge: {exception.Message}");
            return false;
        }
        finally
        {
            writer.Store();
        }
    }

    internal void Clear()
    {
        _networkBehaviour = null;
    }

    private ClientRpcDelegate CreateClientDelegate()
    {
#if MONO
        return new ClientRpcDelegate(OnClientMessage);
#else
        return (ClientRpcDelegate)new Action<PooledReader, Channel>(OnClientMessage);
#endif
    }

    private ServerRpcDelegate CreateServerDelegate()
    {
#if MONO
        return new ServerRpcDelegate(OnServerMessage);
#else
        return (ServerRpcDelegate)new Action<PooledReader, Channel, NetworkConnection>(OnServerMessage);
#endif
    }

    private void OnClientMessage(PooledReader reader, Channel channel)
    {
        try
        {
            ClientMessageReceived?.Invoke(((Reader)reader).ReadString());
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"{ModInfo.LogPrefix} Invalid host verification message: {exception.Message}");
        }
    }

    private void OnServerMessage(PooledReader reader, Channel channel, NetworkConnection connection)
    {
        try
        {
            ServerMessageReceived?.Invoke(connection, ((Reader)reader).ReadString());
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"{ModInfo.LogPrefix} Invalid client verification message: {exception.Message}");
        }
    }
}
