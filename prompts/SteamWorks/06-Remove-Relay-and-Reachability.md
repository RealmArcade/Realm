# 06 — Delete Relay, NAT Traversal, Punch and Reachability

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 02, 05.
**Unblocks:** 15.

## User story

As the developer, I want every line of NAT measurement, UPnP, firewall, STUN, hole punching and volunteer relay code gone, so hosting is either "Steam connects the peers" or a manual port the host opened themselves, and there is no automatic network stack to maintain, test or explain.

As a player, I want to stop seeing NAT types, relay badges and "punching" phases that no longer describe how the game connects, and I never want to forward a port to play with a friend again.

## Context

Task 02 moved every remote peer onto Steam's peer-to-peer sockets and retired the remote ENet path. Task 05 removed the lobby registry that used the punch and reachability services. The automatic reachability and relay subsystem is now dead code. It is also the largest and most operationally expensive custom platform in the repository.

This task deletes the automatic traversal and relay completely. It is a deletion task: do not replace it with a simpler custom fallback, do not keep a "if Steam fails, try the relay" path, and do not keep the direct-IP surface it used to justify.

**Say what is actually being given up, because the two halves are not the same.** Punch and reachability measurement are **shipped**: they are on `main`, they are the listing policy `AGENTS.md` describes, and five suites in `Realm.Network.Tests` cover them. They are being *replaced*, not thrown away as dead weight — Steam owns traversal now, so the capability keeps an owner, and the honest justification is "Steam does this better and for free" rather than "this never worked". The **volunteer relay** is the opposite case: it exists only on an unmerged branch and has never carried a match, so deleting the relay half gives up nothing at all, and the code survives in git history if it is ever wanted again.

Do not let the second fact excuse the first. The claim that nothing here ever worked invites a reader to skip the one capability question that survives the swap — task 02's question 2, whether a host behind carrier-grade NAT can be played against through Steam's relay. That question gates nothing in this task, and it must not be answered by keeping this subsystem alive.

What this task does have to respect is ordering: task 05 must have removed the registry that called these services and task 02 must have moved every remote peer onto Steam, or the deletion leaves a caller with nothing behind it.

## In scope

**Verified against `main` before this task was written.** `Realm.Shared/Reachability/` (NAT classification, STUN, endpoint discovery, address selection, join phases, host reachability verification, port checks, IPv6 lobby policy), `Realm.AdminServer/Services/JoinerPunchRelay.cs`, `HostSignaling.cs`, the lobby registry, the GeoIP lookup and the listing policy are on `main`, and they are the real targets. The volunteer relay is not: `RelayPool`, `RelayModels`, `/relays/*`, `Realm.Shared/Network/Relay/*`, `LobbyManager.Relays`, `PeerRelayVolunteer`, `RelayPeerDemux` and `docs/Manual_Test_Volunteer_Relay.md` exist only on `networking_p2p_volunteer_relay`, which is never merged. Confirm they are absent, record it, and move on: do not recreate a relay so there is something to delete, and do not spend the task searching for one.

### Shared libraries

- Delete `Realm.Shared/Reachability` in full: NAT classification, STUN, endpoint discovery, address selection, reachability probes, punch tickets, join phases, host reachability verification, port checks, IPv6 lobby policy and all related models.
- If a `Realm.Shared/Network/Relay` directory survives on the base, delete it in full with its plans, tickets, UDP shim, demux, probe cadence and frame types. On `main` it does not exist.
- If the base still carries relay members on `IServerTransport`, its sink, `NetworkFrameKind` or a `RelayProtocol` type, remove them. On `main` none exist, so this is a check rather than a refactor.
- Keep `PeerLinkStatistics`; it is now fed by Steam.

### Client

- Delete NAT type testing, STUN results, UPnP, local endpoint discovery, UDP hole punching, automatic firewall rules, port checks, relay volunteering, relay connection setup, relay retry and relay diagnostics.
- Delete the reachability panel in lobby creation, the relay/NAT badges in the lobby room and the automatic punch/direct-connect flow.
- Delete the host's "Allow direct IP" option, the port field, the "Join Private Lobby via IP Address" button, its address dialog and the copy-address action. Keep the host's own loopback socket, which is what seats the host; delete everything that exposed a port to a player.
- Replace the lobby's connection-quality display with the Steam-sourced numbers from task 02.
- Remove relay/NAT/port settings and any "volunteer as relay" option.

### Admin service

- Delete host signalling, the joiner punch relay, join-failure feedback and the GeoIP lookup if they are still present after task 05. These exist on `main`.
- Relay endpoints, allocation, volunteer registration, health checks and budget configuration are not on `main`; verify they are absent instead of looking for them. If task 05 or the branch base brought any of them in, delete them here.
- Remove relay-related app settings, deployment configuration and secrets if any remain.

### Tests, tools and documentation

- Delete the tests that exist only for reachability probes, joinability policy and relay behaviour. In `Realm.Network.Tests` that is `ReachabilityCheckTests`, `ReachabilityContractTests`, `ReachabilityLocalCallerTests`, `ReachabilityNatMatrixTests`, `ReachabilityRouterTests`, `JoinCapabilityTests`, `JoinPhaseMachineTests`, `LobbyJoinabilityTests` and `Ipv6EndpointTests`. Do not weaken them into placeholder tests, and do not leave the project referencing a deleted helper.
- Delete or archive outside the shipped project the network rig and the manual cross-network relay test plan.
- Delete `docs/Hosting_Behind_Restrictive_NAT.md`, `IPv6_Lobby_Decision.md` and `Seed_Node_Setup.md` (both at the repository root, not under `docs/`), plus `Manual_Test_Volunteer_Relay.md` if it is present (it is branch-only and should not be), `docs/rigs/` if its only subject is the reachability rig, and any other document that tells players to enable UPnP, seed a map or test a relay.
- Replace them with a short statement where hosting is documented: Steam connects the peers, the game never asks a player to forward a port or configure a router, and a machine without Steam can play single-player and the editor but not multiplayer. Do not write a replacement guide for a path that no longer exists.
- Update the README and architecture docs to say that Steam owns NAT traversal and relay.

## What stays

- The loopback transport between the host's client and its authority. It is internal IPC, not a public multiplayer path.
- The Steam P2P transport and its connection-quality reporting.
- The loopback socket between the host's screen and its authority, bound to the loopback address. ENet remains that socket's transport, and that is not a second multiplayer path: it carries one seat, on one machine, that no peer can reach. Switching loopback to a different transport is a separate decision and is not part of this task.
- Nothing else about connecting to another machine. Two Steam peers on one network already get a direct path from Steam, so a LAN party needs no port and no special mode; a machine without Steam plays single-player and the editor.
- The snapshot fragmentation, repair ring and presentation timeline.
- The lobby protocol, seat flow and match start.
- The authority's resident process and control channel.

## UX

- No screen shows a NAT type, a relay status, a punch phase, an automatic firewall prompt, an address or a port. Hosting is one button.
- The lobby creation flow is shorter: pick a map, choose public or friends-only, host.
- A connection failure says "Steam could not connect to the host" or "The host's connection was lost", with a retry action.
- The lobby's network panel, if kept, shows round-trip, jitter and packet loss only.

## Acceptance criteria

- No project references `Realm.Shared.Reachability` or `Realm.Shared.Network.Relay`.
- No UPnP, STUN, firewall, port-mapping, punch or relay code remains in the client or authority.
- No relay service, endpoint, pool or configuration remains in the admin service.
- Hosting and joining still work through Steam with no port forwarding.
- No screen offers a direct-IP host or join, no ENet socket is bound beyond the loopback address, and no path admits a peer Steam has not authenticated.
- No automatic NAT traversal, UPnP, STUN, punch or relay code remains.
- The lobby no longer displays NAT, relay or address information.
- The solution builds, and the removed tests are deleted rather than left failing.
- No documentation tells a player to run a relay, enable UPnP or forward a port.

## Pitfalls

- Do not keep a direct-connection path for LAN or development. Two machines on one network connect through Steam, which already prefers a direct path between Steam peers.
- Do not delete the loopback socket along with the traversal code. The host's own seat still uses it, and deleting it breaks every match rather than one network shape.
- Do not keep the relay protocol types "for compatibility". There is no old client to be compatible with.
- Do not delete the loopback transport. The host's own seat still needs it.
- Do not remove Steam's relay; remove only Realm's custom relay. Steam may still choose a relayed path internally, and that is expected.
- Delete the tests with the code. A test that covers a deleted subsystem is not a safety net; it is a compile error waiting to happen.
