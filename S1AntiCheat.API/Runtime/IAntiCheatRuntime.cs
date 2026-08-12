using S1AntiCheat.API.Authorization;
using S1AntiCheat.API.Peers;
using S1AntiCheat.API.Violations;

namespace S1AntiCheat.API.Runtime;

internal interface IAntiCheatRuntime
{
    Version Version { get; }

    bool IsHostProtectionActive { get; }

    AntiCheatDecision Authorize(
        string consumerId,
        int connectionId,
        string capability,
        AntiCheatActionLimit actionLimit);

    bool TryGetPeer(int connectionId, out AntiCheatPeer peer);

    void ReportViolation(
        string consumerId,
        int connectionId,
        string capability,
        AntiCheatViolationSeverity severity,
        string reason);
}
