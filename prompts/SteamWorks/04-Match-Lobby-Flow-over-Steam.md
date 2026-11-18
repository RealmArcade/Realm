# 04 — Keep the Match Lobby Flow, Move Its Membership to Steam

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 03.
**Unblocks:** 05, 11, 14.

## User story

As a player, I want the lobby room I already know — seats, teams, ready state, map settings, chat and the host's start button — to keep working exactly the same way, even though the lobby is now a Steam lobby underneath.

As the host, I want Steam to tell me who is present and Realm to decide where they sit and when the match starts.

## Context

Task 03 makes the Steam lobby the directory. This task makes the authority and the lobby protocol agree on membership without reintroducing a registry or a session service.

The split is:

- **Steam lobby membership** says who is present, who owns the lobby and who is invited.
- **Realm's lobby protocol** says which seat each member holds, which team and faction they have, which settings the match will use, whether they are ready, and when the match starts.

Neither side may become a second copy of the other.

## Requirements

1. **Membership reconciliation.** When a player enters or leaves the Steam lobby, update the Realm roster: a new member waits for a seat or takes an empty one, and a departing member frees their seat and is removed from the player rows. Use Steam's lobby membership callbacks, not a periodic HTTP refresh.
2. **Admission is Steam lobby membership.** A peer that connects to the authority is admitted only if it is a member of the host's Steam lobby or is the host's own loopback seat. That single rule is what makes every seat an account Steam can attribute and moderate, and it is the reason the lobby needs no pending-approval state, no guest mark and no second identity model. The authority should use the Steam lobby it can see as the host user. The Steam lobby ID travels to the authority in the existing match-start control command, alongside the roster and settings. If the task 00 pivot leaves the authority without Steam matchmaking, the host's client supplies the member list over the trusted loopback control channel; do not build a new authentication service.
3. **Seat assignment stays a game decision.** Steam membership does not decide seats, teams or colours. The existing host controls, auto-seat rules and map-defined seating remain authoritative.
4. **Ready and settings stay in the lobby protocol.** Map choice, mode, shroud, teams-together and all map-declared settings continue to travel through `LobbyProtocol`. Steam metadata is only the browser advertisement and is updated when those settings change so the browser stays accurate.
5. **Start is an authority action.** The host starts the match through the existing start flow. The authority transitions from lobby to match, stops accepting new peers unless join-in-progress is supported, and marks the Steam lobby as no longer joinable. The match still runs on the host's authority; there is no server migration.
6. **Join-in-progress is not invented.** If the current game supports joining a match in progress, preserve that behaviour through Steam membership and authority admission. If it does not, close the lobby at start and show "This match has already started." Do not add a new join-in-progress feature in this task.
7. **Kick and ban remain player-visible features.** A host can remove a disruptive player. Removing a player takes their seat, disconnects their peer and refuses their reconnection. Steam does not provide a lobby kick, so keep a small per-lobby ban list held by the authority (or in lobby metadata) for the life of the lobby. Do not close and recreate the lobby to kick one player, and do not build a moderation service.
8. **Chat.** Pre-match chat remains in the lobby room, backed by Steam lobby chat. In-match chat remains on the game channel. System lines such as "joined", "left" and "ready" stay in the lobby room, but they describe game events, not Steam internals.
9. **Host's own seat.** The host's client is the Steam lobby owner and the authority's administrator. It connects to its authority over the loopback path. It must appear in the roster like any other player, with the host marker and the same seat controls.
10. **Failure handling.** A Steam lobby that closes, a host that loses Steam, or an authority that fails to start must return every player to the menu with a readable message. No player should be left in a lobby room whose host is gone.
11. **There is no seat outside the lobby.** The seat grid, ready state and start flow therefore have one membership rule. A player who cannot reach the lobby through Steam is not shown a way to reach it another way, and the client has no address dialog, no port field and no host-approval path for a peer Steam did not authenticate.
12. **Join code is an authority-side seating gate.** When the host set a join code (task 03), the authority refuses every seat until that screen has presented the code, including a member who arrived through Steam. Compare it on the authority, never on the client, and never send it to a screen that has not already presented it. A wrong code is a refused seat with a retry, not a disconnect: the player stays in the room and can try again. The host can change or clear the code before the match starts, and the change reaches the room as a normal lobby state update. If the code is cleared, seats become available without a retry. Do not add a second gate beyond the code: Steam membership and the code are the only two admission decisions.

## UX

- The lobby room looks and behaves as it does today: seat grid, teams, ready state, map panel, settings, chat, start button and host controls.
- The player rows use the Steam persona name and avatar. Remove the account badge, the session status and the connection address.
- The host can still kick, change settings, reorder seats and start.
- When a player leaves the Steam lobby, their seat is freed and the room shows that they left.
- When the match starts, the lobby is visibly closed to new players.
- The lobby browser has one way in: the Steam lobby list. The "Join Private Lobby via IP Address" action and its address dialog are gone, and nothing replaces them.

## Out of scope

- Creating, browsing and inviting through Steam (task 03).
- Deleting the old registry and session-token path (task 05 and task 11).
- Changing the match rules, map seating rules or simulation.

## Acceptance criteria

- Two to eight players can enter a Steam lobby, take seats, ready up and start a match.
- A player leaving the Steam lobby frees their seat and is removed from the roster.
- A host can kick a player; that player is disconnected and cannot rejoin that lobby.
- The host's own seat appears in the roster and is playable.
- The game build, the map's `EngineGeneration` and the map settings remain authoritative and are enforced at start.
- A closed or failed lobby returns players to the menu with a clear message.
- Every seated player is a member of the host's Steam lobby; no peer can be seated any other way.
- A lobby with a join code seats nobody until each screen presents it, and a cleared code releases the seats without a reconnect.
- The lobby flow makes no HTTP call to a Realm registry.

## Pitfalls

- Do not make Steam lobby membership the seat table. Steam does not know teams, factions or seat order.
- Do not poll the Steam lobby every frame for membership; use the callbacks.
- Do not let a kicked player remain in the match because their Steam lobby membership persists. Remove the seat, disconnect the peer and refuse the reconnect.
- Do not admit a peer that is outside the host's Steam lobby, and do not add a pending-approval state for one. There is no guest seat to admit.
- Do not check the join code on the client or store it in Steam metadata. The authority holds it for the lobby's lifetime and the screens only ever send what the player typed.
- Do not add join-in-progress just because Steam makes it possible. Match rules and snapshot admission decide that.
- Do not implement host migration. The authority is a process on the host's machine; the match ends when that host leaves.
