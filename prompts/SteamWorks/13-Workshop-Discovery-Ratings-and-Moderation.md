# 13 — Replace Discovery, Reviews, Greenlight and Moderation with Workshop

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 07A, 08, 11 and 09B (the authorship line and the playtest line it shows beside the trust mark).
**Unblocks:** 15.

## User story

As a player, I want to find maps the way I find anything else on Steam: search, tags, up/down votes and trending, with the Workshop page carrying the description, screenshots, comments and reports. I do not want a second Realm review site with its own stars, awards, playtime charts and greenlight gate.

As a player, I also want a way to hide maps that are too new or too poorly voted to trust, without a custom review system.

As the developer, I want to stop operating a map portal, a review moderation queue and a greenlight system, and let Steam's Workshop moderation and ranking do that work.

## Context

The custom map portal has grown into a small storefront: a discovery feed, star ratings, verified written reviews, a 100-verified-review greenlight threshold, awards, screenshots, playtime and engagement metrics, creator profiles, map maintainers and admin greenlight endpoints.

Steam Workshop already provides the listing, preview, description, tags, search, rating, favorites, comments, reporting and moderation. This task keeps the map-detail experience but replaces the storefront behind it.

## Requirements

1. **Discovery is Workshop search.** Browse and search map items by title, tags, trend, rating, newest and most subscribed. Use Steam's paging and filters. Do not keep a Realm discovery feed or a Realm ranking algorithm.
2. **Ratings are Workshop up/down votes, not stars.** Show a positive percentage and a vote count (`m_unVotesUp` / `m_unVotesDown`), plus favorites/subscribers. Remove the Realm star rating, the "verified review" badge and the written-review system, including their UI: the end-of-match star picker is `Realm.Client/UI/GameOver.ReviewPopup.cs` and the submit path is `GameOver.Metrics.cs`. Delete both with the backend, or a star widget survives with nothing to send to and a player gets a prompt for a review that cannot be posted. Do not synthesise a 1-5 star widget from `m_flScore`; that would be fake precision on a Bayesian average.
3. **Reviews become Workshop comments.** Do not embed a review composer, a star picker or a text body. Provide an "Open on Steam Workshop" action where the player can read and write comments in Steam. Remove review moderation tools.
4. **The hosted greenlight disappears; the playtest bar in front of publication does not.** There is no 100-review threshold, no `[Beta-Testing]` prefix, no greenlight request, no review queue, no admin endpoint and no Realm approval step, because all of them lived in the deleted server. What replaces them is task 07A: an item stays Unlisted until enough different Steam accounts have played a hosted match of it, and that record is a file on the author's own machine. This task does not re-implement, re-check, verify or replicate that record. It may *display* the result — one small "Playtested" line, using the `greenlit` tag task 07A writes, and an optional sort or opt-in view via `AddRequiredKeyValueTag("greenlit", "1")` — and it must never make it the default view, never gate a join on it and never treat the tag as a trust signal, because the creator writes that tag and the developer does not. For a quality signal, use the trust mark below.
5. **Creator views are Workshop-derived, with Steam profile links.** Remove creator registration, the old creator portal pages, maintainer lists and name reservation. The item owner is the author; group Workshop items by `m_ulSteamIDOwner`, query that owner's published items with `CreateQueryUserUGCRequest` **for the current AppID only**, and link to their Steam profile and Workshop author page. Steam has no global creator directory, so this is a derived view, not a complete index. Each creator row shows the Steam persona and avatar, up to five other published items **for this AppID** (never items from another game), the map's optional Discord server invite, and "View Steam Profile" / "View Workshop" actions (task 16). The game does not store profile fields or display donation links; it only reads Steam, opens Steam's pages and opens the one Discord invite declared by the map.
6. **Awards, screenshots and playtime charts are removed.** Keep one preview image per item. If more images are useful, put them in the Workshop description. Do not build an award system or a playtime/engagement metric to rank maps.
7. **Moderation is Steam's.** Use Workshop reporting and the partner-site ban or mark-incompatible tools. In-game, provide a "Report on Steam Workshop" link for a map. Realm can ban an item (visible only to its owner) or mark it incompatible with the game hub; it cannot delete another user's item. Only the owner or Valve can delete it. Remove the custom report form, the admin review queue, the greenlight endpoint and the map-management portal. A map can have two items — the demo-era one and the dormant official one (task 07) — and Steam's actions apply per item, so a ban or an incompatible mark must be applied to both and recorded against both IDs, or the content you removed surfaces in the official catalogue at release.
8. **A curated mark may verify a map, and Steam stores it.** The preferred source is the partner site's per-item **accepted-for-use** flag, exposed as `m_bAcceptedForUse` in `SteamUGCDetails_t`: only the developer can set it, so unlike a key-value tag or a vote count a creator cannot forge it, and it needs no collection, no shipped build and no service. A developer-owned Workshop collection read with `GetQueryUGCChildren`, or a local configuration file, remain the alternatives if the flag cannot be filtered server-side in the target SDK; test which of the three the SDK actually supports and record the answer in the task report instead of assuming it. Whichever is used, this is a curation choice, not a voting system, and it must not become a public admin API or a publish gate. It is also the direct answer to "every official item starts at zero votes": vouch for the maps already reviewed rather than lowering the vote minimum.
9. **Map details keep the useful fields.** Show the Workshop title, author, preview, positive percentage and vote count, subscribers, last updated, tags, size and an "Open on Steam Workshop" link. Remove Realm-only ratings, reviews, awards, playtime, creator badges and maintainers.
10. **Delete the portal backend.** Remove discovery feed endpoints, ratings, reviews, awards, screenshots, the old creator portal pages and endpoints, maintainers, greenlight requests, admin greenlight endpoints and map metrics. There is no external Realm creator profile; the creator links open Steam (task 16). Task 15 removes the remaining admin server.
11. **Trusted marking, not a default filter.** Trusted means the developer's verified mark (requirement 8), or a configured minimum vote count and minimum score. The verified mark is the manual override for a good map that is too new to have votes. The threshold starts low and is raised as the catalog grows; it is not a fixed 100. This is a presentation policy, not a platform primitive. Every map and every lobby is visible by default and carries a trust mark; a "Trusted only" filter exists for a player who wants it, and it is off on first run. Entering a lobby whose map is untrusted, or whose map is not on Workshop at all, requires one confirmation that names the reason (too new, poorly rated, unmaintained, or not published) before the join continues. A local map keeps its "Local map" badge and is untrusted for this purpose, because nothing on Steam vouches for it. Hide nothing without the player asking, and never describe an untrusted map as unsafe: the map script is sandboxed either way and the warning is about quality and maintenance.

    The authorship result (task 09B) is a second, independent line on every map and lobby: signed or unsigned, whether the package still matches its signature, whether the key is the one this machine already knows, and whether Steam confirmed it. Do not fold it into the trust mark, do not let a good vote score imply a signature, and do not let the Verified collection override a broken signature — curation says the map is good, not that this copy is the one its author made.
12. **Votes belong to an item, so a dual-published map has two scores.** The demo item and the dormant official item are separate items with separate votes and subscribers, and nothing carries between them (task 07). Compute the trust mark from the item the running AppID actually sees, never blend the two counts, and never read a map's empty official item as evidence the map is unproven. Every map starts at zero votes on its official item, which is exactly why the verified mark exists and should be populated before the official catalog opens. Set the launch posture deliberately: for the first weeks after release, trust comes from the verified mark rather than from a vote minimum, because a catalogue in which every map carries an untrusted warning is a worse first impression than a curated one that is. Greenlit and verified are separate facts and stay separate lines: greenlit says people played this, verified says the developer vouched for it, and only the second one is unspoofable.
13. **No CDN purge.** Steam hosts the content. Banning or marking an item incompatible removes it from the game's surface; only the owner or Valve can delete it. Do not build a purge service. If a Realm mirror exists, prune the mirror on its own schedule, not the Workshop item.

## UX

- The map browser looks like a Steam Workshop browser inside the game's visual style: search, tag filters, sort, cards with previews and a positive percentage with a vote count.
- The map detail panel has one primary action: Subscribe/Unsubscribe or Play, plus "Open on Steam Workshop".
- There is no "Write a review" button, no star widget, no award badge and no greenlight progress bar.
- A "Verified" badge marks a map the developer accepted for use (or listed in the developer's Workshop collection); it is separate from the vote count and is not a promise of quality.
- A small "Playtested" line marks a map whose author promoted it after task 07A; it is a separate line from Verified, is available as an optional sort, and is not the default view.
- Each map and lobby carries a trust mark, and a "Trusted only" toggle is available in both the map browser and the lobby browser with an explanation of what the mark means. The toggle is off by default.
- Entering a lobby for an untrusted or non-Workshop map shows one confirmation naming the reason, with join and cancel. Leaving is always allowed.
- A local map is labelled "Local map" and does not show a positive percentage.
- The map detail panel and each creator row show the Steam persona and avatar, "View Steam Profile" / "View Workshop" actions (task 16) and, when set, the map's Discord invite. The other-item strip is limited to the current AppID. No donation links are shown in-game.
- Reporting a map opens the Workshop page; the game does not collect reports itself.

## Out of scope

- Publishing and downloading (tasks 07 and 08).
- Map identity and compatibility (task 09).
- Persistent stats and leaderboards (task 12).
- Deleting the whole admin server (task 15).

## Acceptance criteria

- Map discovery uses Workshop search, tags and sorting.
- Ratings and favorites come from Workshop up/down votes; the custom review/star system is gone, including `GameOver.ReviewPopup.cs` and the review submit in `GameOver.Metrics.cs`, with no orphaned star control or localization key left behind.
- No greenlight, beta prefix, awards, screenshots gallery, playtime metric or creator registration remains.
- Reporting and moderation link to Steam Workshop; the ban and incompatible actions are documented.
- The verified mark is the accepted-for-use flag, a Workshop collection or a static curation list, not a voting service or a publish gate.
- The playtest state from task 07A is shown as one line and an optional sort only, never as the default view and never as a join gate, and discovery works on a machine that has never hosted a match.
- The authorship result from task 09B appears separately from the trust mark, and the Verified collection never substitutes for it.
- Every map is visible by default and carries a trust mark, and the Verified collection overrides a low vote count.
- Entering an untrusted lobby, or one whose map is not on Workshop, requires one confirmation that names the reason.
- The "Trusted only" filter is available in both places and is off on first run.
- The creator links open the creator's Steam profile and Workshop page, the other-item strip contains only items for the current AppID, the map's Discord invite is shown when set, and no donation links are displayed in-game.
- A local map is marked "Local map" and is excluded from the trusted view by default.
- The map detail panel shows Workshop metadata and no Realm portal metadata.
- The solution builds with no review/greenlight/admin portal references.

## Pitfalls

- Do not build a "verified review" equivalent. Steam comments are not purchase-verified, and recreating verification would require the account service that task 11 deleted.
- Do not fake 1-5 stars from Steam's Bayesian score. Show the positive percentage and vote count.
- Do not keep a custom rating table "for sorting". Use Steam's score and sort options.
- Do not make the verified mark a hidden voting threshold or a publish gate. It is a curation choice, ideally reviewed manually, and it is the one trust bit a creator cannot write.
- Do not read, verify or re-implement task 07A's playtest ledger here, and do not gate discovery on it. Discovery is Steam's and must work with the local ledger absent, which is the normal case on a player's machine.
- Do not confuse the two marks. The `greenlit` tag is creator-written and forgeable; accepted-for-use is developer-written and is not.
- Do not default discovery to the trusted subset. The friction is a confirmation on the join, not an empty browser for a creator whose first map has no votes yet.
- Do not build a public admin website for featuring or hiding maps. A Workshop collection or local config is enough.
- Do not assume Realm can delete a Workshop item. It can ban or mark incompatible; only the owner or Valve can delete.
- Do not build a CDN purge service. If a Realm mirror exists, prune the mirror only.
- Do not build a creator database in the game. Derive creator views from Workshop items and link to Steam's profile and Workshop pages (task 16); there is no Realm profile data.
- Do not query without the current AppID. A SteamID is global, so an unfiltered creator query shows that person's maps for other games. Reading never crosses AppIDs; the single exception in the whole codebase is the publish write in task 07, which passes the target AppID to `CreateItem`/`StartItemUpdate` deliberately.
- Do not carry a demo-era vote count or score onto the official item, and do not average the two. They are different items; the count starts again and the Verified collection covers the gap.
- Do not add a general link list to the creator row. The only extra creator link is the optional Discord invite in the map's metadata.
- Do not delete the map detail panel's useful fields (author, size, votes) along with the portal-specific ones.
- Do not merge authorship into the trust mark. A curated map can be an unsigned copy and an unproven map can carry a valid signature; one badge for both tells a player neither fact.
