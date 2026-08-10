# Method coverage

Menu DLL names are a short-lived signal. The useful part of the sample review was identifying which Schedule One methods the menus call and deciding where the host can enforce a real rule.

No decompiled menu source or game binaries are part of this project. The table records method names and the protection boundary only.

| Observed game path | Current protection | Boundary |
| --- | --- | --- |
| `Console.SubmitCommand` and `ConsoleCommand.Execute` | Blocks console submission and selected state-changing command implementations on non-host clients. | Client deterrence. |
| `MoneyManager.ChangeCashBalance` | Blocks direct calls from non-game assemblies on a remote client. | Client deterrence; cash is still client-owned native state. |
| `MoneyManager.CreateOnlineTransaction` | Blocks direct calls from non-game assemblies on a remote client. | The native ServerRpc still accepts client-supplied transaction values. Economy mods must use a host-owned ledger. |
| `PlayerInventory.AddItemToInventory` | Blocks direct calls from non-game assemblies on a remote client. | Client deterrence; protected mods must validate inventory ownership on the host. |
| `PlayerMovement.Teleport` | Blocks direct calls from non-game assemblies on a remote client. | Client deterrence. General movement validation is not implemented. |
| `PlayerHealth.SetHealth`, `RecoverHealth`, and `Revive` | Blocks direct calls from non-game assemblies on a remote client. | Client deterrence. |
| `PlayerHealth.SendDie` and `SendRevive` ServerRpc readers | Requires the FishNet sender to own the targeted player object. | Host-enforced. |
| Player camera, save request, flashlight, crouch, equippable, consume-product, value, and world-dialogue ServerRpc readers | Requires the FishNet sender to own the targeted player object. | Host-enforced. |

## Deliberately not guarded as ownership-only RPCs

Some dangerous-looking calls are also legitimate cross-player gameplay. `Player.SendImpact`, damage observers, combat effects, and explosions may target an object the sender does not own. A blanket ownership prefix would break normal combat while still missing forged values.

Those paths need method-specific checks such as distance, weapon state, cooldown, line of sight, damage bounds, or a server-created action token. They should be added only when the host can reconstruct the rule from state it owns.

The same applies to quests, properties, employees, vehicles, relationships, and progression. Many menu calls only mutate the caller's local copy; others eventually reach a permissive ServerRpc. Each host-impacting path needs to be traced to its generated `RpcReader___Server_*` method before adding a guard.

## Why the two layers remain

The built-in name and SHA-256 deny list cheaply rejects the supplied known builds during the verification handshake. The method guards cover renamed or updated menus that keep using the same game paths. Neither makes a remote client trustworthy, so protected mods still authorize and validate every request on the listen host.
