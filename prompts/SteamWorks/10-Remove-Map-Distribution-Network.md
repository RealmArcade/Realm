# 10 — Delete the Map Distribution Network, CDN, Seeders and Cluster (Keep Local Map Sharing)

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 09.
**Unblocks:** 13, 15.

## User story

As the developer, I want the custom map distribution service, its CDN, its peer seeders, its cluster replication and its portal endpoints deleted, so Steam Workshop is the only public way a map reaches a player, a local map is shared host-to-host, and there is no map infrastructure left to operate.

## Context

Tasks 07 to 09 moved publishing, downloading and map identity to Steam Workshop. The old distribution system is now dead: an HTTP server with content-addressable storage, asset endpoints, manifest endpoints, seeder registration and propagation, a Bloom-filter peer network, cluster event replication, version pruning, map metrics, discovery, ratings, awards and admin endpoints.

This task removes the distribution half. It does not remove the account service (task 11) or the review/moderation surface (task 13), although some portal endpoints overlap; leave those for their own tasks and delete only what belongs to distribution here. It also keeps the local content-addressable store and the host-transfer path for maps that were never published; those are not the CDN.

## Before deleting: confirm the back catalogue

The developer keeps an archive of every published map, so this task does not export the distribution storage first. Confirm it anyway, before anything is deleted: list every map and every version the distribution storage holds, check each one against the archive, and record the result. A map that is missing from the archive is exported now and added to it while the storage still exists. Deleting a service is not a reason to lose the only copy of somebody's map, and that archive is also what the mirror policy (task 17) and the old-map compatibility path (task 09A) depend on.

## Where a player Steam cannot reach gets a map

Workshop is unreachable for some players, mainland China among them, for reasons no Realm-side service can fix, and the CDN was the only thing that ever pretended to address it. Nothing in the product replaces it. The replacement is `.rmap`: a complete map file that a community archive or an individual can host, mirror and hand out on whatever channel works where they are, and that the game imports into the local CAS without Steam, without the map being published and without a network call.

The engine's whole job here is to leave that path unobstructed. Keep `.rmap` export and import first-class; do not require a Workshop item for a local map to be hosted, joined or played; and add no anti-tamper, integrity gate or phone-home that would refuse a map because it arrived from outside Steam. The lobby hash check compares two players' installed copies with each other and is not a licence check, so it does not contradict this.

Do not build a Realm-operated download site or archive for this, and do not add a "Workshop unavailable" fallback path. Who distributes `.rmap` outside Steam, in which country, and under what terms is the community's decision to make about its own reach; a foreign operator is the worst possible party to guess at it.

## In scope

### Server and shared distribution services

Delete the distribution server, CDN and cluster, but keep the local CAS and the host-transfer path:

- The distribution HTTP server, CDN manifest endpoints, CDN asset endpoints and range/streaming helpers.
- Keep `ContentAddressableStorage` and the local archive/materialisation helpers. They serve local `.rmap` imports, the editor and host transfer. Delete only the server-side CDN storage, token buckets and bandwidth accounting.
- Seeder registration, seeder advertisement, seeder propagation, seeder circuits, peer seeding policy and the Bloom-filter/peer registry used only for public map transfer.
- Cluster event replication, cluster peer sync, snapshot apply, manifest removal, map published and map metrics events, and the cluster endpoints.
- Map registry, publication endpoints, CDN asset upload endpoints and the distribution-specific metrics and engagement records. Leave discovery, ratings, reviews, awards and the old creator portal for task 13.
- Server-side content pruning and manifest version pruning. Keep the local CAS orphan cleanup used by the editor and local imports.
- Distribution configuration: CDN URLs, seeder settings, cluster keys, public keys used only for distribution responses and the map portal's app settings. Keep the local storage root.

### Client

Delete the client-side transfer stack:

- The map distribution client, HTTP manifest/asset fetchers, circuit breakers, CDN failover and the CDN transfer sessions.
- The peer map downloader, peer seeder manager and the ENet seeder transfer service. Keep the host-transfer session and the `.rmap` import path for local maps.
- CDN/seeder transfer progress UI. Keep the host asset transfer items and progress UI for local maps.
- The lobby's seeder/CDN map-download paths. Keep the local-map path that waits for the host.
- Distribution diagnostics and "seed this map" or CDN-source messages. Keep "the host will send this map" for a local map.

### Portal and admin

- Delete the map portal pages and endpoints for publishing, asset management, seeders and distribution metrics. Leave discovery, ratings, reviews, awards, the old creator portal and moderation for task 13.
- Delete the admin endpoints that exist only to manage distribution storage and seeders.
- Keep account endpoints until task 11. Keep review/moderation work for task 13.

### Tests and documentation

- Keep the tests that protect the local package, manifest round-trip and reference resolver. They are still part of the editor's packaging contract.
- Delete the tests that cover the removed distribution server, CDN, seeders, cluster and portal.
- Delete distribution deployment docs, seeder runbooks and CDN configuration docs.
- Update the README and architecture docs to say that Workshop is the only public map distribution channel and that local maps are shared host-to-host.

## What stays

- The map package format, manifest, hashing, validation and local export/import.
- `MapFileService`, `MapWorkspaceService`, `MapUpgradeService`, `MapReferenceResolver` and the editor's map packaging.
- `ContentAddressableStorage`, `MapArchiveHelper`, `MapImportPipeline`, `ServerMapShares` and the host-transfer path for local maps.
- The Workshop publish and download flows from tasks 07 and 08.
- The map library and map details UI.
- Optional in-package signatures if they are still used for credits or integrity.

## UX

- No map screen shows a CDN, seeder, cluster or server status.
- A Workshop map that is not installed offers a Workshop subscription, not a peer download.
- A local map that the host has in CAS says "The host will send this map"; it does not offer a CDN or seeder.
- The map library has no public seeding controls; a local map can still be hosted.
- Admin pages for map distribution are gone; Workshop is the moderation surface.

## Acceptance criteria

- No distribution server, CDN, seeder, cluster or portal types remain in the game.
- The local CAS, archive helper, import pipeline and host-transfer path remain and are used only for local maps.
- No Workshop map download path calls a Realm server, CDN, seeder or host transfer. A local map with no Workshop item ID may use the host-transfer session.
- The map library and lobby use Workshop or local files only.
- The cluster, CDN, seeder and portal configuration is gone from the build and deployment.
- The local packaging and manifest tests still compile and, if run, pass; the removed distribution tests are deleted.
- The solution builds, and the distribution portion of the map portal no longer runs as part of the product.

## Pitfalls

- Do not delete the map manifest, archive helper, reference resolver or `ContentAddressableStorage` just because they live under a distribution-themed folder. They are the editor's local package format and the local-map store.
- Do not delete the account service in this task; task 11 owns it.
- Do not delete the review/greenlight code in this task; task 13 owns it.
- Do not leave empty namespaces, dead configuration keys or a "distribution" feature flag. Delete them.
- Do not keep the old CDN as a fallback for players who cannot reach Workshop. Steam is the channel inside the game, and `.rmap` shared by the community is the channel outside it.
- Do not add anti-tamper, an integrity gate or a version phone-home that refuses an imported `.rmap`. It would break the one path that works where Workshop does not, and it would not stop anybody who wanted to redistribute a map.
- Do not delete the host-transfer path. It is the only way a local, never-published map is played online.
- Do not treat host transfer as a public seeding service. It is one host sending one map to one joiner.
- Do not delete the storage before the archive has been checked against it. The archive is the only copy of a map whose creator never published it anywhere else.
