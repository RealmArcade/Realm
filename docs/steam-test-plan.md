# Steam test plan

What a Steam-first build is checked against before it is called working. Nothing in this document was run while the
migration was coded: the branch was verified by compilation and static reading only, so every item below is unverified
until someone plays it.

Two rules for the whole plan:

- **Record what happened, not what should have happened.** A step that could not be run (no second account, no second
  network, no partner-site access) is reported as not run, with the reason. A step that passed because nothing visibly
  broke is not a pass unless the thing it tested actually appeared.
- **Read the logs before believing the screen.** A hidden multiplayer surface, a refused join or a silent export all look
  identical to "nothing happened" from the outside.

## Where the evidence lives

| What | Where |
| --- | --- |
| Client session logs, crash files, exported zips | `user://logs` (`DiagnosticsService.LogDirectory`) |
| Authority log for this screen's own matches | beside those logs, named `local_authority.listen_server.<pid>.log` (`LocalAuthority.LogFileName`) |
| Authority PID and exported JIT ELF (debug playtest only) | printed by the authority at match start |
| Diagnostics export | the crash offer on the main menu after an unexpected exit; writes a zip in the log folder and reveals it |
| Crash offer | one line on the main menu after an unexpected exit, once per crash (`DiagnosticsService.UnacknowledgedCrashReport`) |

## Setup

1. `dotnet build Realm.slnx`, then import and run `Realm.Client` in Godot 4.7 mono (the checkout's Godot path is in
   `AGENTS.md`).
2. **Development AppID file.** A run that Steam did not start needs `steam_appid.txt` containing the demo AppID. From the
   editor it belongs in `Realm.Client`: the screen reads it and states the id in `SteamAppId`/`SteamGameId` before
   `SteamAPI.InitEx`, because Godot bootstraps the game from its own folder and the file Steamworks looks for beside the
   launching executable is never the one in this repository. An export run reads the copy beside the executable, as
   Steamworks always has. It is gitignored and must never appear in an export; step G checks that.
   The authority is a process of its own and needs no file of its own: the screen passes it the AppID Steam gave the
   screen, and the authority states that to Steamworks before it initialises (the authority log's first two lines are
   where to check it, `running as application <id>`).
3. **Two accounts.** Steam networking cannot be exercised with one account, so a real multiplayer test needs two Steam
   clients signed into two accounts, ideally on two networks. Steam runs one client per signed-in user session, so the
   cheapest second identity on one machine is a second Windows user session signed into the second account; a laptop on a
   phone hotspot is the honest test of the transport and is worth having anyway. Both screens must own the AppID the file
   names, or `SteamAPI_Init` refuses for a licensing reason rather than a missing id. Everything in group A can be done
   alone first.
4. **A map to publish.** Any map folder that compiles; the editor's `btnPublish` is the publish path.

## A. Local only (one account, no network tests)

| Step | Expected |
| --- | --- |
| Start the client with Steam running | The menu names the player's Steam persona, and the profile dialog shows Steam's avatar and profile link |
| Start a single-player match, play to a result, return to the menu | The match runs; `GameOver` writes a one-line match summary that a later diagnostics export carries |
| Open a map in the editor, save, playtest with `btnTestMap` | The workspace compiles, the playtest attaches to the authority, and the authority prints its PID and workspace |
| Map library: open it with no filter set | Every map is listed with a trust mark; nothing is hidden before the player asks for a filter. On a catalog with no votes yet every item reads untrusted ("too new"), which is the threshold's own behaviour and not a defect: the Verified collection that would vouch for a new map is not read by any build yet |
| Crash offer → `EXPORT` | One zip in the log folder, revealed in the file manager, containing the session logs and an environment summary; nothing is transmitted, and the offer is recorded as seen so it does not return |
| Create ten-plus sessions, then check the log folder size | The folder stays bounded by the documented caps (`DiagnosticsService` retains 10 sessions and 32 MB) |

## B. Hosting and joining (two accounts)

1. Account A: host a lobby on a Workshop map. Watch the host progress card — it should show the map loading and the host
   taking its seat, with no port, relay or registry step in between.
2. Account B: find A's lobby in the browser. It is listed with the map name, seat count, trust mark and whether a code is
   required.
3. B joins. Expect seats, chat and the start countdown over the lobby protocol, then a complete match.
4. **Record:** the lobby metadata A published (map name, version, hash, generation, build, seats, host SteamID, trust
   mark, code flag), the time from B's click to B's first drawn frame, and whether the match ran to a result without a
   repair request storm in either log.

The high-value question here is not "did it work on my LAN" but "did Steam choose a path without a port prompt". A join
that only succeeds when both machines are on the same network is a finding, not a pass.

## C. Join code

1. Host a lobby with a code. B, standing in the room without the code, cannot take a seat, and the room still has room
   for B plus the players who have the code (the lobby is created with code-gated headroom).
2. B enters the wrong code, then the right one. The refusal is a translated line, not a silent nothing.
3. Confirm the code appears in no Steam lobby metadata: it is compared on the authority only.

## D. Invitations and rich presence

1. A invites B from the Steam friends list. B accepts and lands in A's lobby.
2. B invites A back from inside the lobby. Any seated player may invite, not just the host.
3. Start the game from a friend's Join Game action while it is closed. The `+connect_lobby` argument should land the
   player in that lobby without a second click (`SteamRuntime.ReadConnectLobbyArgument`).
4. Check rich presence while in the menu, in a lobby and in a match: it should name the state to a friend, and clear on
   exit.

## E. Workshop publish and subscribe

1. Account A opens a map that nothing has played and asks to publish. It is refused with "0 of 25 testers have played
   this" and no item is created on the Workshop, so no name is claimed either. Play it with enough testers, then publish:
   the licence is shown once and recorded in `user://ugc_license_acceptance.json`, and the next publish must not ask
   again (bumping `UgcLicenseVersion` must ask again). The item is created by that first publish, with the visibility the
   author picked and a preview image converted from the map's thumbnail; a later publish updates it in place.
2. Inspect the uploaded folder in the Steam client: it should be the canonical copy only — no `bin/`, `obj/`, `.vscode/`,
   history or keys.
3. Account B subscribes, downloads, and plays the map from a lobby that names the item.
4. A republishes with a change; B receives the update and a lobby naming the item runs the new version.
5. **The private-session-copy rule:** after playing, exactly one session copy of that map should remain, keyed by the
   package hash of the installed copy, and it must not be a hard link to the Workshop folder. Republishing the map makes
   a new hash, so the next match rebuilds the copy; the old one is swept rather than reused.
6. B unsubscribes and deletes the item's local files; a lobby that names it must refuse with a readable reason rather
   than loading a stale copy.

### E-2. Two creators, one map name

Steam reserves no map name, and the publisher now looks for the item to update among its own account's items only. This
is the group that proves it, and it needs both accounts from the setup:

1. **A foreign item is not taken over.** B publishes a map named `Arena`. A then publishes a map also named `Arena` from
the editor. A's publish must succeed as a new item with its own id, and A's console must name B's item id as belonging to
somebody else. Before this was fixed, A's publish was aimed at B's item and failed every time.
2. **A's own republish stays in place.** A changes the map and republishes: the item id shown in the confirmation is the
same one, and the Workshop page shows the new version rather than a second item.
3. **A deleted item is republished, not resurrected.** B deletes their item on the partner site, then B publishes the map
again. The confirmation must name a **new** item id, and a lobby that names the new item must be joinable. The old copy of
the deleted item must not be played.
4. **A privatized item** (set to private on the Workshop page, out of the editor's reach) then republished from the
editor: record what happens - a second public item, or the private one updated. This is unverified behaviour of Steam's
own-item query rather than a decision made here, and the answer decides whether the editor needs a visibility control.
5. **A banned item** republished: record whether a fresh item appears and whether the creator is told anything.
6. **The lobby advertises the host's own item.** With both `Arena` items existing and B's the more recently updated one,
A hosts their map and B reads the lobby's advertisement: the item id must be A's, not the newest `Arena`. Then repeat
step 2 of the main group (join over the advertised item) and confirm the join downloads A's package and passes the hash
check. Before this was fixed, B downloaded the other `Arena` and was refused with no reason on either side.

## F. The workspace archive tool

`tools/WorkshopArchive` is a developer tool and is run by hand with a partner publisher key in the environment. Check it
on one item: the archive folder holds the item's files plus a reviewable JSON record naming the source item, creator
SteamID, license clause, reviewer and date, and nothing is uploaded anywhere. This is the only place a publisher key is
used, and it must never be read from a committed file or a build.

## G. No Steam client at all

1. Stop the Steam client entirely, then start the build.
2. `SteamStartupGate` offers to continue without Steam. Accept.
3. Single player, the editor, local map play and `.rmap` import all work. Hosting, joining, invites, the Workshop, stats
   and leaderboards are absent, each explaining the reason (`SteamService.UnavailableExplanation`), and no surface is
   left clickable and broken.
4. No DRM prompt, no mandatory Steam check, no relaunch-to-update dialog.
5. Inspect an exported build: it ships `steam_api64.dll` beside the client **and** beside the authority, and no
   `steam_appid.txt`.

Steam offline mode is a different state and is worth one run of its own: Steam initialises, local features work, and
multiplayer's behaviour should be the honest failure rather than a hidden button.

## H. Map compatibility across a build

1. Publish a map from this build, then raise `RealmVersion.GameBuildNumber` in a local build only.
2. The published map must still be playable: the generation is what gates a map, not the build number.
3. Raise `EngineGeneration` past the map's stated generation and confirm the map is refused with an update action, in the
   map details rather than in a warning dialog.

## I. Failure surfaces

1. Publish a map whose script traps on instantiation, and join a lobby running it. The client should say the map's
   script failed and name the map, with the log path in an advanced field — not present as a game crash.
2. Kill the authority process during a match. The client should report the disconnect reason rather than freeze.
3. Close the client with an unhandled exception forced (a test build is fine). The next launch offers the diagnostics
   export once; dismissing it must not make it return on the following launch.

## J. Two-network reality check

Repeat B and D with the two accounts on different networks (one on a phone hotspot is enough). This is where a transport
assumption shows up: the host's authority must be reachable over Steam's own path, with no forwarded port and no relay
of ours.

## K. One non-English language, played through

The catalogs are verified mechanically — `verify` proves every key is present with matching placeholders, `prune` proves
no key outlived its call site — but nothing in this branch has been read by a native speaker or drawn on a screen in
another language. This group is the cheapest way to find the class of defect that a compiler and a key audit both miss:
**a string built at draw time that carries no key, and a comparison made against text a translator was allowed to
change.**

1. Settings → Video → Language, pick German (or Japanese, which is better at exposing truncation). Apply, then restart
   the client: the language must persist (`GameSettings.Language` is saved on Apply). Applying settings closes the
   dialog, so every check below is done after the switch rather than during it. Arabic is worth a run of its own for a
   different reason: `LocalizationManager` flips `LayoutDirection` to RTL for it, so the question there is whether the
   screens still lay out at all rather than whether the words are right.
2. Menu → map library → a map's details → storage. Nothing may read in English: creator line, stat rows, version rows,
   the tech info block, the download button while it is downloading (`Wird heruntergeladen... 42%`), and the
   `1 Version` / `3 Versionen` count.
3. Host a lobby alone (no second account): seats heading, settings rows, map card, start button, chat placeholder.
4. Start a single-player match. Hover a command card, an upgrade card and an inventory item: `Kaufen: …`,
   `Verbessern: …`, `Stufe 2`, `Bis zu 3 Mal.`, `Bei 2 Exemplaren.`, `Schwert (Bestand: 2)`. Press the grid overlay
   toggle and read the feedback line (`Overlay-Modus: RASTER`); right-click a unit on the minimap and read
   `Kamera → …`.
5. Open a map in the editor and walk the surfaces this pass touched:
   - **Object Manager**: choose `Einheiten` in the filter. The tree must list units and each heading must read
     `Einheiten (12)`. An empty tree while `Alle Typen` shows everything is the exact defect that was just fixed, so it
     is the single most important check in this group.
   - **Animation preview**: the per-action count badge (`3 Anim.`).
   - **Asset browser**: open the folder filter. Every downloaded map reads `📦 [Karte] <name> (<version> - Neueste)`,
     and the newest version of each is marked `Neueste` once rather than on every row.
   - **Feedback lines**: draw a coordinate (`Koordinate „x“ erstellt.`), run a utility
     (`„<name>“ wurde abgeschlossen`), trigger a failed import or delete (`Fehler beim Importieren des Assets: …`).
   - **Template manager**: delete-guard notes read translated (`Platzierte Instanzen im Kartengelände gefunden …`),
     while a template's `Damage: 5 | Range: 2 | Type: pierce` description stays English on purpose — it is written into
     `metadata.json` and read back as map content, so a translated one would put the editor's language into every
     player's copy. Confirm it is still English, and confirm an author's own description in that field survives a save
     unchanged.
6. **What this group cannot test yet, and why.** The language changes only on Apply, and Settings is a screen of its own
   reached from the main menu, so no editor or match surface is on screen when `LocalizationManager.UpdateLocale`
   fires. Those labels are therefore checked as "correct when built", not as "healed in place": a control that was
   assigned once instead of registered still reads right, because the dialog was rebuilt afterwards. The one place the
   live path is observable is the Settings screen itself — change the language with that screen up and press Apply.
   There, a label drawing a key is asked for again (`RetranslateCurrentScene` sweeps the live tree, so even a plain
   `TranslationServer.Translate` assignment heals), a built string registered with `LocalizedText` is redrawn, and a
   built string assigned directly keeps the language it was built in. If a later pass lets Settings open over a match
   or the editor, that is where their registration gets its real test; until then, `LocalizedText` registration is a
   claim this plan can only partly check.

**A failure in this group looks like one of four things**, and each points at a different cause: an English word sitting
beside translated furniture (a literal with no key, so the generator never saw it); a label that keeps the previous
language after a switch (a built string assigned once instead of registered); an empty list or an unmatched filter where
English shows entries (a comparison against translated text); or a literal `{0}` on screen (a translation whose
placeholders drifted, which `verify` should already have caught — if it appears, `verify`'s check is the thing to fix).

**Record** the language, the screen, the on-screen text and a screenshot. Wording that is merely clumsy is a note for
the translation kit, not a defect: the first pass in each language is a review, and this group is how that review starts.

## Pivot candidates to settle during the test

These are the decisions most likely to be wrong in a way that only playing reveals. Each is a single change, not a
redesign.

- **The donate button is gone.** The main menu's footer link was deleted as a donation surface, even though it funded the
  project rather than a creator. If the project wants that link back, the rule in `AGENTS.md` has to say why a developer
  donation link is not the thing the Workshop rule bans.
- **Authorship has three tiers, and none of them gates anything.** A map is signed over its whole package, this machine
  remembers the key it first saw for a map, and a Workshop item's metadata states the key its owner publishes. Publishing,
  installing, joining and hosting still work with no key at all. The pivot to watch is whether players understand the
  distinction between "signed" and "Steam-confirmed", and whether a changed-key question reads as a warning about a copy
  rather than an accusation against the author; both are word and layout changes. Making any tier a requirement would be
  a policy change and belongs in its own task.
- **A publish identity is the map's name.** A renamed map creates a second Workshop item rather than moving the first,
  and the lookup is one query per publish. Publishing now only ever updates an item the publishing account owns, so a
  shared name no longer blocks a creator. What is still name-resolved is the player's side: the storage list's "newer
  version" offer and the details screen's download button pick the newest item stating that name, which two same-named
  maps make ambiguous. The join path is not affected because a lobby advertises the item id. Closing that needs the
  installed package to record the item it came from, which is a map-format change rather than a repair.
- **The trust threshold starts low.** `MapTrustPolicy`/`MapCatalogFilter` hide nothing by default; the confirmation sits
  on entering an untrusted lobby. If a first-time visitor's library looks noisy, raise the threshold rather than
  filtering by default.
- **Nothing is vouched for yet.** Trust is votes and item age, plus an optional `config/featured_workshop_items.json`
  that no build ships. Reading the children of a developer-owned Verified Workshop collection with `GetQueryUGCChildren`
  is the intended way to vouch for a map too new to have votes, and it is not written: there is no collection id to point
  it at. So every item is "too new" until it collects votes, and a first run looks uniformly untrusted. Before the
  official catalog opens, either ship the list or write the collection query together with the place its id is stated.
- **Asset packs are Workshop items.** The editor's art pack is published as an ordinary item marked `IsAssetPack` and
  filtered out of browsing. That is the one place a non-map item exists, and it is worth confirming it never appears in
  the map library or a lobby.
- **The archive tool is manual.** If mirroring becomes routine, the operator steps (key from the environment, record per
  mirror) are the part to automate, not the publishing decision.

## Reporting

For each group: pass, fail, or not run — with the log excerpt or the on-screen text that decided it. Anything that
needed the partner site, a second account or a second network and was not available goes in the "not run" list rather
than being inferred from code.
