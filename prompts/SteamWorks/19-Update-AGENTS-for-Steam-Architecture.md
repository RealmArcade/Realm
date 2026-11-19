# 19 — Update AGENTS.md for the Steam-First Architecture

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 15. Run this after the platform cleanup, so the file describes what the repository actually contains.
**Related:** 01, 02, 03, 04, 06, 11, 13, 16, 17, 20, 21 and 09A.

## User story

As the developer, I want `AGENTS.md` to describe the Steam-first architecture accurately and to contain explicit guards against rebuilding the custom platform, so a future agent does not read an obsolete rule and reintroduce accounts, a lobby registry, a relay, a CDN or a reachability service.

As a contributor, I want the project instructions to keep every convention that is still true, and to stop asserting things the migration deleted.

## Context

`AGENTS.md` was written when Realm operated accounts, a lobby registry, a reachability and relay service, a map distribution network and an admin server. The migration deletes those and makes Steam the platform for identity, lobbies, transport, Workshop, stats, moderation and the creator's public profile. Several statements in `AGENTS.md` now contradict the code and the task set, most importantly:

- the "Steam Surface for Map Scripts (Opt-In)" section says "The authority has no Steamworks";
- the "Lobby Reachability (measured, never certified)" section describes a subsystem that no longer exists;
- the creator/profile and map-distribution assumptions appear in several places;
- there is no guard that stops a future change from re-adding the deleted platform.

This is a documentation correction, not a code change. Do not add features, do not restore a deleted subsystem, and do not rewrite conventions the migration did not touch.

## Requirements

### 1. Correct only the statements the migration invalidated

Read `AGENTS.md` and compare every platform-related statement with tasks 01 to 20, including 09A. Change only statements that are now false or misleading. Keep the file's existing tone, heading style and level of detail. Do not reorder unrelated sections, do not rewrite the ECS, simulation, map API, map workspace, localization, Godot or concurrency rules, and do not "tidy" prose for its own sake.

### 2. Replace the lobby reachability section

Replace the whole "Lobby Reachability (measured, never certified)" section with a short "Steam Lobbies and One Transport" section that states the new contract:

- Steam lobbies are the only lobby directory and Steam P2P is the only transport. There is no Realm lobby registry, heartbeat, GeoIP, reachability class, punch, relay, direct-IP join or forwarded port.
- Every seat is a Steam identity. There is no guest, no pending-approval peer and no locally typed name that can hold a seat, so a peer that cannot present a verified identity is not admitted.
- The host's own screen reaches its authority over a loopback socket bound to the loopback address. That is internal IPC rather than a multiplayer route, and it is the only thing ENet still carries.
- A session with no Steam plays single-player, the editor and local maps. It does not play multiplayer, and the game says so plainly instead of offering a worse way to connect.

Do not keep the old measurement vocabulary (classes, cold probes, translated ports, node capabilities, join-failure feedback) anywhere in the file.

### 3. Correct the Steam map-script surface

In "Steam Surface for Map Scripts (Opt-In)", replace "The authority has no Steamworks" and the sentences that depend on it. The correct contract is:

- The authority initialises Steamworks when it is available, for Steam P2P transport and admission. That is platform plumbing, not the player's Steam account surface.
- A map script's achievement, stat and cloud operations are still written as `PresentationKind.Steam*` requests addressed to the player slot, and only the screen in that seat applies them. The authority does not unlock achievements, write stats or touch cloud files on the player's behalf.
- A seat always carries a verified SteamID64, because a peer is admitted only through Steam, so the map-script Steam surface has no guest case to handle.
- Keep the existing namespacing, cloud round-trip, reliability and `IsSteamAvailable` rules. Do not change the map API itself.

Search the rest of the file for the same assumption (`The authority has no Steamworks`, `authority` plus `Steam`, `ServerSimulation.SetPeerSteamId`) and make every occurrence consistent.

### 4. Add a "Steam-First Platform Boundaries" section

Add one new section with the guards below. Write them as project rules, in the same imperative voice as the rest of the file. Keep it concise: this is a list of boundaries, not a design document.

**Platform ownership**

- Steam is the only provider for player identity, sessions, lobbies, transport, NAT traversal, relay, invites, Workshop hosting and discovery, persistent stats, leaderboards, moderation and the creator's public profile. Do not build a custom replacement.
- If a capability is not player-visible and Steam offers it, use Steam. If it is player-visible and Steam cannot express it, keep the smallest local layer and never a second service with its own database, HTTP API, accounts or deployment.
- No new database, HTTP endpoint, queue, retry loop or deployment for a platform feature. Stop and re-read the Steam capability first; prefer changing the requirement to fit Steam.

**Deleted platform must not return**

- Do not reintroduce `Realm.AdminServer`, `Realm.Shared/Reachability`, `Realm.Shared/Network/Relay`, `LobbyRegistry`, `LobbyEndpoints`, `ServersConfig`, `servers.json`, the account service, the map distribution server, the seeder network or the admin portal. Their absence is the design, not an unfinished migration. The volunteer relay on `networking_p2p_volunteer_relay` is never merged: if a custom relay is ever wanted again it is a new decision with its own justification, not a branch to resurrect.
- Write down what map reuse is, so a future agent neither deletes it nor grows it into a resolver. Composition (`MapReferences`/`references.json`, `MapReferenceResolver`, `MapMetadataMerger`, `MapScriptComposer`, `MapComposition.g.cs`) is vendoring: on save the referenced map's files are copied into the parent, its metadata is merged with the parent's overrides kept in `metadata.partial.json`, and its C# is compiled into `composed/<Map>/`. It is one layer deep, pinned by `ManifestBlake3`, and the packaged `references.json` clears the authoring machine's `FolderPath`. Everything downstream reads one self-contained folder, so the map format has no load-time dependency, no install graph and no resolution step, and `MapMetadata.Dependencies` is a second unused vocabulary for the same idea that is deleted rather than supported. A guard should forbid a load-time cross-map resolve and should not forbid composition.
- Do not add a compatibility shim, a "legacy" folder, a feature flag or a fallback for any of them. Git history is the archive.
- `Realm.Client` must still never reference `Realm.ListenServer`. If the client and authority need to share Steam code, put it in a separate assembly with no Godot dependency and no authority logic; a shared Steam helper is not a licence for the client to call the authority.

**No Steam DRM, offline always works**

- Never enable Steam DRM, a Steam launcher wrapper or a mandatory Steam startup check. The build must start with no Steam client.
- Single-player, the editor, `.rmap` import and local map play must always work without Steam. Multiplayer and every other Steam surface is hidden with a translated reason, not left clickable and broken.
- Steam offline mode and running without Steam are different: offline mode still initialises Steam and can use Steam's local features; with no Steam client there is no identity at all, so no lobby, no Workshop and no multiplayer. Do not conflate them.

**One AppID, no demo bridge**

- Exactly one AppID is live at a time. The official AppID already exists and is configured as the demo's base game; only its store release is deferred, so "the official AppID is available" is a release date, not a missing identifier. The demo is the publicly listed AppID until then and is unpublished afterwards. Do not support both as reachable products at once.
- The AppID comes from the launching process, never from a hard-coded constant. `steam_appid.txt` is a local-development file only and must never ship.
- Lobbies, stats and cloud files do not carry over between AppIDs. A Workshop item belongs to exactly one AppID as well, so the demo-era publish writes a second item into the official Workshop while it is still dormant (task 07); that is two items authored once, never one item moving between apps. Do not build a migration, bridge or bulk transfer between the two Workshops.

**Workshop content**

- A Workshop item is a complete, playable map folder downloaded as one unit. Do not publish asset-only items, "sword" mods or per-asset packs, and do not build a per-asset Workshop downloader.
- The creator owns the Workshop item and can update, privatize or delete it. Realm can hide, ban or mark an item incompatible, and can mirror it under the UGC license; Realm cannot delete or transfer another user's item.
- Steam is the host, not a Realm CDN. There is no CDN purge, seeder network or host transfer for Workshop maps. Host transfer remains only for local maps the host has in CAS.
- The trusted mark is a presentation policy, not a platform primitive: the developer's curated mark (preferably Steam's per-item accepted-for-use flag, which only the developer can set, with a Workshop collection or a local list as alternatives) or a configured vote/score threshold. It is not a publish gate and not a review database. The threshold starts low and can be raised; it is not a fixed 100.
- Never start a map library or a lobby browser filtered. Every map and lobby is shown with a trust mark, and the confirmation belongs on entering an untrusted lobby, not on hiding it.
- **Nothing is published until other Steam accounts have actually played the map.** Until then it has no Workshop item and reserves no name, so an unfinished map is never in the catalogue and never holds a name; the first publish, after the gate, creates the item at the visibility the author chooses. That record is a local file on the author's machine - distinct SteamIDs collected by the authority during hosted matches, keyed by the map's name and the author's SteamID64 so the count follows the map across versions, a re-published item and a change of AppID - and it is never a service, a database, a request, a queue or a verification of any kind. It is not a discovery filter either: the greenlit state is a label and an optional sort, and nothing is hidden by it. Never rebuild it as a hosted greenlight, and never gate installing, joining, hosting or `.rmap` export on it.
- One optional join code per lobby is the only gate beyond Steam visibility and invites. It is compared on the authority, never stored in Steam lobby metadata, never a credential, and never a requirement for standing in the lobby room.
- Any seated player may invite their own Steam friends. Do not restrict invites to the host; a friends-only Steam lobby admits invitees who are not the host's friends.
- When mirroring a Workshop item, upload it under a separate Steam account, never the partner account, and only after a manual review. Keep a record per mirror: the source item, the creator's SteamID, the license clause relied on, the reviewer and the date. A mirror under a second account reduces who receives a rights notice; it does not make the item stop being UGC under this AppID, so never treat the second account as legal insulation for the app.
- Mirrors live on Steam Workshop and nowhere else. Never add a Realm-hosted mirror site, mirror storage, archive download page or public mirror API, and never publish a mirror outside Workshop as a `.rmap`.

**Old maps and the compatibility contract**

- A map is playable for as long as its `EngineGeneration` is supported and its data can be migrated. It does not have to match the game's build number; the `GameBuildNumber` equality gate on maps was removed on purpose (task 09A) and must not come back as a "safety" check.
- The map's data is upgraded by `MapUpgradeService` onto a private session copy at lobby creation. Never migrate a Workshop folder in place, and never add a per-version reader or a fallback branch in the loader.
- Exactly one session copy is retained after a match: the most recently used one, keyed by the Workshop item ID and the package hash. Recompute the key on every open so a retained copy can never make a stale map playable, replace it only after the new copy is verified, sweep every other copy at startup, and never hard-link it to the Workshop folder.
- The map-script host import surface is additive. Never remove a WASM host import, change an existing import's signature or meaning, or reuse a replicated component id. An ABI break cannot be repaired at runtime: only the author or a mirror maintainer can recompile the map.
- Do not build old-engine hosting, a version switcher, side-by-side game builds, per-version AppIDs or a Workshop item that ships an engine. Builds that shipped before the contract cannot participate, and the guarantee starts with the current generation. The Steam beta branch is a forward-only preview channel for creators and players, never a way to run an older engine.
- The retired version launcher does not come back in any form: no `versions/<build>` resolution, no main-menu version dropdown, no relaunch-or-download popup in the lobby browser, lobby creation, the lobby room or the replay list, and no GitHub releases link presented as a way to get another build. A build difference between two screens is a Steam update.
- There is no non-Steam update path. The game does not poll a release feed, does not show an "outdated" badge and does not link to a download page; Steam keeps players in step. GitHub links that remain are support and takedown surfaces only, and they must never read as distribution.
- Replays are supported only while their recorded format stays readable. Do not add a replay migration, a compatibility shim or a "run an older build to open this" path; refuse an unreadable replay in one line and say which version recorded it.
- The preview build cannot publish Workshop items. The rule lives in the build CI uploads to the branch, not in a runtime branch check, a setting or an override prompt: a preview build can playtest, import and export `.rmap`, and cannot create or update an item. Do not re-enable it "temporarily", and do not disable the editor or the export alongside it.
- A release that breaks a wide part of the catalog is answered by setting the previous build live again, and a narrow break by the affected authors rebuilding. Keep local persisted state (settings, the map store, the asset index) readable by the previous build so a rollback does not discard it.
- Support at least the current generation and the one before it, and name the generation in the map details rather than in a warning dialog.

**Creator identity and links**

- The creator's public profile is their Steam profile and Workshop page. Do not build a Realm creator database, website route, OpenID login, bio editor or profile copy.
- The only creator link the game shows beyond Steam is one optional `DiscordServerInviteLink` in the map's metadata. Do not generalise it into a social-link list, a website field or a donation surface. The editor's helper label tells the creator to make a non-expiring invite.
- Other Workshop items shown for a creator must be queried for the current AppID only. A SteamID is global; never show that person's maps for another game.
- Do not scrape Steam profile pages or Workshop descriptions for links, and do not call the Steam Web API from the client with a developer key.

**Free game, developer cosmetics only**

- The game is free and no map, mode, feature, seat, unit, building or match option is purchasable. Nothing purchasable may affect gameplay, reach, information, matchmaking or randomness. Do not add a config flag, an override or a "temporary" exception that makes a map or a feature paid.
- The only products are the developer's own cosmetic items for the official game. There is no per-map product, no per-creator product, no per-asset SKU and no creator payee. Do not add a revenue split, a payee list, an `IWorkshopService/SetItemPaymentRules` call, a monetization block in map metadata, a price a creator supplies or a payout calculation. Per-creator sales were considered and dropped (task 21); a design that needs them is a new product decision, not a small addition.
- Product identity and price are the developer's. Item definitions are created with `IGameInventory/UpdateItemDefs` under a publisher credential, by the developer, for the official AppID. No publisher credential may live in the repository, a build, a shipped config or a map package, and the client, the editor, the authority and any map script must never call a publisher API or any Steam Web API endpoint that requires a publisher key.
- Never build a payout, a revenue ledger, a wallet, a cart, a checkout, a refund flow or a purchase history, and never have the developer move money to a player or a creator. Do not build contributor payment on an item-definition `dividends` field; that is not a documented mechanism.
- An entitlement is cosmetic only, read from Steam every session and never persisted as durable state. No core path may read it for admission, seats, scoring, matchmaking or randomisation, and a refund is a normal revocation that only changes appearance.
- Cosmetic assets ship inside the official game. Never gate a download, an asset load, a map, a lobby join or a seat on an entitlement, and never hide a non-owner's cosmetic from other players. Only equipping is paid.
- Sell on the official AppID only. Items and purchases belong to one AppID, so nothing is sold under the demo AppID.
- No donation, payment, store or advertising link in the game, in a map, in a generated Workshop description or in a generated credits line. The only creator channel is the `DiscordServerInviteLink` in map metadata, and the only guidance about it is the publish-time explanation of Valve's rules. Do not add a creator support panel, a price display, a "support us" prompt or a donation action.
- The publish guidance states the prohibition plainly and in the player's words: "Steam bans maps that post links to external payment systems (Patreon etc.). Put your donation links on your Discord server instead." Never soften it to "Valve has no specific rule about this", "not explicitly prohibited" or a bare "at your own risk": a hedged label is read as permission, by players and by coding agents alike, and the behaviour it invites is what gets the item - and with it the map - removed. Where a claim needs sourcing, put the source in the task notes, not in the label.
- Player-facing text says map, publish, subscribe and the player's own vocabulary - never "UGC", "Workshop item", "published file", "item definition" or "entitlement". A creator is making a map for Realm and has no reason to know what a Workshop item is; the platform underneath is the developer's concern. Keep Steam's own words only where the player is being sent to a Steam page whose title uses them.
- Do not advertise a monetization route in the publish dialog and do not offer to arrange one. There is no Realm support desk, and per-creator Steam sales are not a product this project offers. A line promising "an official method, contact support" is a promise the project cannot keep and a queue of requests it will decline.

**No platform server**

- The project ships no hosted service, database, HTTP API, queue, scheduled job or deployment of its own. `Realm.AdminServer` and `Realm.Tools.Admin` are deleted (task 22) and do not come back in any form, including a small one behind a different name.
- When a feature appears to need infrastructure, re-read the Steam client API and the partner site first, then consider a developer-run local script, then change the requirement to fit Steam. Inventing a service is the last option and needs its own task and justification.
- Publisher-key Web API calls belong to the developer's own machine or the partner site. They never ship, they are never proxied through something this project operates, and moderation of Workshop items is done with the partner site's tools unless a local script is genuinely better.
- A loopback socket between the host's screen and its authority is internal IPC, not a service. Do not remove it while deleting services, and do not treat it as a precedent for adding an endpoint.

**One transport, one identity**

- Multiplayer is Steam's, and there is no second path. Do not add a direct-IP join, a LAN multiplayer mode, a port prompt, a UPnP or NAT helper, a relay, a punch, a reachability probe or an automatic fallback when Steam fails.
- Every seat is a SteamID. Do not add a guest, a locally typed name, a pending-approval peer or any way to seat a peer Steam did not authenticate.
- The host's own loopback socket is internal IPC. Do not bind it beyond the loopback address, and do not remove it: it carries the host's seat and single-player.
- Authorship is the Workshop item owner or the attribution tag, never a typed name. A map also carries its own Ed25519 package signature and, when it is a Workshop item, the key fingerprint the item publishes; that is integrity and continuity evidence for `.rmap` copies and never an identity, a gate or a replacement for Steam's item ownership. The displayed author name always comes from Steam for the SteamID64 the package states, and an unconfirmed claim is rendered as a claim.
- The authorship tiers are additive and must stay that way: signed package, local key continuity, and Steam confirmation. Do not add a key directory, revocation service, reputation database or server, and do not make any tier a condition of publishing, installing, joining or hosting.

**Preserve the game**

- Steam replaces the platform around the game. Do not change the ECS, snapshot protocol, map script ABI, map format, editor surface or RTS rules as part of a platform change unless the task explicitly says so.
- Keep `IGameAPI` and `ILobbyAPI` generic. No map-specific rule, identifier or balance value belongs in the core; express variation as map data or an opt-in API call.
- Keep the existing localization, Godot, node naming, lifecycle and match-critical concurrency rules. This task does not change them.

### 5. Reconcile the rest of the file

Search `AGENTS.md` for these terms and fix every remaining stale statement: `AdminServer`, `LobbyRegistry`, `Reachability`, `Relay`, `Seeder`, `CDN`, `AccountStore`, `AccountId`, `ServersConfig`, `servers.json`, `servers.template.json`, `community node`, `volunteer`, `creator profile`, `greenlight`, `star rating`, `NAT`, `UPnP`, `punch`, `SteamDRM`, `The authority has no Steamworks`, `trusted filter default`, `direct IP`, `LAN-only`, `Tailscale`, `guest`, `microtransaction`, `donation`, `store`, `paid item`, `GameUpdateService`, `api.github.com`, `GetVersionExecutablePath`, `versions/`.

Rewrite the paragraph that describes what the test suite holds ("the suite is deliberately small and holds only contracts whose silent breakage is expensive"). It currently names the reachability probe, the lobby identity wire and the joinability policy, all of which this migration deletes. Name the contracts that survive instead — snapshot fragmentation, the replication classification and wire ids, the map project's DWARF settings, pathing invariants, map package and manifest integrity, the shared-memory component ids and the roster settings line — and do not leave a deleted subsystem in the list of what the suite protects.

Where a term is still valid, leave it and make sure its context is still true. For example, a reference to the loopback authority, the map package, the local CAS or the host-transfer path for local maps stays. A reference to a relay or a lobby registry goes.

### 6. Do not over-correct

- Do not re-add a direct-IP exception because a contributor asks for LAN play. Steam already prefers a direct path between two of its peers on one network, and a second transport is a second identity model.
- Do not write "Steam is required" or "the game cannot run without Steam". Offline single-player, the editor and local maps are requirements; multiplayer needing Steam is a narrower and different claim.
- Do not remove the opt-in Steam map API. It still exists and is still opt-in; only the authority's role in it changes.
- Do not remove the rule that only the authority simulates, the client renders, or that map scripts run in the authority.
- Do not restore a rule that a map must match the game build. That gate is what made every published map unplayable after a release, and task 09A replaces it with the generation contract.
- Do not change the MIT/UGC licensing statements; task 06A owns the terms.
- Do not add new project conventions that no task asked for. The guards above are the only additions.

## Acceptance criteria

- `AGENTS.md` contains no statement that the authority has no Steamworks, and its Steam map-script section states that the authority's Steamworks is for P2P and admission, while map-script Steam effects still apply on the seat's screen.
- The old "Lobby Reachability" section is gone, replaced by the Steam-lobby and one-transport contract.
- A "Steam-First Platform Boundaries" section contains the guards for platform ownership, deleted services, no Steam DRM, one AppID, Workshop content (including the playtest gate before any publish and the local playtest bar), old-map compatibility, the trusted mark and join code, creator identity, one transport and one identity, and preserving the game.
- No stale reference to `Realm.AdminServer`, `Realm.Shared/Reachability`, `Realm.Shared/Network/Relay`, `LobbyRegistry`, `ServersConfig`, `servers.json`, the account service, the CDN, the seeder network or the admin portal remains as a live instruction.
- Every still-valid convention (ECS, SoA, map API, localization, Godot, concurrency, git footer, tests) is preserved.
- The file is still readable as one document, with no duplicated section and no contradictory pair of rules.
- No code, project file or test is changed by this task.

## Pitfalls

- Do not rewrite the whole file. A wholesale rewrite loses conventions that were never about Steam and makes the diff unreviewable.
- Do not leave both an old and a new rule in place. A contradictory file is worse than an out-of-date one.
- Do not turn the new section into a second architecture document. It is a set of guards; the task files hold the design.
- Do not claim the authority never uses Steamworks. It does, for P2P and admission.
- Do not keep a guest rule in the file "because the code might still need it". A document that describes a guest seat invites one back.
- Do not add a guard that forbids a shared Steam helper assembly. The rule is that the client never references the authority; a Godot-free shared helper is fine.
- Do not remove the offline requirements while adding the Steam-first rules. They are the reason there is no DRM, and they are narrower than they used to be: offline play is local-only.
- Do not add "for now" or "temporary" language to the deleted-service guards. The removal is the decision.
- Do not edit `Terms/` here. Task 06A owns the legal text.
- Do not report this task as complete without searching the file for every term in requirement 5.
