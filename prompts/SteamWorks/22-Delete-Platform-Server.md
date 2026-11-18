# 22 — Delete `Realm.AdminServer` and `Realm.Tools.Admin`

> Read `00-Architecture-and-Task-Order.md` first. This task applies its "No new custom service" rule to the two projects that were the platform.

**Depends on:** 05, 06, 10, 11, 12, 13 (each removes one system that the server currently backs).
**Unblocks:** 15 (the final sweep documents what is left), 20 (the diagnostics decision).
**Related:** 07, 08 and 17 own the only tools that remain.

## User story

As the developer, I want the two platform projects gone rather than shrunk, so there is no deployable service, no database, no scheduled job, no credential to protect and no second source of truth for anything Steam already answers.

As a player, I want nothing in the game to depend on a Realm server existing, so an offline launch, a single-player match and the editor work the same on the day the project is deleted as they did the day before.

## Context

`Realm.AdminServer` is an ASP.NET service: a lobby registry with GeoIP and punching, a seeder registry and download path, a content-addressable asset CDN, a map publish portal, a creator registry with a greenlight queue, a multi-node cluster with event sync and quorum snapshots, a Steam-ticket account service with sessions and profile stats, a map-maintainer registry, a ban store, a metrics/engagement endpoint and a generic document store behind `/api/data/{collection}`. (A volunteer relay pool also exists, but only on the unmerged `networking_p2p_volunteer_relay` branch: it is not on `main` and is not a deletion target.) `Realm.Tools.Admin` is its CLI: admin key generation, maintainer management, greenlighting, account renames, CAS and manifest pruning, snapshot/restore, digest and event export.

Every one of those is either replaced by Steam or has no consumer left after the earlier tasks. This task is the structural deletion, and its job is to leave **nothing** behind: no "small storage layer", no "just the moderation helper", no hosted piece waiting to grow back.

**The two assumptions that used to justify a server, tested.**

1. *"Publisher keys cannot be embedded in the client, so I need a server to hold them."* The client does not need a publisher key at all. Everything the shipped game does goes through the Steamworks SDK inside the running Steam client, which authenticates as the logged-in user: Workshop discovery, UGC queries and votes, publishing with `ISteamUGC`, stats, inventory, lobbies and P2P. A Steam Web API publisher key is only needed for operations the *developer* performs — moderation from a secure server (`IPublishedFileService/UpdateBanStatus`, `UpdateIncompatibleStatus`, `UpdateAppUGCBan`, `Delete`; `IInventoryService/GetInventory`) and approval queues. Those are owner actions taken at the partner site or from a script on the developer's own machine, and Valve's own documentation states they must never be called from a client. So the keys have no shipping consumer, and the only credential that matters is the partner account, which never leaves the developer.
2. *"The game needs a server to aggregate and cache Steam data, so each client does not redo it."* The Steam client already is that cache: it holds the local UGC cache, and queries are served per client from Steam's own backend, so a thousand players browsing is Steam's load and not the developer's. No fan-out to Realm infrastructure exists to optimise. The concrete client-side limits are known and none of them needs a server: results are paged at 50 (`kNumUGCResultsPerPage`), a details request takes up to 1,000 ids, and all queries are scoped to the running AppID. The client cannot read another user's subscriptions or inventory, cannot search another AppID's Workshop, and playtime aggregates for an item are a Web API parameter rather than a client statistic — which is a reason to drop a per-map playtime chart (task 12), not a reason to host something.

**Moderation without a backend.** The partner site can "Hide as Incompatible" (the item stops being available in the game) and "Ban" (visible only to its owner), edit tags and read reports, with per-user permissions such as "Economy/Workshop Support" for moderators. That is the whole moderation surface this project needs (task 13), and it needs no database, no queue and no deployment.

**Creator profile data.** The requirement for a Realm database of creator profile fields was already reduced to one field — the optional Discord invite — which is authored in the map's own metadata and travels with the map. Everything else a player sees about a creator (persona, avatar, profile link, their other items for this AppID) is read live from the Steam client (task 16). There is no remaining field that needs a server, which is what makes this deletion possible at all.

## Requirements

### 1. Delete both projects

Delete `Realm.AdminServer` and `Realm.Tools.Admin`. Do not trim them, do not keep the storage layer, do not keep the moderation helpers, and do not move a "little" piece somewhere else in the solution. If a piece in the table below is still wanted, the answer is to re-derive the requirement in front of Steam's API, not to keep a server for it.

### 2. Per-feature verdicts

Every feature is listed with where its replacement lives. Anything not in this table is deleted on the same basis: it existed to make the server work.

| Feature in the server | Where it goes |
|---|---|
| `/auth/providers`, `/auth/steam/ticket`, `/auth/session`, `/auth/logout`, `/auth/rename`, `/profile/stats`, `/profile/match_result`, `SteamTicketService`, `AccountStoreService` | Steam identity (01, 11). The ticket service existed to mint a Realm session; with no Realm account there is nothing to mint. A peer's SteamID is established by the transport handshake (02), not by an account service. |
| `/lobbies/*` (register, heartbeat, close, propagate, join, punch), `LobbyRegistry`, `GeoIpService`, `HostSignaling`, `JoinerPunchRelay`, `JoinFailureFeedback` | Steam lobbies and SDR (03, 05). |
| `/relays/*`, `RelayPool`, volunteer registration | Steam Datagram Relay (06). Branch-only: absent on `main`, so verify and skip. |
| `/seeders/*`, `/api/seeders/catalog`, seeder websocket, `SeederRegistry`, `SeederDownloadRequest` | Workshop for public maps and the lobby's host transfer for local maps (08, 10). |
| `/api/assets/*`, `/api/manifests*`, `ContentAddressableStorage` serving, `ContentPruner`, `ManifestVersionPruner` | SteamPipe through Workshop (10). The local CAS stays as a client-side store; the server-side one does not exist. |
| `/api/publish_map/*` (initiate, finalize, upload_asset, asset_author) | `ISteamUGC` publishing in the editor (07). `asset_author` lookups die with public-key attribution (18). |
| `/api/cluster/*`, `ClusterEventService`, `ClusterPeerSync`, quorum sync loop, `AdminPublicKeys`, key generation | No federated nodes exist (05, 06, 10). There is no cluster to sync. |
| `/api/maps/{title}/maintainers/*`, `MapMaintainerHelper` | Forks and reviewed mirrors with SteamID credits (17, 18). A map is maintained by publishing a fork, not by adding a maintainer to a registry row. |
| `/api/admin/greenlight`, `/api/maps/greenlight_status/{mapId}`, `AdminGreenlightRequest` | The publish checklist (07), the local playtest bar that unlocks a public release (07A) and the trusted marks (13). Nothing replaces the endpoint: no request is sent anywhere. |
| `/api/admin/bans*` | Partner-site moderation for items (13). Per-player gating is a Steam-level and lobby-level concern, not a server table. |
| `/api/maps/report_metrics`, `MapStats`, `PlayerMapEngagement`, `MapMetricsReport` | Steam stats for anything player-facing (12). Per-map playtime and engagement charts are dropped, not reimplemented. |
| `/api/discovery/maps`, `/api/creators*` (register, check, list, portfolio), `CreatorDiscoveryService` data | `CreateQueryAllUGCRequest` and `CreateQueryUserUGCRequest` in the client (08, 13, 16); attribution by SteamID (18). |
| `/api/data/{collection}`, `/api/players*`, `/api/maps*` generic document store | Nothing stores these. The only candidate was a creator profile, which is Steam (16). Do not replace it with a local file store: that is the same service, smaller. |
| `Realm.Tools.Admin` keygen, maintainers, greenlight, accounts, rename, unlock-name, manifests, snapshots, restore, prune-cas, digest, export-events, info, status | Deleted with their server endpoints. Snapshots existed to back up the server's store; there is no store. |

**One exception, stated so it is not lost:** `Realm.Tools.Cli`'s own key command is not part of this deletion. It is removed by task 18 together with the public-key attribution scheme it signs, because removing it before 18 would break the attribution it still feeds. Check for any leftover `appsettings.json` / `AdminPublicKeys` documentation and delete that here.

### 3. Delete the callers, the tests and the build

- Remove both projects from every solution file and delete the project directories. There are three: `Realm.slnx` at the root, which is what CI builds, `Realm.Client/Realm.Client.sln`, and `Realm.ListenServerHost/Realm.ListenServerHost.sln`.
- Remove every client caller that only existed for the server. The known ones are `CreatorDiscoveryService` (`/api/creators`), the creator registration call in `EditorAssetImport`, the metrics report in `GameOver.Metrics`, the `asset_author` lookups in `LobbyRoom.MapAuthorship` and `MapStorageService`, and the creator check in `NetworkService`. Each is owned by the task in the table; this task deletes whatever is still there once those tasks have merged, and must not leave a call site pointing at an endpoint that no longer exists.
- Delete `Realm.Client/Testing/LobbyDirectConnectHarness.Ipv6.cs`: it launches `Realm.AdminServer.dll` as a registry node and has no other purpose.
- Delete the tests that assert contracts of the deleted services rather than adapting them. Every test file in `Realm.Network.Tests` whose subject is reachability, joinability, join phases or IPv6 endpoints goes: `ReachabilityCheckTests`, `ReachabilityContractTests`, `ReachabilityLocalCallerTests`, `ReachabilityNatMatrixTests`, `ReachabilityRouterTests`, `JoinCapabilityTests`, `JoinPhaseMachineTests`, `LobbyJoinabilityTests` and `Ipv6EndpointTests` — nine files. Also remove the `ProjectReference` to `Realm.AdminServer` in `Realm.Network.Tests.csproj`, and delete the file `LobbyDirectConnectHarness.Ipv6.cs` leaves the harness with. What remains in that project is the lobby identity wire contract and whatever else is not about a deleted subsystem; if that is nothing, the project goes too. A test for a service that no longer ships is a false statement about the product.
- Remove the AdminServer publish, archive and artifact-upload steps from `.github/workflows/build.yml`. A release artifact for a service nobody runs is how a deleted service comes back.
- Delete `appsettings.template.json`, `ServerHost`, `AdminAuthorityOptions` and the server's configuration surface with the projects.
- Delete or rewrite the documents that describe running the server: `docs/Hosting_Behind_Restrictive_NAT.md` and `IPv6_Lobby_Decision.md` describe infrastructure that no longer exists. Do not leave a hosting guide for a deleted service; task 15 owns the final documentation pass, and this task at minimum removes anything that instructs a reader to deploy it. `docs/Manual_Test_Volunteer_Relay.md` is branch-only and should already be absent; if it is present, it goes with the rest.

### 4. Keep the loopback, keep the tooling that has a consumer

The host's screen still reaches its authority over a loopback socket (`LocalAuthority`, `ServerControlProtocol`). That is internal IPC between two local processes and has nothing to do with this deletion: it is not a network route, it serves no player, and it must not be removed with the server. The editor's publish path (07) and the archive tool (17) are the only developer tools that remain.

### 5. No replacement host, no exception for a "tiny" service

Do not stand up anything to take the server's place: no hosted function, no static API, no database as a service, no telemetry endpoint, no cache. If a future feature genuinely needs one, it is a new task that argues for it against this decision, with its own account, keys, quotas, data-processing terms and privacy clause. The default answer is Steam's client API, a developer-run local script, or the partner site.

### 6. Verify the deletion

- `dotnet build Realm.slnx` succeeds with no reference to either deleted project, and so do `Realm.Client/Realm.Client.sln` and `Realm.ListenServerHost/Realm.ListenServerHost.sln`.
- The game starts and plays single-player offline with no server reachable, opens the editor and imports a `.rmap`.
- `Realm.AdminServer` and `Realm.Tools.Admin` appear nowhere in the repository outside `bin`/`obj` and git history: no `using`, no project reference, no workflow step, no config key, no documentation instruction.
- The release workflow produces no admin-server artifact.
- `Realm.Network.Tests` still builds and runs with the deleted files gone.

## Out of scope

- Implementing the archive tool (17), the publish flow (07), Workshop discovery (08, 13) or Steam stats (12). Those land before this task; this task deletes what they replaced.
- A hosted crash or error service. Task 20 explicitly decides that separately and this task must not half-add one.
- Removing the public-key attribution scheme (18) or the client's local CAS and host transfer (08, 10).
- Rewriting the remaining documentation or `AGENTS.md` (15, 19).

## Completion

- Both projects are deleted, from disk and from every solution and workflow.
- No client, editor, authority or map-script code path calls a Realm HTTP endpoint.
- Every feature in the verdict table has either a Steam replacement that already shipped, or is explicitly dropped with the reason recorded.
- The release build ships the game and nothing else.
- The repository contains no instruction to deploy, configure or operate a Realm service.
- Reported, not assumed: the list of what was dropped rather than replaced, so the loss is a decision on the record instead of a surprise later.
