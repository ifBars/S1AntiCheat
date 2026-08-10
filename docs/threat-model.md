# Threat model

S1 Anti-Cheat assumes the listen host is trusted and remote clients may be modified.

## Protected assets

The project is intended to protect host-owned game and mod state, including inventories, balances, trade ledgers, progression, and privileged mod actions.

## Trust boundary

The FishNet sender connection and the transport Steam identity are host-observed inputs. Client request fields are untrusted, including claimed SteamIDs, connection IDs, balances, item ownership, positions, and timestamps.

The host decides whether an action can change authoritative state. A client may request a change, but it does not commit one.

## Covered paths

The runtime currently adds these controls:

- A prefix on FishNet's remote connection state handler checks Steam lobby admission before normal handling continues.
- A custom FishNet challenge ties each report to a connection and one-time nonce.
- A client manifest policy rejects reported risky, denied, or mismatched mods.
- Client-side Harmony prefixes block the native console entry points and selected state-changing command implementations.
- Client call-origin guards block direct external calls into the cash, online-balance, inventory, teleport, and health methods used by the sampled menus.
- Host-side prefixes restore ownership checks on selected player and player-health ServerRpc readers that use `RequireOwnership = false`.
- The public API denies protected mod actions until the sender is admitted and verified.
- Consumer-specific fixed-window limits reduce request spam.

## Limits

The client report is not a remote attestation system. A hostile mod can forge the list, alter a hash, patch the reporting method, or change code after verification.

The console and client mutation patches are deterrence. A hostile client can patch them out, reflect around them, or forge its call origin. Their job is to stop common menu behavior and raise the effort required, not to attest the process.

The handshake starts when the shared `DailySummary` network object is available. The host retries one nonce until the deadline because the client may spawn that object later. Protected integrations fail closed while verification is pending.

The project does not validate every native game RPC. In particular, shared money transactions, combat impacts, explosions, world progression, and other context-sensitive actions cannot be made safe by an assembly name or a generic rate limit. They need authoritative game semantics or a protected mod-owned ledger.

A malicious listen host remains authoritative. It can forge state and verification results for its clients. Do not use a player-hosted lobby as the source of truth for a cross-server economy.

Process inspection, native injection detection, kernel enforcement, global ban services, and identity reputation are outside the current scope.

## Consumer responsibilities

An integrating mod must validate request shape, authorization, ownership, current state, replay protection, and transaction consistency on the host.

Rate limits are not semantic validation. Manifest checks are not ownership checks. A verified peer can still send an invalid request.

## Failure policy

The default policy rejects unknown identities, unavailable lobby membership, missing client verification, invalid reports, expired challenges, denied manifests, and integration actions from unverified peers.

Compatibility settings can weaken those defaults. If a protected economy depends on this project, record the active host policy and refuse to start under an unacceptable configuration.
