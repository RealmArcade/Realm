# 14 — Make Steam's Overlay, Invites and Rich Presence the Social Surface

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 03.
**Unblocks:** 15.

## User story

As a player, I want to invite friends from the lobby with the Steam invite dialog, see "Join Game" on a friend who is in a Realm lobby, and have my Steam status say what I am doing, without copying an address, sharing a code, or explaining which server I am on.

## Context

The client already has partial Steam invite support: it can create a private Steam lobby as an invite envelope, handle an accepted invite, and open the friends dialog. But the real lobby is now a Steam lobby (task 03), and the custom invite-code and Steam-lobby address-sharing paths are gone. The manual direct-IP join is deleted by task 06 and no invite flow replaces it.

This task turns Steam's social surface into the primary invite path and removes the last custom invite plumbing.

## Requirements

1. **Rich presence.** While in the menu, set a status such as "In the menus". In a lobby, set "In Lobby (3/8)" with the map name. In a match, set "In Match" with the map and mode. Clear the presence when the game exits or the player returns to the menu without a lobby.
2. **Join Game.** Set Steam's connect string to the current Steam lobby so a friend can right-click the player and choose Join Game. Use the `+connect_lobby` launch argument so Steam can start the game directly into the lobby.
3. **Invite dialog.** The lobby room's Invite Friends action opens Steam's invite dialog for the current lobby. Accepting an invite enters the Steam lobby and then the match lobby. Remove custom Steam-lobby invite codes, invite links and "send this code to your friend" instructions. There is no copy-address action to keep: task 06 deletes it with the rest of the direct-IP path.
4. **Join requests while running.** Handle a Steam lobby join request when the player is already in the menu, in another lobby or in a match. If they are in another lobby or match, ask for confirmation before leaving, using the existing confirmation pattern. If the join fails, say why: full, closed, friends-only, wrong build, map missing.
5. **Friends list.** Keep the existing friends dialog, but make it open Steam's friends overlay rather than a custom friends list where possible. Show Steam avatars in the lobby roster.
6. **Lobby chat.** Pre-match chat uses Steam lobby chat. In-match chat stays on the game channel. Do not build a second chat backend for invites.
7. **Overlay availability.** Check whether the Steam overlay is enabled. If it is disabled, show a small hint that invites and the friends list are available through Steam, but do not block the game or fake the overlay.
8. **Localized presence.** Use Steam's rich presence localization tokens where the app config supports them. Do not hardcode English strings in the overlay if the game has a localization key for the same state.
9. **No custom social backend.** Remove Steam invite tokens, invite codes, Steam lobby address sharing, "join by lobby ID" text fields and any server-side invite registry. Steam is the invite transport, and no screen accepts an address in its place.
10. **Privacy.** Do not expose the host's IP, port or machine name in presence or lobby metadata. Presence states the lobby and the game state, nothing about the network.

## UX

- The lobby room has a clear "Invite Friends" button that opens the Steam overlay.
- A friend's Steam context menu shows "Join Game" while the player is in a lobby.
- The player's Steam status shows the lobby size and map, or the match mode.
- No screen asks the player to copy or paste an address, a code or a lobby ID, whether for a lobby or an invite.
- An invite that cannot be joined explains why in one sentence.

## Out of scope

- Lobby creation and browsing (task 03).
- Seating and match start (task 04).
- The final removal of the admin server and docs (task 15).

## Acceptance criteria

- Invite Friends opens the Steam invite dialog for the current lobby.
- A friend can join from the Steam friends list and lands in the lobby room.
- `+connect_lobby` starts the game into the correct lobby.
- Rich presence updates on menu, lobby and match, and clears on exit.
- Lobby chat works before the match starts.
- No custom Steam invite code, Steam lobby address-sharing, invite registry or address field remains.
- The overlay being disabled does not break the lobby.

## Pitfalls

- Do not create a Steam lobby just for the invite and a separate Realm lobby for the game. There is one Steam lobby now.
- Do not keep the old `realm_game_lobby_id` metadata envelope. The Steam lobby ID is the lobby ID.
- Do not set rich presence once at startup. Update it when the state changes.
- Do not show "Join Game" for a lobby that has already started or is full; the join must fail with a clear message.
- Do not put the host's network address in presence or metadata.
- Do not keep the copy-address action "in case" a player asks for it. An address to copy is the beginning of the path task 06 deletes.
