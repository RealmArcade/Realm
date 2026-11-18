# 08 — Consume Workshop Maps and Rebuild the Map Library

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 07.
**Unblocks:** 09, 10, 13.

## User story

As a player, I want to browse Realm maps on Steam Workshop, subscribe to one, watch it download, and then play it, without a Realm account, a map portal, a CDN progress bar or a host uploading files to me.

As a player joining a lobby, I want the game to tell me that the lobby's map is not installed and send me to the Workshop download, instead of waiting for the host to transfer it.

As a player joining a lobby whose map is a local map the host built, I want the host to send it to me, so a map that was never published can still be played.

## Context

Today the client downloads maps from Realm distribution servers and from peer seeders over HTTP, with its own manifest fetch, circuit breakers, disk checks, transfer sessions and host-seeding logic. The map library and map discovery screens are built around that backend.

This task makes Steam Workshop the source of map files. The game keeps its own manifest reader, validation, asset lookup and map upgrade pipeline; it stops owning the transfer.

## Requirements

1. **Subscribed maps are the library.** Read the player's subscribed Workshop items, their install state and their install folders. A map is available when Steam reports it installed. A map that is subscribed but not installed shows a download action and progress. Query and subscribe only under the AppID this build is running, using the item ID that belongs to it (task 09): a map also published under the other AppID is not missing, and its other item is not something this build can fetch.
2. **Browse Workshop.** Query Workshop items for Realm by the app's tags, with sort options for trend, rating, newest and most subscribed. Show title, preview, author, votes/rating, subscribers/favorites, last updated and tags. Support Steam's paged results and search text; do not build a custom discovery feed.
3. **Subscribe and download.** A Subscribe action subscribes and starts the download. Show Steam's download state and progress. A download that is paused, queued or waiting for disk space is stated as such, not shown as a generic failure.
4. **Install folders are read in place.** Resolve a Workshop item to the folder Steam installed it in and register that folder with the existing map manifest/asset system. Do not copy, repack or mirror the map unless the game genuinely needs a writable local copy. If it does, make the copy explicit and keep the Workshop item as the source of truth. An `ISteamUGC` item is a folder downloaded as one unit: there is no per-file API and no cross-item dedupe contract. Do not build a per-asset downloader for Workshop content; the per-asset CAS remains the store for local `.rmap` imports and the editor, not for subscribed Workshop items.
5. **Auto-updates.** When Steam updates a subscribed item, refresh the map entry. A running match is already insulated: it plays the private session copy made when its lobby opened (task 09A), so a Workshop update cannot change the files under it. Do not swap files in a lobby that has already advertised a hash either; if the installed map changes while a lobby is open, the lobby must be cancelled and reopened on the new version rather than started on a mix, and the host is told why.
6. **Lobby maps resolve by one of two paths.** A lobby whose map has a Workshop item ID resolves through Steam: when the player does not have it installed, show the item, its size and a Subscribe/Download action, wait for Steam to install it, then continue the join. A lobby whose map has no Workshop item ID is a local map: if the host has it in CAS, send it over the existing host-transfer path; if neither side has it, refuse the join with a clear message. The host never sends a Workshop map, and a local map never pretends to be a Workshop item.
7. **No dependencies to resolve.** A map carries everything it needs: imports are copied into the map when it is authored, and a composed map already contains the referenced map's files, so there is nothing to install alongside it and nothing to merge at load. `MapMetadata.Dependencies` promises a load-time merge that no code performs, and task 09 deletes it. Do not add a dependency panel, a dependency download prompt or an install-graph check here; a map that is missing something is a broken map, and the remedy is the author's, not a resolution step in the launcher.

    Composition needs nothing from this path and must not be given anything. A composed map's `references.json` travels inside its package and its files are already vendored, so subscribed, downloaded and host-transferred copies all play as one item without the map they were built from. Read `references.json` only as credits (task 18), never as something to fetch: do not resolve it on install, do not offer to download a referenced map, do not compare its `ManifestBlake3` against something on this machine, and do not show a composed map as damaged because a reference names a folder that exists on nobody else's disk.
8. **Offline behaviour.** Installed maps remain playable in Steam offline mode. New subscriptions and downloads require Steam to be online. Do not fall back to a Realm CDN.
9. **Disk space.** Check free space before starting a large download and show a clear message if there is not enough. Do not implement a second downloader that bypasses Steam.
10. **Unsubscribe and removal.** A player can unsubscribe through the UI; the map disappears from the library when Steam removes it. Never delete a map folder the player still owns or a local map outside Steam's Workshop folder.
11. **No public host seeding.** Remove "seed this map", peer seeder, CDN selection and Workshop-map host-transfer UI. Keep the host-transfer path for local maps: it is a one-off join transfer from a host that owns the map, not a public seeding service and not a fallback for a Workshop map.
12. **Trusted marking, not a default filter.** Every map in the library and every lobby in the browser is visible and carries a trust mark: a verified tick for membership in the developer's Verified Workshop collection, a plain trusted mark for a map that meets the configured minimum vote count and score, and a warning mark for a map that is too new, poorly rated, or not on Workshop at all. The Verified collection is the manual override for a good map that is too new to have votes. The threshold starts low (for example 10 votes and 70% positive) and is raised as the catalog grows; it is not a fixed 100. A "Trusted only" filter exists for a player who wants it, but it is off on first run and the game never hides a map unless the player asked. Entering a lobby whose map is untrusted shows one confirmation that names the reason before the join continues, and the same confirmation covers a non-Workshop local map, because nothing on Steam vouches for it. The friction belongs on the join: a new creator's first map stays visible and playable.
13. **Creator discovery.** A creator view is derived from the Workshop items already queried: group by `m_ulSteamIDOwner`, show the creator's maps **for the current AppID**, and link to their Steam profile and Workshop author page. The map details also show the creator's persona, avatar and a short list of their other items for this AppID (task 16). The map's optional Discord server invite comes from its metadata, not from a profile service. There is no global creator directory API; do not build a creator database or registration system in the game.
14. **Creator panel.** The map detail panel and the creator row show the creator's Steam persona and avatar, up to five of their other Workshop items **for the current AppID** (never items from another game), the map's optional Discord server invite, and "View Steam Profile" / "View Workshop" actions (task 16). They open the Steam overlay when available, otherwise the system browser. The game does not fetch or scrape a profile page and does not display donation links. A local map with no author SteamID shows the metadata author and the Discord invite only and hides the Steam actions.

## UX

- The map library keeps its familiar structure: a grid or list with previews, filter tabs for Workshop, Subscribed, Installed and Local, and a map detail panel.
- The primary action is "Subscribe" / "Unsubscribe", with download progress on the card.
- Each card carries its trust mark, and a "Trusted only" toggle sits beside the search box with the filter off by default. A map outside the threshold says why it is marked, rather than disappearing without explanation.
- Joining a lobby for an untrusted or non-Workshop map shows one confirmation naming the reason, with join and cancel; the player is never silently bounced.
- A local map is labelled "Local map" and its primary action is "Host this map", not "Subscribe".
- The map detail panel shows the Workshop title, author, positive percentage and vote count, subscribers, last updated, an "Open on Steam Workshop" link and a creator row with the Steam avatar, persona, "View Steam Profile" and "View Workshop" actions.
- Remove Realm-specific download progress dialogs that describe CDN servers, seeders or peer transfers.
- In a lobby, a Workshop map that is not installed says "This map is not installed" with a Workshop download action. A local map says "The host will send this map", which is a different action and never a CDN or seeder.

## Out of scope

- Publishing (task 07).
- The canonical map identity and version model, and the removal of the declared-dependency field (task 09).
- Deleting the old distribution server and client (task 10).
- Reviews, ratings and moderation (task 13).

## Acceptance criteria

- A player subscribes to a Workshop map and it appears in the library with its preview and metadata.
- A Workshop map can be hosted and played without the host uploading anything.
- A joining player without a Workshop map is sent to the Workshop download and joins after it installs.
- A Workshop update refreshes the library entry.
- Installed maps work in offline mode.
- No HTTP request is made to a Realm distribution server, seeder or peer transfer endpoint.
- The map library no longer shows CDN, seeder or peer-transfer status.
- A local map imported from `.rmap` can be hosted, and a joining player receives it from the host.
- A Workshop map always resolves through Steam; it is never offered as a host transfer.
- Every map is visible by default and carries a trust mark; the "Trusted only" filter is off on first run.
- Joining a lobby with an untrusted map, or with a map that is not on Workshop, shows one confirmation naming the reason before the join continues.
- The Verified collection overrides a low vote count and is the only manual escalation.
- The creator row opens the creator's Steam profile and Workshop page, lists their other items for the current AppID, shows the map's Discord invite when set, and no donation links appear in the game.

## Pitfalls

- Do not keep the custom downloader as a fallback when Steam is slow or unavailable. Show the Steam state.
- Do not promise per-asset incremental downloads or cross-map asset deduplication for Workshop items. Steam downloads an item as a unit; only the local CAS deduplicates.
- Do not copy Workshop folders into the game's data directory on every launch; that wastes disk and breaks Steam's update tracking.
- Do not build a CDN purge or a downloader that bypasses Steam. Banning or marking an item incompatible is the moderation action; only the owner or Valve can delete it.
- Do not use the Workshop title as the map's identity. The item ID is the identity; the title is a label (task 09 makes this explicit).
- Do not start a download in the middle of a running match and replace files the match is reading. The session copy is what protects the match; do not remove it in favour of reading the Workshop folder directly.
- Do not hard-link the session copy to Steam's folder. A hard link shares the file, so an in-place write by Steam would still change the map under the match; make a real copy and verify it against the package hash while copying.
- Do not fetch preview images from a Realm server; use the package thumbnail or Steam's own preview URL.
- Do not build a Workshop browser with its own ranking algorithm. Steam provides search, sort and paging.
- Do not fetch or scrape the creator profile page, and do not display donation links. The creator row opens Steam.
- Do not build a creator database. The other-item list is a bounded Steam query for the current AppID, cached per session.
- Do not show a creator's Workshop items from another AppID. Pass the current AppID to `CreateQueryUserUGCRequest` and ignore anything that comes back for a different app.
- Do not turn the Discord invite into a link list or add a website field. It is one optional map metadata value.
- Do not query Workshop details for every lobby on every refresh without caching. Cache per item for the session and refresh on demand.
- Do not default the library or the lobby browser to a filtered view. Hiding a new creator's only map behind a threshold nobody explained is a worse first experience than showing one weak map with a warning mark.
- Do not describe an untrusted map as unsafe. The map script is sandboxed either way; the warning is about quality, maintenance and votes, not about the sandbox.
