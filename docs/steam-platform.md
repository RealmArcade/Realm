# Steam platform

Realm is a free Steam game. Steam owns identity, the lobby directory, the peer-to-peer transport, the Workshop and the
stats; this repository owns the simulation, the editor and the map format. There is no service this project operates,
no database, no deployment and no address a player or a client connects to.

## The two applications

| Application | AppID | Purpose |
| --- | --- | --- |
| Full game | `5372260` | The product players own. Everything ships here. |
| Demo | `5537420` | The install that exists before the full game is released. |

The same build is uploaded to both, and no feature may depend on which one is running
(`SteamApplicationIds`). A cosmetic product for the supporter flair is a third AppID, read as an owned subscription and
used for appearance only (`SteamApplicationIds.SupporterFlairDlcAppId`, `0` until the product exists).

## Partner-site configuration

Performed once on the Steamworks partner site, not in this repository:

- **Workshop enabled** for the AppID, with the tags the game writes: `map`, `asset-pack`, and the key/value tags
  `mapName` and `build` (`SteamWorkshopService`).
- **App Publish Permissions**: during the demo era the official AppID's Workshop configuration lists the demo AppID, so a
  demo client can create the dormant official item of the same map.
- **Cloud quota** enabled for both AppIDs, which is what stores a Workshop item's preview image.
- **Steam DRM disabled**. The build starts without the Steam client and offers single player, the editor and local maps.
- **Lobbies enabled** for the live AppID; the release targets the official AppID only.
- **Stats, achievements, cloud and rich presence** configured, since the game writes results and presence to them.
- **Workshop Legal Agreement** accepted by the publishing account.

## What a release build ships

- The Steam native library for the platform it targets, beside the client and beside the authority
  (`Realm.ListenServerHost`), because a hosted match runs the authority in its own process.
- `steam_appid.txt` **only in local development output**. Exports, packages and manifests never carry it.
- No request to a release feed, an account host, a lobby registry, a CDN or a relay. Those services do not exist.

## Lobby limits and shape

- Steam holds up to 250 members (`SteamLobbyDirectory.MaximumSteamLobbyMembers`), which is a platform ceiling.
- A lobby sized to its seats; a code-gated lobby is given two extra seats of headroom
  (`SteamLobbyDirectory.CodeGatedHeadroom`) so people standing in the room without the code cannot shut out the ones who
  have it.
- Lobby metadata is the advertisement only: map name, map version, map hash, engine generation, game build, mode, seats,
  the host's SteamID, the trust mark and whether a code is required. Seats, teams, settings and chat live in the lobby
  protocol, which the authority owns.
- Visibility is Steam's: public, friends-only, private or invisible.

## No-Steam state

A machine with no Steam client still starts. Single player, the editor, local map play and `.rmap` import work; hosting,
joining, invites, the Workshop and stats are hidden, each with the reason Steam is unavailable
(`SteamService.UnavailableExplanation`). There is no DRM wrapper and no mandatory Steam check.

## The two-account test a release is checked against

Steam networking cannot be exercised by one account, so a release candidate is tested on two machines signed into two
accounts:

1. Host a lobby on account A, join it from account B on another network. Confirm seats, chat, the start countdown and a
   complete match.
2. Publish a map from account A, subscribe to it from account B, and play it from a lobby that names the item.
3. Invite from A's friends list and from the in-game dialog, and accept on B.
4. Stop the Steam client on B and confirm single player, the editor and local map play still work while multiplayer,
   the Workshop, invites and stats are hidden with a reason.

Anything that can only be checked on the partner site or with a second network is reported as not verified rather than
assumed.

## Versioning and rollback

- Run preview builds on a Steam beta branch (opt-in, optionally password-protected). Workshop publishing is disabled in a
  preview build, so no map can be authored against a surface that will change before release.
- Keep the previous release available so it can be set live again from the Builds page if a release breaks a wide part of
  the catalogue. Rollback is safe only while local state written by the newer build is still readable; say in the release
  notes what a rollback discards.
- A wide break is answered by rollback plus a corrected build; a narrow break is answered by the affected authors
  rebuilding their maps. The engine generation and the append-only map API guarantee are what keep those two remedies
  distinct.
