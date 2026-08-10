# Configuration

S1 Anti-Cheat stores its settings under the `S1AntiCheat` category in `MelonPreferences.cfg`.

Restart the game after changing admission or manifest settings. That keeps the policy fixed for the full lobby session.

| Setting | Default | Behavior |
| --- | --- | --- |
| `EnableAdmissionGate` | `true` | Rejects remote transport identities that do not pass the lobby policy. |
| `FailClosedWhenLobbyUnavailable` | `true` | Rejects a remote join when Steam lobby membership cannot be read. |
| `AllowedSteamIds` | empty | Allows the listed SteamID64 values without current lobby membership. Separate values with commas. |
| `TrustSteamFriendsInLobby` | `true` | Allows a transport-verified Steam friend who is also in the lobby. |
| `TrustAllCurrentLobbyMembers` | `false` | Allows any current lobby member. This is a compatibility option and weakens admission. |
| `RequireClientAntiCheat` | `true` | Requires every remote player to answer the verification challenge. |
| `VerificationMode` | `BlockKnownRisky` | Selects `RequiredOnly`, `BlockKnownRisky`, or `MatchHost`. |
| `VerificationTimeoutSeconds` | `12` | Sets the client handshake deadline. Values below three seconds are treated as three seconds. |
| `IgnoredModNames` | anti-cheat assemblies | Excludes the listed assembly or display names from manifest comparison. |
| `DeniedModNames` | empty | Rejects clients that report a listed assembly or display name. |
| `DeniedModHashes` | empty | Rejects clients that report a listed SHA-256 assembly hash. |
| `DisconnectOnExploitAttempt` | `true` | Disconnects a peer after an integration reports `ExploitAttempt`. |
| `EnableRpcOwnershipGuards` | `true` | Rejects selected player RPCs when the sender does not own the targeted player object. |
| `EnableClientMutationGuards` | `true` | Blocks direct client-mod calls into protected cash, inventory, movement, and health methods. |
| `AllowedClientMutationAssemblies` | empty | Allows the listed assembly names to call protected client mutation methods. Separate values with commas. |
| `LockLobbyWhenGameplayStarts` | `false` | Marks the Steam lobby non-joinable after the main game scene loads. |

## Manifest modes

`RequiredOnly` checks that the remote player runs a compatible S1 Anti-Cheat runtime. Explicit name and hash deny lists still apply.

`BlockKnownRisky` also rejects the built-in names for known cheat menus and runtime explorers. Renaming a DLL or forging the report can bypass a name check.

`MatchHost` requires the reported client manifest to match the host manifest after ignored names are removed. This works best for a small private group with a fixed mod pack.

## Example host policy

```toml
[S1AntiCheat]
EnableAdmissionGate = true
FailClosedWhenLobbyUnavailable = true
TrustSteamFriendsInLobby = true
TrustAllCurrentLobbyMembers = false
RequireClientAntiCheat = true
VerificationMode = "MatchHost"
VerificationTimeoutSeconds = 12
DeniedModNames = "Example Cheat Menu"
DeniedModHashes = ""
EnableRpcOwnershipGuards = true
EnableClientMutationGuards = true
AllowedClientMutationAssemblies = ""
LockLobbyWhenGameplayStarts = true
```

MelonPreferences owns the exact file syntax. Keep a backup before editing the file by hand.

Only add an assembly to `AllowedClientMutationAssemblies` when you trust that exact mod to change native client state. This is a compatibility escape hatch, not a substitute for host-side validation.
