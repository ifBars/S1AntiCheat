# Integrating another mod

Use the API at two points: require it during initialization, then authorize each incoming action on the listen host.

## Add the dependency

Reference `S1AntiCheat.API.dll` from your project. Do not reference `S1AntiCheat_Mono.dll` or `S1AntiCheat_Il2Cpp.dll`. Those assemblies contain runtime-specific game wrappers.

```xml
<Reference Include="S1AntiCheat.API">
  <HintPath>path\to\S1AntiCheat.API.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Declare the loader dependency and require the minimum runtime version:

```csharp
using MelonLoader;
using S1AntiCheat.API;

[assembly: MelonAdditionalDependencies("S1AntiCheat.API")]

private AntiCheatHandle _antiCheat = null!;

public override void OnInitializeMelon()
{
    _antiCheat = AntiCheat.Require(
        "your-name.your-mod",
        new Version(0, 1, 0));
}
```

Use a stable consumer ID. Changing it resets rate-limit buckets and makes violation logs harder to correlate.

## Keep authorization on the host

FishNet can supply the actual sender connection as the final ServerRpc parameter. Use that object. If ownership is disabled for the RPC, sender validation matters even more.

```csharp
[ServerRpc(RequireOwnership = false)]
private void SubmitTradeServerRpc(
    TradeRequest request,
    NetworkConnection sender = null!)
{
    AntiCheatDecision decision = _antiCheat.Authorize(
        sender.ClientId,
        "trade.submit",
        new AntiCheatActionLimit(5, TimeSpan.FromSeconds(10)));

    if (!decision.Allowed)
    {
        return;
    }

    ProcessTradeOnHost(decision.SteamId, request);
}
```

Do not put the authorization call on the sending client. A modified client can remove it.

FishNet normally limits a ServerRpc to the owner of its `NetworkObject`. If you set `RequireOwnership = false`, accept the sender connection and validate why that sender may act on the target. S1 Anti-Cheat cannot infer your mod's ownership rules from the RPC name.

## Validate the action after authorization

Authorization does not prove that a trade is valid. The host still needs to check:

- The request has a valid shape and bounded sizes.
- The sender owns every offered item or balance.
- The same asset is not reserved by another pending trade.
- The request ID has not already been committed.
- Both sides still satisfy the trade when the host commits it.

Commit the ledger, inventory changes, and escrow release as one host-owned operation. If part of the operation fails, roll it back or leave it pending for recovery.

## Report impossible requests

Use `ReportViolation` when your own state proves that a request is impossible or unauthorized:

```csharp
_antiCheat.ReportViolation(
    sender.ClientId,
    "trade.submit",
    AntiCheatViolationSeverity.ExploitAttempt,
    "The sender offered an item that is not present in the host inventory ledger.");
```

Do not report normal race conditions as exploits. A stale listing or a trade that lost a reservation race should return a normal business error.

## Handle decisions by code

`AntiCheatDecision.Code` is stable integration data. `AntiCheatDecision.Message` is for local diagnostics and may change between releases.

Most handlers can simply stop on any denied decision. If the UI needs a useful response, map the decision code to your own player-facing message.

## Startup behavior

`AntiCheat.Require` throws when:

- The runtime mod has not initialized.
- The installed runtime is older than your minimum version.
- Another runtime did not register correctly.

Let initialization fail. Continuing without the dependency turns a protected protocol into an unprotected one while leaving users with no clear warning.

## Native mutation compatibility

S1 Anti-Cheat blocks direct non-host mod calls into a small set of native cash, inventory, movement, and health methods. If your mod intentionally calls one of those methods on a client, hosts must add your assembly name to `AllowedClientMutationAssemblies`.

Prefer moving the mutation to a host-owned request handler instead. An allowlisted client assembly can still be patched, and the client remains free to lie about its own process.
