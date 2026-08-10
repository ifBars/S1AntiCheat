# S1 Anti-Cheat

S1 Anti-Cheat adds host-side checks to normal Schedule I friend lobbies. It is for players who want a stricter lobby and mod authors who need a shared trust gate for features such as player trading.

The listen host is the authority. Remote players must pass Steam lobby admission and a client verification handshake before protected mod actions are accepted.

This is not a kernel anti-cheat, and it cannot make a hostile client process trustworthy. A cheat mod can patch managed code, forge its manifest, or skip a client hook. The host-side checks are the part that matter.

## What it currently does

- Rejects transport identities that are not allowed by the active Steam lobby policy.
- Challenges each remote player through a custom FishNet RPC.
- Checks the reported client mod manifest against the host's selected policy.
- Blocks the native game console and selected state-changing console commands on non-host clients.
- Blocks direct client-mod calls into protected cash, online-balance, inventory, teleport, and health methods.
- Enforces ownership on selected player RPC readers that the base game exposes with `RequireOwnership = false`.
- Gives other mods a small API for dependency checks, peer verification, rate limits, and violation reports.
- Builds separately for the Mono and IL2CPP versions of Schedule I.

The method guards matter because menu filenames and hashes change. They cover the game calls found in the supplied menu samples, including `MoneyManager.ChangeCashBalance`, `PlayerInventory.AddItemToInventory`, `PlayerMovement.Teleport`, `PlayerHealth` mutations, console command execution, and non-owner player RPCs.

The manifest check is useful friction, especially for casual friend lobbies. It is still self-reported by the client. The client mutation guards are also deterrence because a hostile process can patch them out. The RPC ownership guards and protected mod handlers are stronger because the listen host makes those decisions.

See [method coverage](docs/method-coverage.md) for the exact boundary and the game calls that still need consumer-specific validation.

## Project layout

- `S1AntiCheat.API` contains the loader-independent contract, models, and policy services.
- `S1AntiCheat` contains the MelonLoader, FishNet, Steam lobby, and Harmony adapters.
- `tests/S1AntiCheat.ContractVerifier` exercises the dependency contract and pure policy code.
- `tests/Verify-GameSurface.ps1` checks the exact game and FishNet seams used by both runtime builds.
- `tests/S1AntiCheat.P2PSmoke` and `tests/Run-GseP2PSmoke.ps1` exercise real two-process listen-host sessions.

The split follows the same basic shape as MLVScan and MLVScan.Core: policy stays separate from the loader-specific integration.

## Install

Every player in the lobby needs both files for their game runtime:

1. Put `S1AntiCheat.API.dll` in `UserLibs`.
2. Put `S1AntiCheat_Mono.dll` or `S1AntiCheat_Il2Cpp.dll` in `Mods`.
3. Start the game once to create the `S1AntiCheat` entries in `MelonPreferences.cfg`.

Do not install both runtime DLLs. Mono players use the Mono build. Public IL2CPP players use the IL2CPP build.

The default manifest policy is `BlockKnownRisky`. For a private group where everyone runs the same mod set, `MatchHost` is the stricter option.

See [configuration](docs/configuration.md) for every host setting.

## Require it from another mod

Reference `S1AntiCheat.API.dll`, then require the runtime during your mod's initialization:

```csharp
using MelonLoader;
using S1AntiCheat.API;

[assembly: MelonAdditionalDependencies("S1AntiCheat.API")]

internal sealed class TradeNetworkMod : MelonMod
{
    private AntiCheatHandle _antiCheat = null!;

    public override void OnInitializeMelon()
    {
        _antiCheat = AntiCheat.Require(
            "bars.trade-network",
            new Version(0, 1, 0));
    }
}
```

`Require` throws `AntiCheatUnavailableException` when the runtime is missing or too old. Do not catch that exception just to continue without protection. Let your mod fail initialization with the dependency error.

## Authorize incoming actions

Call `Authorize` on the listen host, inside the incoming FishNet handler. Use the sender connection supplied by FishNet. Never accept a connection ID, SteamID, price, balance, or ownership claim from the request payload.

```csharp
private void HandleTradeRequest(NetworkConnection sender, TradeRequest request)
{
    AntiCheatDecision decision = _antiCheat.Authorize(
        sender.ClientId,
        "trade.submit",
        new AntiCheatActionLimit(5, TimeSpan.FromSeconds(10)));

    if (!decision.Allowed)
    {
        return;
    }

    // The trade mod still owns these checks.
    ValidateRequestShape(request);
    ValidateInventoryOwnership(decision.SteamId, request);
    ReserveAssetsOnHost(request);
    CommitTradeAtomically(request);
}
```

S1 Anti-Cheat answers one narrow question: is this a verified peer allowed to attempt this capability at this rate? Your mod still validates the action itself.

A trade network should keep its ledger and escrow on the host or a separate trusted service. If one client tells another client that a trade succeeded, someone will eventually fake it.

The full integration notes are in [integrating another mod](docs/integration.md).

## Build and verify

Copy `local.build.props.example` to `local.build.props` and set the local game paths. Then run:

```powershell
dotnet build .\S1AntiCheat.csproj -c Mono
dotnet build .\S1AntiCheat.csproj -c Il2cpp
dotnet run --project .\tests\S1AntiCheat.ContractVerifier\S1AntiCheat.ContractVerifier.csproj -c Release
```

The complete local validation command is:

```powershell
.\tests\Run-Validation.ps1
```

The isolated GSE harness has three scenarios:

```powershell
.\tests\Run-GseP2PSmoke.ps1 -Runtime Il2cpp -Scenario Clean -GseSteamApiPath C:\path\to\gse\steam_api64.dll
.\tests\Run-GseP2PSmoke.ps1 -Runtime Il2cpp -Scenario Ownership -GseSteamApiPath C:\path\to\gse\steam_api64.dll
.\tests\Run-GseP2PSmoke.ps1 -Runtime Il2cpp -Scenario Risky -GseSteamApiPath C:\path\to\gse\steam_api64.dll -RiskyModPath C:\path\to\sample.dll
```

`Ownership` verifies a clean client first, then sends a non-owner `PlayerHealth.SendDie` RPC and requires the host to block it. `Risky` requires an IL2CPP sample that is already on the built-in deny list. The runner never replaces the Steam DLL in a live install.

Local game assemblies are build inputs only. They are not part of this project and should never be committed.

## Security boundary

The listen host can reject a bad request because it owns the authoritative state. The same guarantee does not work in reverse. A malicious host can lie about trades, balances, verification results, or anything else it owns.

This project is meant for a trusted host protecting a lobby from remote clients. It does not establish trust between strangers across separate player-hosted servers.

Read the [threat model](docs/threat-model.md) before using it for an economy or cross-player market.
