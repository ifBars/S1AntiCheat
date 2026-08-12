using S1AntiCheat.API;
using S1AntiCheat.API.Authorization;
using S1AntiCheat.API.Peers;
using S1AntiCheat.API.Runtime;
using S1AntiCheat.API.Verification;
using S1AntiCheat.API.Violations;

namespace S1AntiCheat.ContractVerifier;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyDependencyContract();
            VerifyAdmissionPolicy();
            VerifyManifestPolicy();
            VerifyRateLimiter();
            Console.WriteLine("PASS|S1AntiCheat.ContractVerifier|4 groups");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL|S1AntiCheat.ContractVerifier|{exception}");
            return 1;
        }
    }

    private static void VerifyDependencyContract()
    {
        Throws<AntiCheatUnavailableException>(() => AntiCheat.Require("bars.trade-network", new Version(0, 1, 0)));

        var oldRuntime = new FakeRuntime(new Version(0, 0, 9));
        AntiCheat.RegisterRuntime(oldRuntime);
        Throws<AntiCheatUnavailableException>(() => AntiCheat.Require("bars.trade-network", new Version(0, 1, 0)));
        AntiCheat.UnregisterRuntime(oldRuntime);

        var runtime = new FakeRuntime(new Version(0, 1, 0));
        AntiCheat.RegisterRuntime(runtime);
        AntiCheatHandle handle = AntiCheat.Require(" Bars.Trade-Network ", new Version(0, 1, 0));
        Equal("bars.trade-network", handle.ConsumerId, "consumer normalization");
        AntiCheatDecision decision = handle.Authorize(
            7,
            "trade.submit",
            new AntiCheatActionLimit(5, TimeSpan.FromSeconds(10)));
        True(decision.Allowed, "runtime authorization");
        Equal("bars.trade-network", runtime.LastConsumerId, "consumer forwarding");
        AntiCheat.UnregisterRuntime(runtime);
    }

    private static void VerifyAdmissionPolicy()
    {
        var allowlist = new HashSet<ulong> { 76561198000000001UL };
        var denylist = new HashSet<ulong>();
        AdmissionDecision allowed = AdmissionPolicy.Evaluate(
            3,
            "76561198000000001",
            76561198000000002UL,
            true,
            Array.Empty<string>(),
            false,
            true,
            false,
            allowlist,
            denylist,
            true);
        True(allowed.Allowed, "explicit allowlist");
        Equal(AdmissionReason.ExplicitAllowlist, allowed.Reason, "allowlist reason");

        AdmissionDecision rejected = AdmissionPolicy.Evaluate(
            4,
            "not-a-steam-id",
            76561198000000002UL,
            true,
            Array.Empty<string>(),
            false,
            true,
            false,
            allowlist,
            denylist,
            true);
        True(!rejected.Allowed, "invalid transport identity");
        Equal(AdmissionReason.InvalidTransportIdentity, rejected.Reason, "invalid identity reason");
    }

    private static void VerifyManifestPolicy()
    {
        var riskySamples = new[]
        {
            new ModDescriptor("CDXX", "CDXX", "1.6.4", "RobbyMRDR", "56b67630b08fe2a253fe2fdc7ead7e7d049da09c74447d5043fb3d594836d8de"),
            new ModDescriptor("LegacyBlazesMenu", "Legacy Blaze's Menu", "4.3.4.6", "LegacyBlaze", "0eabd1723f3151449aebc1f4ff09f2db88fe8c29a61729c8d70a443116322b68"),
            new ModDescriptor("Modern_Cheat_Menu", "Modern Cheat Menu", "2.0.6", "darkness", "c88933a451e7b7c1a96a10093f89a222ba414899311ee088264eebc97e9c9e7e"),
            new ModDescriptor("NastyMod v2", "NastyMod", "2.0.0", "nasty.codes", "85b64ace2294418cf9b6b33263e3b9b0e567938d983f5bd6005c34e38335a745"),
            new ModDescriptor("NugzzMenu", "NugzzMenu", "0.9.9R4", "XUnfairX", "38a838a417003f71edd1c815afd5da474c7547f751b629a3ff66a23bff253d39"),
            new ModDescriptor("UltimateModMenu", "Ultimate Mod Menu", "3.0", "UnknownGlitcha", "3c5d9a073fbf0054dcb008cba9d708129aaefc7130efc62b864bc1e50f377a92"),
            new ModDescriptor("UnityExplorer", "UnityExplorer", "4.9.0", string.Empty, "abc")
        };

        foreach (ModDescriptor risky in riskySamples)
        {
            ModVerificationResult blocked = ModVerificationPolicy.Evaluate(
                new[] { risky },
                ModVerificationMode.BlockKnownRisky,
                string.Empty,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            True(!blocked.Allowed, $"known risky mod {risky.AssemblyName}");
        }

        var renamedKnownBuild = new ModDescriptor(
            "HarmlessLookingName",
            "Harmless Looking Name",
            "1.0.0",
            string.Empty,
            "c88933a451e7b7c1a96a10093f89a222ba414899311ee088264eebc97e9c9e7e");
        ModVerificationResult blockedByHash = ModVerificationPolicy.Evaluate(
            new[] { renamedKnownBuild },
            ModVerificationMode.BlockKnownRisky,
            string.Empty,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        True(!blockedByHash.Allowed, "renamed known risky hash");

        var host = new[] { new ModDescriptor("TradeNetwork", "Trade Network", "1.0.0", "Bars", "1234") };
        string hostFingerprint = ModVerificationPolicy.ComputeFingerprint(host);
        ModVerificationResult mismatch = ModVerificationPolicy.Evaluate(
            Array.Empty<ModDescriptor>(),
            ModVerificationMode.MatchHost,
            hostFingerprint,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        True(!mismatch.Allowed, "host manifest mismatch");
    }

    private static void VerifyRateLimiter()
    {
        var limiter = new ActionRateLimiter();
        DateTime now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var limit = new AntiCheatActionLimit(2, TimeSpan.FromSeconds(10));
        True(limiter.TryAcquire("bars.trade-network", 8, "trade.submit", limit, now), "first rate-limit action");
        True(limiter.TryAcquire("bars.trade-network", 8, "trade.submit", limit, now), "second rate-limit action");
        True(!limiter.TryAcquire("bars.trade-network", 8, "trade.submit", limit, now), "third rate-limit action");
        True(limiter.TryAcquire("bars.trade-network", 8, "trade.submit", limit, now.AddSeconds(10)), "reset rate-limit window");
    }

    private static void True(bool value, string name)
    {
        if (!value)
        {
            throw new InvalidOperationException($"Assertion failed: {name}.");
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Assertion failed: {name}. Expected {expected}; got {actual}.");
        }
    }

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class FakeRuntime : IAntiCheatRuntime
    {
        internal FakeRuntime(Version version)
        {
            Version = version;
        }

        public Version Version { get; }

        public bool IsHostProtectionActive => true;

        internal string LastConsumerId { get; private set; } = string.Empty;

        public AntiCheatDecision Authorize(
            string consumerId,
            int connectionId,
            string capability,
            AntiCheatActionLimit actionLimit)
        {
            LastConsumerId = consumerId;
            return new AntiCheatDecision(true, AntiCheatDecisionCode.Allowed, 0UL, "Allowed by fake runtime.");
        }

        public bool TryGetPeer(int connectionId, out AntiCheatPeer peer)
        {
            peer = default;
            return false;
        }

        public void ReportViolation(
            string consumerId,
            int connectionId,
            string capability,
            AntiCheatViolationSeverity severity,
            string reason)
        {
        }
    }
}
