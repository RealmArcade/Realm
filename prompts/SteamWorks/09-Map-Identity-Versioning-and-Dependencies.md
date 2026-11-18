# 09 — Make the Workshop Item the Map's Identity

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 08.
**Unblocks:** 09A, 10, 13.

## User story

As a player, I want two maps with the same name to be distinguishable by their Workshop page and author, and I want a lobby to refuse me with a clear "update this map" message instead of starting a match that desyncs.

As a map author, I want my Workshop item to be the permanent identity of my map, with its version visible, so renaming the map or moving the folder never breaks who can play it.

## Context

Realm currently treats a map's display name as its identity, reserves names for creators, stores its own versions in the portal, and downloads exact versions from its own CDN. Workshop changes the model: the published file ID is stable, the title is only a label, and Steam always serves the latest installed version.

This task defines the identity and compatibility contract the rest of the Steam-first game uses.

## Requirements

1. **The Workshop item ID is the canonical key, and a map may hold one per AppID.** A map published in the demo era is written twice — into the demo's Workshop, which demo players can subscribe to, and into the official AppID's Workshop, dormant until release (task 07) — so the manifest stores both published file IDs rather than a single field. The running AppID selects one: a demo build uses the demo item, the released build uses the official item. A map that holds only the other AppID's item is not broken and not deleted; it plays from its installed content, and its Workshop link and subscribe action are shown only when the item belongs to the current AppID. The game may also index by display name for local convenience, but every network and lobby reference uses the item ID for the current AppID. **Reading never crosses AppIDs** — a query, a subscribe or a details request is always made under the running AppID. The one documented exception is a *write*: task 07 passes the target AppID to `CreateItem`/`StartItemUpdate` so a demo client can create the dormant official item. Keep that exception in task 07 and nowhere else, so this contract stays one rule plus one named write path.
2. **Titles are labels, not identities.** Two Workshop items may share a title. The lobby, map picker and map details show the title with the author, not a globally reserved name. Remove the name-reservation and creator-registration logic.
3. **Manifest schema.** Add the Workshop item ID to the manifest model and let the project's schema generator produce the schema. Do not hand-edit the generated schema. If an existing manifest needs the field, add a sequential `MapUpgradeService` migration; do not add a fallback reader.
4. **Version and hash.** Keep the map version and the manifest hash. The hash is what the match actually plays; the version is for humans. A Workshop update changes the hash.
5. **Lobby compatibility.** Steam lobby metadata carries the Workshop item ID, the manifest hash and the map's `EngineGeneration` (task 09A). On join, compare the local installed map: same item ID and same hash means compatible. A missing Workshop map sends the player to the Workshop download; a missing local map is requested from the host. A different hash means the host's installed copy and the joiner's are not the same build of the map, and the remedy is owned by the host: cancel the lobby, let Steam update the map, and create the lobby again. Say exactly that. A map from a newer `EngineGeneration` than the player's game says the player needs a newer version of Realm. An older generation is not a mismatch: task 09A upgrades the map's data on a private copy, so do not refuse it here. Do not start the match on a real mismatch.
6. **Steam does not serve old versions, and the game does not try to fetch them.** Do not build a version-pinning, downgrade or old-version download service. The compatibility guarantee for an old map is task 09A's (a migratable data format and an additive map-script surface), not a second copy of the map. Never re-upload, fork or duplicate another creator's Workshop item to pin a version; that is a new identity and it belongs to the mirror policy (task 17), not to the lobby.
7. **The host plays a private copy.** The map the authority is given is a session copy made when the lobby opens (task 09A), not the Workshop folder itself. Steam owns the installed folder and may replace it at any time; the copy is what keeps a running match stable and what gives the lobby a hash it can advertise. Do not copy on every launch and do not copy at match start; the lobby creation step is the one place it happens.
8. **Nothing resolves another map at load time, and the dead field is deleted.** Reuse across maps already works, by composition, and that must keep working. A map folder keeps `references.json` (`MapReferences`), and on every save `MapReferenceResolver` copies the referenced map's files into the folder and makes them read-only, `MapMetadataMerger` deep-merges its `metadata.json` while keeping the parent's own overrides in `metadata.partial.json`, and `MapScriptComposer` rewrites its C# into `composed/<Map>/` with a `MapComposition.g.cs` registry. That is vendoring, not linking: references are pinned by `ManifestBlake3`, composition is one layer deep so a referenced map's own references are not followed, `FolderPath` is cleared in the packaged form because it is a folder on the authoring machine, and repointing a reference at a newer version re-applies only the overrides the parent actually stated. Everything downstream - testing, export, publish, download, play - reads one ordinary self-contained folder and resolves nothing.

    `MapMetadata.Dependencies` is a second, unused vocabulary for the same idea, which is worse than dead code: its schema description promises that a dependency's content "is merged into this one before the map's own templates are resolved", nothing reads it, and an author who fills it in expects a merge that never happens while `references.json` is what actually does the work. Delete the field, its `MapDependencyMetadata` type, the helpers in `MetadataService` and `AuthorSignatureHelper`, and the generated schema entries, through a sequential `MapUpgradeService` migration and with no fallback reader. Do not build an install graph, a resolution order, a missing-dependency prompt or a dependency list in the map details: there is no load-time merge for them to serve, and adding one would create the coupling this deletion exists to remove.
9. **Authorship.** The Workshop item owner is the author of record. Show the owner's persona name in the map details and credits. Task 18 replaces the old public-key signature with SteamID-keyed attribution; do not require a signature to publish, install or join. The map details and the credits panel show the creator's Steam persona and avatar, link to their Steam profile and Workshop page, and show the map's optional Discord server invite (task 16); the Steam page is not a map ownership record and the game does not display donation links.
10. **Local-only maps.** A map that has never been published has no Workshop item ID. It can still be opened, edited and played locally. If the host has it in CAS, the lobby can host it and a joiner receives it over the host-transfer path; if the host does not have it in CAS, only players who already have the same files can join. Warn that the map is not on Workshop and is not publicly downloadable; do not auto-publish it.
11. **Map picker.** The host and players pick maps by Workshop item (title, author, preview), not by a globally unique map name. The map details show the Workshop ID, version, hash, generation and size.
12. **No portal identity.** Remove creator registration, name reservation, map ownership claims and portal versions. Steam owns the item and its author, and a map declares no load-time dependency on another map: reuse is vendored when the map is authored, by copying or by composition (requirement 8).
13. **Steam's update metadata is not the map version.** The item's `revision_change_number` or `time_updated` is a cache hint, not the manifest hash. The hash the match plays is the version contract; the change number only tells the library that the item may have changed.
14. **Attribution metadata.** Imported assets and composed maps carry the source author's SteamID and source Workshop item ID (task 18). The credits panel uses that metadata; it does not need a signature.

## UX

- Map details show "Workshop: <title> by <author>", the item ID in a copyable/advanced field, the version and the last update.
- A lobby whose map is missing or outdated shows a single primary action: "Subscribe" or "Update on Workshop".
- The error for a hash mismatch says "The host has a different version of this map installed. The host needs to update it on Steam Workshop and open the lobby again."
- The error for a newer map generation says "This map needs a newer version of Realm. Update the game and try again."
- A map from an older, still-supported generation opens normally and shows a small "Made for an earlier version of Realm" note in its details.
- Local-only maps are marked "Local map" and, when the host has them in CAS, "The host will send this map"; when it does not, "Local only — not downloadable by other players".

## Out of scope

- Publishing and uploading (task 07).
- Browsing and downloading (task 08).
- Old-map compatibility, the generation field and the load-time upgrade (task 09A).
- Deleting the old distribution backend (task 10).
- Reviews and discovery ranking (task 13).

## Acceptance criteria

- Two Workshop maps with the same title can coexist and are distinguished by item ID and author.
- A lobby records the item ID, hash and `EngineGeneration`, and refuses a mismatched player with a clear message.
- A missing map sends the player to the Workshop download and then completes the join.
- An older-generation map opens normally from a private session copy, and the Workshop folder is untouched.
- The `Dependencies` field and `MapDependencyMetadata` are gone from the model and the generated schema, the `MapUpgradeService` migration builds, and no screen offers a dependency list, prompt or download.
- A local-only map still loads and plays locally; hosting it either sends it from the host's CAS or warns that others cannot download it.
- The map details link to the creator's Steam profile and Workshop page, and the credits list imported assets by source author.
- No creator registration or name-reservation code remains.
- The manifest round-trips the Workshop item ID, and the migration path builds.

## Pitfalls

- Do not use the Workshop title as a filename or a key. Titles change and collide.
- Do not try to download an older Workshop version to match a host. Steam serves the installed content; the host cancels the lobby, updates, and opens it again.
- Do not treat an older map generation as a mismatch. That is the compatibility path, and refusing it re-creates the build lock this migration removes (task 09A).
- Do not make the hash the only identity. The item ID says which map; the hash says which build of it.
- Do not require a signature for Workshop maps. Steam's item ownership is the attribution and the access control.
- Do not treat the Steam profile as a map ownership record. The Workshop item owner is the author; the profile is a page Steam hosts.
- Do not reintroduce a load-time cross-map reference: no dependency list, no shared asset library resolved from another map, no "requires map X" prompt, no install step before a map can run. Composition is the supported mechanism and already avoids this by vendoring on save, so do not replace it with linking, do not make a downloaded map resolve its `references.json` before it plays, and do not treat an unresolvable reference as a broken install. A map that needs another map's art or script composes it or copies it, with attribution (task 18). A merge performed at load would make every map's working state depend on somebody else's version, which is the same failure a shared library had before package managers, and this format has none of the versioning machinery that fixes it.
- Do not require a Workshop item ID for a local map. The absence of the ID is what selects the host-transfer path.
- Do not resolve an item ID across AppIDs. An item belongs to one app, so asking Steam for the other app's item returns nothing; treating that as "the map was deleted" would hide a map the player can still play.
- Do not treat Steam's change number as the map version. It changes for reasons that are not a new map build.
