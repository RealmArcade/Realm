# 05 — Delete the Lobby Registry and Its Admin Service

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 04.
**Unblocks:** 06, 15.

## User story

As the developer, I want the old lobby registry, its HTTP endpoints, its heartbeat, its GeoIP lookup and its client failover code gone, so there is one Steam lobby path and no server I have to run for players to find each other.

## Context

Tasks 03 and 04 moved lobby creation, browsing, joining, invites and membership to Steam. The Realm lobby registry is now dead code: a public HTTP service with an in-memory lobby table, heartbeat expiry, GeoIP, join tickets, punch coordination, host signalling and a client that fails over between registry nodes.

This task removes the lobby half of that service. It does not remove the relay pool (task 06), the account service (task 11) or the map distribution service (task 10); those have their own tasks.

## In scope

### Server

Delete the lobby registry and every endpoint that exists only to serve it:

- `Realm.AdminServer/LobbyEndpoints.cs`.
- `Realm.AdminServer/Services/LobbyRegistry.cs`.
- `Realm.AdminServer/Services/PeerRegistry.cs` if it only tracks lobby peers.
- The GeoIP service and its lookup data if no other feature uses it.
- Host signalling, joiner punch relay and join-failure feedback services that exist only for the registry join.
- The lobby request/response models: register, heartbeat, list, join, punch, close, join-failure report, lobby info, endpoint offers and reachability classes.
- Any registry-specific admin endpoints or diagnostics endpoints that expose the lobby table.

Keep the account endpoints until task 11 and the map distribution endpoints until task 10.

### Client

Delete the registry half of the lobby manager:

- The registry client, registry endpoint failover, node lists, retry loops and circuit breakers used for lobby calls.
- Heartbeat and keep-alive code that exists only to keep a registry listing alive.
- The join request/response path, join-phase reporting to the registry, and the direct "join by lobby id" HTTP call.
- The server list, official server URLs, embedded admin public keys used to verify registry responses, and the `ServersConfig`/`ServersConfigHelper` surface together with the runtime `servers.json` it reads. That file is generated, not committed; the committed template is `Realm.Client/servers.template.json`, and if the surface has no other consumer by the end of this task, the template goes too.
- The registry-backed lobby browser refresh. The browser now reads Steam lobbies.
- Registry-specific diagnostics, stability tracking and host-stage reporting that no longer has a consumer.

Do not delete the Steam lobby browser, the Steam join, the lobby protocol, the seat/roster flow, the match start flow or the ENet client used by the advanced "Join by IP" action.

## UX

- The lobby browser still looks the same, but its Refresh action queries Steam.
- There is no "server region", "registry status", "connecting to lobby server", "punching" or "relay" phase shown to the player.
- A join failure says why in game terms: full, closed, friends-only, wrong build, Steam offline.
- No screen offers a registry-backed server list, a "join by lobby ID" field or an address to type. The Steam lobby list is the only way in.

## Requirements

1. Remove the code, not just the call sites. Delete the services, models, configuration and data files that exist only for the registry.
2. Do not leave a dormant registry client behind a flag. There is no fallback to a Realm lobby server.
3. Remove the localization keys that belonged only to the deleted UI, and keep the keys that still describe the Steam lobby flow.
4. Update any diagnostics or admin pages that read the lobby table so they compile and do not claim to show lobbies that no longer exist.
5. Keep the match lobby's own protocol and authority admission untouched, except where they referenced registry fields.
6. If a shared model is used by both the registry and the match lobby, split it: the match lobby keeps what it needs, the registry model is deleted.

## Acceptance criteria

- There is no `LobbyRegistry`, `LobbyEndpoints` or equivalent lobby HTTP service in the solution.
- The client makes no HTTP call to create, list, join, heartbeat or close a lobby.
- `ServersConfig`, `servers.json` and `Realm.Client/servers.template.json` no longer affect the lobby path, and the template is deleted if nothing else reads the surface.
- The lobby browser and join still work entirely through Steam.
- No path reaches a lobby except the Steam lobby list and membership in a Steam lobby.
- No code references the deleted registry models or endpoints.
- The solution builds, and the deleted UI strings are gone from the localization source.

## Pitfalls

- Do not delete the relay pool or relay endpoints here; task 06 owns them.
- Do not delete account endpoints here; task 11 owns them.
- Do not keep a "legacy registry" path for old clients. Old builds are not supported.
- Do not leave the registry's data store tables behind in a migration that still runs on startup. Remove the storage and the migration.
- Do not remove the loopback authority control channel. It is not the lobby registry.
- Do not delete the ENet client needed by "Join by IP" in this task; it is not the registry, and the tasks that own its removal are 02 (the client's join dialog) and 06 (the traversal and host-side surface). It is not a survivor — the task report should say so, so the next task does not read it as one.
