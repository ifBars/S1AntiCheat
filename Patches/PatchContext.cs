using S1AntiCheat.Networking;
using S1AntiCheat.Runtime;

namespace S1AntiCheat.Patches;

internal static class PatchContext
{
    internal static ConnectionRegistry Connections { get; private set; } = null!;

    internal static IntegrityMessaging Messaging { get; private set; } = null!;

    internal static void Initialize(ConnectionRegistry connections, IntegrityMessaging messaging)
    {
        Connections = connections;
        Messaging = messaging;
    }
}
