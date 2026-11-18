# 03 — Replace the Lobby Registry with Steam Lobbies

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 01, 02.
**Unblocks:** 04, 14.

## User story

As a player, I want to press Host and have my lobby appear in my friends' Steam friends list and in Realm's lobby browser, and I want to join a lobby by clicking Join or accepting an invite, without ever seeing an IP address, a port, a region server or a password prompt.

As the developer, I want the lobby directory to be Steam Matchmaking, so there is no registry to host, no heartbeat, no GeoIP and no server list to keep alive.

## Context

Realm currently creates a Steam lobby only as an invite envelope: it stores the ID of a separate Realm lobby in the Steam lobby metadata. The real lobby lives in a custom HTTP registry, with heartbeats, GeoIP, reachability classes, join tickets and a browser that polls Realm servers.

This task makes the Steam lobby the real lobby. It owns the directory, membership, metadata, invites and chat. The Realm lobby protocol still owns seats, teams, ready state, settings and match start; that is task 04.

## Requirements

1. **Create through Steam.** Hosting creates a Steam lobby with the correct visibility: public for the browser, friends-only for friends, private for invite-only, invisible for hidden. Set the maximum members from the map's playable slots, capped at Steam's lobby limit. **A code-gated lobby needs headroom over its seats.** Requirement 6 admits a member to the room before the code is entered, so a room sized exactly to the seats fills with people who cannot play and refuses the ones holding the code. With a join code set, allow the playable slots plus a small headroom (two is enough for an event), and once the seats are taken stop admitting new members with `SetLobbyJoinable(false)` rather than letting the room keep filling. Without a code, the limit is exactly the seat count.
2. **Lobby metadata is the game's advertisement.** Store the map name, map version, map hash, map `EngineGeneration`, game build number, protocol version, mode, seat count, host SteamID and any short settings the browser needs. Keep every value short: the documented limits are 8,192 bytes per value (`k_cubChatMetadataMax`) and 255 characters per key, and Valve documents no separate total budget, so treat the budget as "short strings only" rather than measuring against a number that does not exist. Do not store a full match configuration blob that belongs in the lobby protocol.
3. **Browse through Steam.** The browser requests Steam lobbies with filters for game build, protocol, map and mode, and a distance filter where useful. Show the host persona, map, mode, current/max players and a join button. Steam returns a bounded result set; show up to that bound and let filters narrow it. Do not build pagination, a region server or a ping column.
4. **Join through Steam.** Joining enters the Steam lobby, reads its metadata, and then connects to the host's SteamID through the task 02 transport. A join that cannot enter the Steam lobby explains why: it closed, it is full, it is friends-only, or the game build differs.
5. **Invites are Steam invites, and any seated player can send one.** The lobby room has an Invite Friends action that opens the Steam invite dialog for the current lobby. Every seated player has it, not only the host: a friends-only Steam lobby is joinable by friends **and by invitees**, and `InviteUserToLobby` works from any member, so a player who brought their own friend does not have to route the invite through the host. Accepting an invite enters the Steam lobby and then the match lobby. The existing `+connect_lobby` launch path should work while the game is running and at startup. Visibility, settings and the start stay with the host, who is the Steam lobby owner.
6. **Friends-only replaces the password; one optional join code replaces the event gate.** A host who wants a private match uses friends-only or private plus invites, and there is no password prompt for an ordinary lobby. Keep one optional join code, because a community event announced in Discord cannot be gated any other way: the host sets a short code when creating a public or friends-only lobby, and the authority refuses a seat until the joining screen presents it. The code is never written to Steam lobby metadata, because that would publish it; it is not a credential, it is not stored beyond the lobby's lifetime, and a lobby that has one still appears in the browser with its requirement shown. The gate is on seating, not on Steam membership: a member may enter the room and see it, and cannot take a seat or start until the code is entered.
7. **Chat.** Pre-match lobby chat uses Steam lobby chat, so it works before the P2P match connection exists. In-match chat stays on the game channel. Keep the existing chat panel and its styling; only the backend changes.
8. **Build, map and hash lock.** A lobby is visible only to players whose game build and protocol match; Steam keeps those in step, and a screen that has not restarted after an update is told to restart. A map whose `EngineGeneration` is newer than the player's game, or whose installed hash differs from the host's, is shown as incompatible with a clear reason instead of being joined and then failed. An older, still-supported generation is compatible and is upgraded on a private copy (task 09A). The host cannot start with an incompatible player.
9. **Host is the lobby owner.** The Steam lobby owner is the host. There is no separate "host id" or registry ownership record. If the owner leaves, the match ends and the lobby closes; do not build host migration.
10. **No registry.** The browser must not call a Realm server to list, create, join, heartbeat or close a lobby.

## UX

- The lobby browser keeps its familiar layout: a list of lobbies with map, mode and player count, refresh, and a Join action.
- Remove IP, port, region, ping-to-registry and reachability columns. Steam provides the directory and the routing.
- The create panel keeps the map picker, player count and visibility choice. "Public" and "Friends only" replace the old listed/direct distinction.
- One optional "Join code" field sits under the visibility choice, with a plain explanation that it is for community events and that leaving it empty means anyone who can see the lobby can join.
- The lobby room keeps its seat grid, team colours, ready state, settings and chat. Remove the connection address, the copy-address button and NAT warnings.
- A lobby that requires a code shows a lock marker in the browser, and the room shows one "Enter join code" field for the players who joined without it. It is not a password modal before entry.
- Every seated player sees "Invite friends"; the host's extra controls are the ones that change visibility, settings, seats and the start.
- Errors are player-readable: "This lobby is full", "This lobby is friends-only", "This lobby was made with a different game version", "Steam is offline".

## Steam limits to design within

- Lobby metadata is limited per value (8,192 bytes) and per key (255 characters), with no documented total. Keep the browser fields short and put detailed match settings in the lobby protocol.
- The lobby member limit is 250. Realm maps are much smaller; use the map's playable slots.
- A lobby list request returns a bounded number of results. Filters, not pagination, are the way to narrow it.
- Steam lobbies are scoped to the AppID. Only one AppID is live at a time: the demo until the official AppID releases, then the official AppID alone. The official AppID already exists as the demo's base game, so this is a release date rather than a missing identifier. Do not build a registry to bridge AppIDs, and do not keep demo-era lobbies alive after the switch.

## Out of scope

- Seating, ready state, team assignment and match start (task 04).
- Deleting the old registry and admin lobby service (task 05).
- Rich presence and the full invite polish (task 14).

## Acceptance criteria

- A host creates a public lobby and another account sees it in the browser without a Realm server.
- A friends-only lobby does not appear publicly, but a friend can join from an invite.
- A player who is a friend of a seated non-host member can be invited by that member and join.
- A lobby with a join code appears in the browser, admits a member to the room, and refuses a seat until the code is entered; the code is absent from Steam lobby metadata.
- A code-gated lobby admits up to its seat count plus the small headroom, and closes to new members once the seats are taken, so a stranger without the code can never occupy the last seat.
- Joining connects through Steam and reaches the lobby room.
- The browser and lobby room show no IP, port, region or registry status.
- A build or map mismatch is refused before the match starts with a clear message.
- Lobby chat works before the host starts the match.
- The lobby closes when the host leaves.
- The game no longer calls the Realm lobby registry for any lobby operation.

## Pitfalls

- Do not keep the old registry as a hidden fallback when Steam returns no lobbies. An empty list means no lobbies.
- Do not store the match's seat state in Steam metadata and then keep a second copy in the registry; Steam metadata is the advertisement, the lobby protocol is the state.
- Do not expose the host's IP in the lobby metadata. The host is a SteamID.
- Do not build a lobby password service. Use Steam visibility and invites for privacy, and one optional join code for a community event gate. Do not put the code in Steam lobby metadata, do not turn it into a credential or a session, and do not make it a prerequisite for standing in the room.
- Do not restrict invites to the host. Steam lets any member invite, and a friends-only lobby admits invitees who are not the host's friends; suppressing that only makes players relay Steam IDs by hand.
- Do not size a code-gated lobby to its seat count. The room admits code-less members by design, so the room needs headroom and the seats need closing; a room at exactly the seat count turns the code into a denial of entry for the people it was written for.
- Do not assume lobby ownership moves usefully when the host leaves; the authority is on the host's machine, so the match ends.
