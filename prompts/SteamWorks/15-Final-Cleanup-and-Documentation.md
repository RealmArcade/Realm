# 15 — Delete the Remaining Platform and Rewrite the Architecture Docs

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 01 to 14, plus 16, 17 and 18. Run it after them, because this task sweeps the documentation and the residue of everything they add.
**Unblocks:** 19 and shipping the Steam-first build.

## User story

As the developer, I want the old admin server, its configuration, its database, its deployment and every stale document gone, and I want the repository to describe the Steam-first architecture accurately, so no future contributor re-adds a lobby registry, a CDN or an account service because the docs still mention one.

## Context

By this point the game uses Steam for identity, networking, lobbies, map distribution, invites and stats, and single-player, the editor and local maps are all that work without it. The old platform may still exist as a project, configuration, dead endpoint or document. This task removes the residue and makes the repository tell the truth.

This is a cleanup task. Do not add features, do not start a new service, and do not keep a "just in case" copy of the removed platform.

## In scope

### Projects and build

- Do not delete `Realm.AdminServer` or `Realm.Tools.Admin` here: task 22 owns that deletion and has already merged by the time this task runs. Verify they are gone from the solution, the build scripts, the publish profiles and every deployment configuration, and record anything task 22 missed rather than half-removing a project in the sweep.
- Remove project references to it and any test project that exists only to test it.
- Remove its app settings, database files, storage roots, TLS/DNS settings, container or service definitions and secrets.
- Remove the shared `ServersConfig`, `servers.json`, admin public keys and distribution/account configuration that no longer has a consumer.
- Remove packages that were only used by the deleted server, storage, cluster or database code.
- Ensure `Realm.Client` has no compile-time reference to the authority beyond the existing build-ordering reference, and no reference to the deleted platform namespaces.
- Ensure the client and the authority both ship the Steam native library, and that development builds have the development AppID file only in local output.
- Do not ship `steam_appid.txt` in an export or release.
- Confirm the Steamworks app has no Steam DRM wrapper enabled. The build must launch without the Steam client for offline single-player, the editor and local maps.

### Code residue

- Remove empty namespaces, folders and projects left by the deleted reachability, relay, distribution, account and admin code.
- Remove dead localization keys and dead UI scenes/resources for the removed screens.
- Remove dead command-line flags, environment variables and settings for the old platform.
- Remove dead diagnostics, admin pages and test harnesses that point at removed services.
- Keep the loopback authority, the Steam transport, the map package format, the editor, the simulation and the map API.
- If any public map API changed during the migration, rebuild `Realm.MapAPI` and refresh the committed API documentation and map-template libraries. If no public API changed, leave them alone.

### Documentation

- Rewrite the README's architecture and hosting sections: Realm is a Steam game; Steam owns identity, networking, lobbies, Workshop, invites and stats; Realm owns the simulation, editor and map format.
- Do not edit `AGENTS.md` here. Task 19 owns that update after this cleanup, so the two tasks do not produce competing edits to the same file.
- Replace the hosting/NAT/relay/seeder docs with a short "Steam platform" document covering the development AppID, the Steamworks partner-site configuration, Workshop tags, lobby limits and the two-account test.
- Document the no-Steam state where hosting is documented: single-player, the editor, local maps and `.rmap` import work, multiplayer does not, and Steam connects peers without a port, a relay or a router setting from the player. Do not write a replacement guide for the deleted direct-IP path.
- Remove or archive outside the shipped project the old network rig, relay test plan, CDN/seeder runbooks and IPv6 lobby decision document.
- Update the map author documentation: publishing is Workshop, the map's identity is the Workshop item, reusing another map's work is vendored when the map is authored (copying, or composition through `references.json`, with credits), so a map declares no load-time dependency and nothing resolves at install or join, a map keeps playing on a supported generation after an engine update (task 09A), and the in-game portal no longer exists.

### Release checklist

Document, and verify where possible:

- The Steamworks app has Workshop enabled, with the tags the game uses.
- During the demo era, the official AppID's Workshop Configuration lists the demo AppID under "App Publish Permissions", and both AppIDs have cloud quota enabled for preview images.
- Steam DRM is disabled, so offline single-player, the editor and local maps work without a Steam client.
- Lobbies are enabled for the live AppID, and the release targets the official AppID only.
- Stats, achievements, cloud and rich presence are configured.
- The Workshop Legal Agreement is accepted by the publishing account.
- The build ships the correct Steam native library for each platform.
- A two-account host/join test passes on two networks.
- A publish/subscribe/download/play test passes with a second account.
- Invites work from the friends list and from the in-game dialog.

## Verification

- Build the whole solution and fix every reference to a deleted project or namespace.
- Search the repository for the removed terms: `AdminServer`, `LobbyRegistry`, `Reachability`, `Relay`, `Seeder`, `AccountStore`, `AccountId`, `ServersConfig`, `servers.json`, `steam_appid.txt` in release output, and any old hosting doc. Do not delete `ContentAddressableStorage` or the host-transfer path; they serve local maps.
- The website in `D:\git\Realm\Website` was checked and does not call any `Realm.AdminServer` endpoint, so removing the server cannot break it. Do not add anything to the game that depends on that site either; it is not part of this repository and not a runtime dependency.
- Launch the client through Steam and exercise: menu, single-player, editor open/save, host a lobby, join from a second account, invite from the friends list, subscribe to a Workshop map, play it, and return to the menu.
- Launch the client without Steam, continue without it, and confirm single-player, the editor and local map play work while multiplayer, Workshop, invites and stats are hidden with a reason and the no-Steam marker is visible.
- Launch the build with the Steam client stopped and confirm there is no DRM prompt or mandatory Steam launch; single-player, the editor and `.rmap` import still work.
- Build the solution. If the user asks for a verification sweep, run the remaining test suite with the documented Godot path; do not add new tests for this cleanup.
- Report anything that could not be verified because it needs the Steamworks partner site, a second account or a second network.

## Acceptance criteria

- `Realm.AdminServer` and `Realm.Tools.Admin` and their configuration are gone from the solution and the build, verified rather than removed here (task 22 owns the deletion).
- No project references a deleted platform namespace.
- The README and docs describe the Steam-first architecture. No doc tells a player to run a relay, seed a map, forward a port or configure a router.
- The release build ships Steam native libraries and no development AppID file.
- Single-player, the editor, hosting, joining, invites, Workshop download and Workshop publishing all work in a manual smoke test.
- The build has no Steam DRM and starts without the Steam client.
- The client can start and play single-player and the editor with no Steam client, and it offers no multiplayer path in that state.
- The repository contains no dead platform service, database, deployment or admin portal.
- The solution builds; if tests were run, the remaining tests pass or their failures are reported as pre-existing or environment-related.

## Pitfalls

- Do not delete the local map package format, manifest, reference resolver or editor publish pipeline. Steam replaces distribution, not the map format.
- Do not delete the loopback authority path. It is the host's own seat and single-player.
- Do not reintroduce a direct-IP transport or its document because a player asks for one. A request for a port is a request for a path Steam cannot identify, and the answer is a Steam lobby.
- Do not remove the no-Steam startup choice. It is the only way a machine without Steam reaches single-player and the editor, and it is not a second multiplayer mode.
- Do not delete `ContentAddressableStorage` or host transfer; they are the local map path.
- Do not edit `AGENTS.md` in this task. Task 19 corrects the statements the migration invalidated and adds the platform guards, after this cleanup has removed the code they describe.
- Do not leave a "legacy" folder, branch or feature flag for the old platform. Git history is the archive.
- Do not claim the two-account or real-network tests passed if they could not be run. State the blocker.
