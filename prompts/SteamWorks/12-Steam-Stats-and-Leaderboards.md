# 12 — Move Persistent Stats and Leaderboards to Steam

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 11.
**Unblocks:** 15.

## User story

As a player, I want my wins, losses and best results to persist across sessions and appear on the same profile Steam shows for me, without a Realm account or a Realm leaderboard server.

As the developer, I want the custom match-stat storage and ranking endpoints gone, with a small, bounded set of Steam stats and leaderboards replacing them.

## Context

The old account service stored per-player match results and the portal stored per-map stats and engagement. With accounts gone, those records have no owner. Steam Stats and Steam Leaderboards provide the persistent player-facing surface.

This is not a replacement for the in-match scoreboard. The scoreboard is a game feature and stays exactly as it is.

## Requirements

1. **Define a small stat set.** Player-facing totals such as matches played, matches won, matches lost and a small number of per-mode or per-faction counters. Keep the set fixed and documented; Steam stats are not a general-purpose database.
2. **Write stats from the owning client.** Steam stats are per-user and are updated by that user's client. After a match ends, the client records the result and stores it. The authority may send the result to the client as part of the match summary, but the client is the one that writes to Steam.
3. **Be honest about verification.** Steam stats are client-uploaded. If the game needs anti-cheat-verified rankings, keep the smallest possible server-side match-result record and document it as the exception. Do not rebuild the account service, a general stats API or a ranking database for this. For a casual RTS, Steam stats are sufficient.
4. **Leaderboards are bounded.** Create leaderboards at startup or on demand for a fixed set, such as global wins, or one board per official mode. Do not create one leaderboard per Workshop map: the map count is unbounded, and Steam does not document a per-app leaderboard cap you could measure against, so the bound is a rule this project keeps rather than a threshold it discovered. If per-map scores are essential, use a single board with the map's Workshop item ID as a detail, or only for official maps.
5. **Leaderboard UI.** Show global and friends filters where Steam supports them, the player's rank and score, and a clear "Steam stats" label. Keep the existing visual style; only the source changes.
6. **Achievements stay on the map API.** The existing `IGameAPI` Steam achievements, stats and cloud files are a map-script feature. Do not duplicate them in the new stats service and do not change the map scripting ABI.
7. **Separate namespaces for the game and for maps.** The global stats and achievements this task adds live under their own prefix, distinct from `SteamScriptNames.ForMap`. A map script writing its own stat must never be able to overwrite a player's lifetime total, and a base-game achievement must never collide with one a map defines. Choose the prefix once, document it, and keep the map-script namespacing rule unchanged.
8. **Offline behaviour.** If Steam is offline, show the last known values or "Steam stats unavailable offline". Do not queue an unbounded list of pending uploads; a failed store is retried on the next session at most.
9. **One live AppID.** Steam stats are scoped to the AppID. The official AppID already exists and its release is deferred; the demo is the publicly listed AppID until then and is unpublished afterwards, so demo-era stats do not carry over. Treat them as throwaway pre-release data and do not build a cross-AppID stats service.
10. **Remove the old stats storage.** Delete the account match-stat records, their endpoints, the portal's map metrics, engagement tracking and any admin views that read them.
11. **Keep the local match summary.** The end-of-match screen still shows the result and the scoreboard for that match. It reads the match's own data, not Steam.

## UX

- The profile shows Steam-backed totals and a link to the Steam profile.
- The leaderboard screen shows Steam leaderboards with the player's rank; it does not show a Realm server list.
- A stat that could not be stored says so once, without a modal loop.
- The end-of-match screen still looks familiar; it does not show Realm account IDs, MMR or a custom rank.

## Out of scope

- The in-match scoreboard and match summary design.
- Map scripting achievements, stats and cloud files.
- Workshop discovery and ratings (task 13).
- Deleting the admin server (task 15).

## Acceptance criteria

- Player-facing totals persist across sessions through Steam.
- The leaderboard screen reads Steam and shows global/friends views.
- No custom stats or leaderboard endpoint is called.
- The old account match-stat records and portal metrics are deleted.
- Offline mode degrades cleanly.
- The in-match scoreboard is unchanged.
- The solution builds with no account-stat references.

## Pitfalls

- Do not treat Steam stats as trusted anti-cheat data. They are client-reported.
- Do not create a leaderboard per UGC map. The map count is unbounded.
- Do not store Steam persona names as stat keys; use SteamID64.
- Do not build a season system, MMR ladder or matchmaking rating in this task. Those are game-design features and need their own decision; Steam does not provide them.
- Do not delete the map-script Steam surface. It is separate from this task.
