# 01 — Make Steam the Default Runtime and Identity

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** nothing. Run this first.
**Unblocks:** 02, 03, 07, 14.

## User story

As a player, I want to launch Realm from Steam and be myself in the game automatically, so I never create a username, type a password, or wonder which account I am signed into. If Steam is unavailable, I still want to play single-player, use the editor and load the maps I already have, with a clear statement of what is missing.

As the developer, I want Steam to be the default runtime and the only identity, with no second mode that pretends to be a player when Steam is absent.

## Context

The client already initialises Steamworks in `SteamRuntime`/`SteamService`, mints auth tickets and handles Steam invites. But the current design treats Steam as optional: account features are written to survive without it, and the authority process has no Steamworks at all.

That assumption changes here. Steam is the platform whenever the game runs: identity, lobbies, Workshop, invites and stats all assume it. The old account service remains temporarily until task 11, but no new code may depend on it. A machine without usable Steam gets local play and nothing else, which is not a second online product and must not grow a custom account, a local identity, a lobby directory or a direct connection.

## Requirements

1. **Try Steam first; offer local play only as a fallback.** On startup, initialise Steamworks and keep pumping callbacks for the life of the process. If it cannot initialise because the Steam client is not running or the account is unavailable, show a translated startup choice: retry, quit, or continue without Steam. The choice explains that without Steam the game can still run single-player, the editor, local maps and `.rmap` import, and that multiplayer, Steam lobbies, Workshop, invites, stats and creator Steam profile and Workshop links are unavailable. Do not silently continue into the menus and do not pretend the Steam features work.
2. **Offline and no-Steam are different states, and both are local-only.** Steam's offline mode still initialises and allows single-player, the editor, local map play and `.rmap` import into the local CAS. A machine with no usable Steam client can continue without Steam with the same local features, plus the same absence of Steam lobbies, Workshop, invites, stats, achievements and Steam profile links. Multiplayer exists only inside Steam: do not add a non-Steam storefront, a custom account, a custom online service or a way to reach another machine.
3. **One identity object, and it is Steam's.** Expose the current player's SteamID64, persona name, avatar and AppID through a single service the rest of the game reads. When Steam is unavailable the service reports that there is no player rather than inventing one: there is no guest name, no local display name and no placeholder identity, because a name nothing verified would be the first step back to a second identity model. The main menu and profile area show the Steam persona and avatar and nothing that reads like an account when there is none.
4. **The authority gets Steamworks.** Add Steamworks to `Realm.ListenServerHost`, initialise it as the host user, and pump callbacks on the authority's main loop. Log the AppID and SteamID on startup. A Steam-hosted lobby must not start if the authority has no Steam identity, because every peer and every seat is a Steam identity; a loopback single-player match still starts, because it needs no identity at all.
5. **Lazy, retried initialisation.** The authority is started once per screen and may start before Steam is ready. If Steam is not available at that moment, retry when a hosting request arrives rather than failing the process permanently. Do not call the "restart the app through Steam" API in the authority.
6. **AppID comes from the launching process, never from a constant.** The authority must initialise under the exact AppID the client was launched with: pass it from the client, or rely on the inherited `SteamAppId` when Steam started the client. Only one AppID is live at a time — the demo until the official AppID releases, then the official AppID alone. The official AppID already exists and is configured as the demo's base game, so this is a release-date distinction rather than a missing identifier. Do not support both simultaneously, and do not make gameplay depend on which one launched the process. **The mechanism already exists and is not to be reinvented:** `Realm.Client/Services/Steam/SteamApplicationIds.cs` holds `FullGameAppId` and `DemoAppId`, and `Realm.Client/steam_appid.demo.txt` / `steam_appid.retail.txt` are the two files that select one. Read what is there, make the rule "the process initialises under the AppID it was launched with", and keep those files out of exports and releases rather than inventing a third mechanism.
7. **Remove the optional-Steam branches from Steam multiplayer.** The Steam lobby, Workshop, invite and stats paths must stop treating "Steam unavailable" as a supported state. Keep exactly one translated explanation for why a Steam feature is unavailable, and hide or disable that feature instead of falling back to a custom service. There is no path that plays multiplayer without Steam, and nothing may offer one.
8. **Report why initialisation failed.** Use the detailed initialisation API (`SteamAPI_InitEx` or the flat equivalent) and log the returned error message. A bare boolean tells a player nothing and makes every support report undiagnosable. The same error text is what the blocking screen shows in a translated wrapper.
9. **Keep the ticket flow until task 11.** The authority may continue to verify the existing Steam auth ticket for admission during the migration. Do not add new account or session dependencies, and do not delete the ticket verification yet.
10. **Retire every way to reach another machine without Steam.** The "Join Private Lobby via IP Address" button, the host's "Allow direct IP" option, the port field, the guest-seat admission path and the client's address dialog are removed in task 02 and task 06, not kept as an advanced option. Do not delete the loopback socket between the host's screen and its authority in the same sweep: the host's own seat still needs it, and it stays bound to the loopback address so it is not a route anything on the network can reach.
11. **Make the no-Steam state visible.** A persistent, unobtrusive marker states that Steam features are off, with a way to retry Steam. Do not let a player expect a lobby list, a publish action, a vote, a creator's Steam profile or an achievement in this state; the multiplayer entry points are absent rather than present-and-failing.
12. **Delete the GitHub update path.** `Realm.Client/Services/GameUpdateService.cs` polls `api.github.com` for the latest release, and `MainMenu` (its caller at `IsNewerReleasePublishedAsync`) shows an "Outdated" label with a `DOWNLOAD...` action that opens the GitHub releases page. Both go in this task: there is no non-Steam update channel, Steam keeps players in step, and a download button in the main menu contradicts requirement 2 and the architecture document. Delete the service, its `MainMenu` call site, the label and the button, plus their localization keys and the two hard-coded repository slugs. Note that the same class also hosts `GetOtherInstalledVersions`, which is what fills the main menu's version dropdown; that dropdown is removed by task 09A, so delete the method with the service rather than leaving a helper with no consumer (they disagree today: `speige/Realm` in the poll and `RealmArcade/Realm` in the link). Record in the task report every remaining GitHub link and what it is for: the BUG REPORT button and the `GameOver` takedown/report link are support and legal surfaces, not a distribution path, and they stay — but they must not read as a way to get another build of the game.

## UX

- The first screen when Steam is missing is a clear choice, not a dead end: "Retry Steam", "Continue without Steam" and "Quit", with a short list of what the game cannot do without Steam — multiplayer first among them.
- The profile shows the Steam avatar and persona name, and shows nothing that reads like an account when Steam is absent.
- The lobby room and player rows use the Steam persona name, which is the only name a seat has.
- Single-player, the editor, local maps and `.rmap` import remain usable in Steam offline mode and with Steam unavailable.
- Nothing on any screen asks a player for an address or a port, and nothing in the main menu offers to download or install a different version of the game. A build difference is a Steam update.
- The no-Steam state shows a small persistent note and a "Retry Steam" action; multiplayer and Steam-only buttons are hidden, not left clickable and broken.

## What must not be built

- No custom account login, cloud guest account, custom session or account database. There is no local guest identity either.
- No second Steamworks wrapper, no per-scene initialisation, no duplicate callback pumps.
- No non-Steam storefront, no custom lobby directory, no custom Workshop substitute, no direct-IP join and no automatic fallback from Steam to anything.

## Acceptance criteria

- Launching the build outside Steam produces the startup choice and never silently reaches the main menu.
- Choosing "Continue without Steam" reaches the menu with a visible marker; multiplayer and Steam-only actions are hidden with a reason.
- Launching through Steam shows the persona name and avatar in the menu and profile.
- Hosting a loopback single-player match works with Steam in offline mode and with Steam unavailable, and a `.rmap` imports into the local CAS.
- No Steam path admits a peer or issues a session without a verified Steam identity, and no screen presents a Steam-less way to play multiplayer. The Join-by-IP action and the port field are still present at the end of this task, and that is expected: the transport stage (02) and the traversal stage (06) delete them together with the listener they need. The task report names them as pending removal rather than claiming they are already gone.
- The build has no Steam DRM wrapper and starts with no Steam client installed.
- The authority log states its AppID and SteamID, and states that it has no Steam identity when it does not.
- The client and authority each initialise Steamworks exactly once, and both compile.
- No Steam lobby, Workshop or invite screen offers a Steam-less path, and no service exposes a placeholder player.

## Notes and pitfalls

- `SteamService` currently documents that every account feature works without Steam. Update that contract; it is the central lie this task removes.
- Do not initialise Steamworks in a background thread. Callbacks must be pumped on the main thread.
- The authority is a separate process from the client. It needs its own native Steam library beside its executable, not only the client's copy.
- Do not remove the loopback socket between the host's screen and its authority. It is the host's own seat, not a multiplayer path; only the part that binds beyond loopback and admits a remote peer is retired (task 02).
- Two processes of the same user can both initialise Steamworks; do not add a check that forbids it. What is not supported is a Steam P2P connection from a process to its own SteamID, which is why the host's own client uses loopback.
- The most likely real failure is an AppID mismatch: the authority resolving a different AppID than the client because of a stale `steam_appid.txt`, a different working directory, or a hard-coded constant. Log both AppIDs and compare them.
- Do not leave `GameUpdateService` behind with its startup call removed. A poller nobody calls is the kind of residue that gets wired back in by the next person who reads its name in the file tree.
- Do not invent a guest identity for a machine without Steam. A name nothing verified is the beginning of a second identity model, and the game has no multiplayer to use it in.
- Do not make the no-Steam state the default or hide the fact that Steam is off. The player must be able to tell which state they are in.
- Never enable Steam DRM, a Steam launcher wrapper or Steam's "restart through Steam" API in the client. Any of them would prevent the startup choice and break offline single-player and the editor for a machine without Steam.
- Authorship is the Workshop item owner (task 09), never a local name. The map's own key signature is kept as an integrity and continuity record for `.rmap` copies (task 09B); it is not an identity and it gates nothing.
- To test two peers on one machine, run a second Steam instance with `-master_ipc_name_override <name> -userchooser` and launch the game with `steam_master_ipc_name_override=<name>`, and **log that second Steam client into a different account**. The flags give you a second Steam client, not a second identity: two instances signed in as the same user share one SteamID, and a self-addressed connection is not refused — GameNetworkingSockets relays it over the network to yourself — so a same-account pair is the one configuration that produces no meaningful peer. The second account is what makes the test a test.
