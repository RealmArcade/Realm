# 11 — Delete Custom Accounts, Sessions and Creator Registration

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 04, 07.
**Unblocks:** 12, 13, 15.

## User story

As a player, I want my Steam identity to be my Realm identity, with no sign-up form, no password, no session expiry and no "verify your account" step. My Steam persona and avatar are enough.

As the developer, I want the account service, its database, its bearer tokens and its creator registration gone, so there is no second identity system to secure or operate.

## Context

The account service exists because the game was originally built without Steam. It stores accounts, unique display names, bearer sessions, rename history, creator registrations and match stats. The authority verifies Steam auth tickets and then issues its own session tokens.

Steam already gives every player a stable, authenticated SteamID64 and a persona name. The transport (task 02) authenticates the peer, and the Steam lobby (tasks 03 and 04) establishes membership. The custom account layer is now redundant.

## Requirements

1. **SteamID64 is the identity.** Replace the custom account ID in the roster, player rows, lobby protocol and match results with the player's SteamID64. Keep one small player-identity value carrying SteamID64, persona name and avatar so the rest of the game does not pass three loose fields around.
2. **Persona name is the display name.** Use the Steam persona name. Do not enforce uniqueness, do not store a rename history and do not add a name-change cooldown. Two players with the same persona name are allowed and are distinguished by their SteamID and avatar.
3. **No custom session tokens.** Remove bearer sessions, session hashing, rotation, expiry and revocation. A peer's identity comes from the authenticated Steam connection and its Steam lobby membership. Do not replace the session token with a custom signed token; there is nothing left to protect.
4. **Remove the ticket exchange if it is only for the account service.** With Steam P2P, Steam has already authenticated the connection. If the existing ticket verification exists only to mint an account session, delete it with the account service. Keep a ticket check only if a concrete admission rule still needs it, and document why.
5. **Remove creator registration.** A map's author is the owner of its Workshop item. Delete creator accounts, creator name policy, creator registration endpoints, creator events and creator badges that depend on the custom account. There is no Realm creator profile: the creator's Steam profile and Workshop page are their public presence, and the game only links to them (task 16). Keep those links.
6. **Admin is a SteamID allowlist.** Replace account roles and admin permissions with a small local configuration listing SteamID64s. The host is the administrator of its own lobby by being the lobby owner. Do not build an admin account system, an admin web login or a role database.
7. **Delete the account surface.** Remove the login, registration, password reset, account settings, sign-in prompts, "unverified name" marks, account badges and account-linked profile pages. The profile shows Steam identity only.
8. **Do not migrate old accounts.** Old account data is abandoned. Do not write a compatibility shim, a one-time importer or a "link your old account" flow. Document that Steam is the account.
9. **Keep the map-script Steam surface.** `IGameAPI`'s Steam achievements, stats and cloud files are keyed by the player's seat and SteamID. They are not part of the custom account service and must keep working.
10. **Keep the authority's peer-to-SteamID mapping.** The simulation already records a seat's SteamID for the map API. Use that as the canonical identity for the roster and the match results, and remove the parallel account ID.

## UX

- No login screen, no sign-up, no password reset, no "sign in" buttons.
- The profile shows the Steam avatar, persona name and SteamID (or a copyable ID in an advanced view).
- The lobby roster shows Steam personas and avatars; duplicate names are allowed.
- The map editor no longer asks for an account to publish.
- Admin-only actions are visible only to SteamIDs on the local allowlist.

## Out of scope

- Persistent stats and leaderboards (task 12).
- Reviews, ratings and moderation (task 13).
- Deleting the entire admin server (task 15).

## Acceptance criteria

- The game never asks for a username or password.
- No account HTTP request is made by the client, lobby, authority or editor.
- The roster and match results identify players by SteamID64.
- Duplicate persona names are displayed without conflict.
- Publishing no longer depends on an account.
- Admin access is controlled by a SteamID allowlist, not an account role.
- The account service, its database, its endpoints and its shared contracts are deleted.
- No creator profile database, website route, OpenID login or profile editor remains; creator links open Steam, and a map's optional Discord invite is map metadata rather than a profile.
- The solution builds with no account references.

## Pitfalls

- Do not keep the account service as a "profile cache" for names. Steam provides the persona.
- Do not replace creator registration with a Realm-hosted profile. The Steam profile and Workshop page are the only creator profile surfaces; the only in-game creator link beyond them is the map's optional Discord invite.
- Do not persist persona names as the identity. Names change; SteamID64 does not.
- Do not use the Steam persona name in a rich-text control without escaping it. It is user-controlled text.
- Do not leave session-token fields in the lobby protocol or roster "for later". Delete them.
- Do not delete the map-script Steam bridge or the seat SteamID mapping; those are game features, not the account service.
