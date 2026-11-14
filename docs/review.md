# Review notes

Everything in this file is either **not fully proven** by an automated check, **a judgement call I made** that a
person may want to overturn, or **left incomplete** and needing a follow-up prompt. Anything not listed here was
verified by a build plus the test suites named in the change's commit message.

Repositories touched in this batch:

| Repo | Branch | What landed |
| --- | --- | --- |
| `Realm` (this repo) | `network_host` | lobby reachability verification, Steam friend request, localisation retranslation, warning hints, unit card icons, ground offset on re-import, shroud vision/allies/lobby mode, melee draft harness repair, match-settings panel and lobby defaults |
| `ugc-ChaosArena` | `main` | Spanish instruction pages, hero and blacksmith icons, hero ground offsets, twelve lobby seats and a visible board |
| `MeleeRuleset` | `master` | board plays visible by default, 3× sight on soldiers/workers/buildings, building-limit and refused-placement fixes, hero level cap, metadata upgraded to this build, eight lobby seats with both switches on and the deck switches hidden |
| `ugc-SecludedCrest` | `main` | metadata upgraded to this build (a test-player field and a per-model table the engine no longer reads) |

---

## 1. Lobby reachability verification (public lobbies)

**What is proven:** the crafted ENet opening packet is answered by the real authority (`RealAuthorityReachabilityTests`),
a punch-only port verifies, a dead port is refused, the live registry refuses an unverified lobby and publishes the
measured endpoint (`Realm.Network.Tests`, run by hand against a live `Realm.AdminServer`).

**Needs your action / review:**

1. **Deployment.** Every registry node now needs a reachable UDP port — `ReachabilityProbePort` (default **8998**).
   `ReachabilityProbePublicIp` must be set when the node's UDP address differs from the HTTP host name (reverse proxy,
   separate front end). Without it, hosting is refused for every player on that node. This is a config change on the
   live admin servers, not something the code can default safely.
2. **The Windows Firewall fault injection was never run elevated** — this shell has no admin session and a UAC prompt
   cannot be answered unattended. The failure was reproduced with two non-elevated equivalents (`nothing listening on
   the advertised port`, and `a port that refuses unsolicited traffic`). If you want the literal firewall case, run
   `Realm.Network.Tests` from an elevated shell; the harness is in place, nothing else is needed.
3. **Real UAC behaviour of the firewall request** is only unit-tested with a scripted runner. What is untested against
   the real OS: the exact wording of the prompt, whether the rule appears for users in a non-admin group, and whether a
   *third-party* security suite answers the same netsh path. Worth one manual host attempt on a machine with an
   untouched firewall.
4. **Finding the existing rule** uses PowerShell (`Get-NetFirewallRule`) with a `netsh` fallback. The PowerShell path is
   locale-independent by design; the netsh fallback is only reached if PowerShell cannot run and its output is
   localised on non-English Windows (it would then be misread as "no rule" and the rule asked for again). If you ship to
   machines where PowerShell is blocked by policy, this is the piece to look at.
5. **Symmetric NAT is still refused up front** by the pre-existing STUN classification, before verification runs. I left
   that gate alone: it is stricter than the verification (which could sometimes pass a symmetric host through punching).
   Say the word if you would rather let verification be the only judge.
6. **Multi-node failover**: each registry node runs its own check, so a lobby that fails over is re-verified. Untested
   against a real two-node cluster; the path is exercised with an in-process registry host only.

## 2. Steam friend request on the game-over table

**What is proven:** the SteamID64 travels from the seat's own client through the hello, the lobby seat and the
published state to the table (`LobbyIdentityWireTests`, `MeleeLobbyTransportTests`), and the button is only drawn for a
person who stated an id, is not you, and is not already one of your Steam friends (Steam's `SteamFriends.HasFriend(…,
k_EFriendFlagImmediate)`, asked per row when the table is built). A session without Steamworks has no friend list to
read, so it keeps the button and the existing "Steam unavailable" answer rather than deciding for the player.

**Needs your review:**

1. **The overlay call itself is untested** — it needs a Steam build, two accounts and a friend who is not already a
   friend. The API is `SteamFriends.ActivateGameOverlayToUser("friendadd", …)`: Steam has no interface that sends a
   request silently, so the request is confirmed in Steam's own dialog. Please click it once in a real build.
2. **Seats state their own SteamID64.** It is as trustworthy as the display name next to it: a hostile peer could state
   somebody else's id, and the button would open that person's profile. Nothing else is exposed by it, but if you want
   the id attested, it has to come from the account authority instead.
3. **No button is drawn for your own seat, for a computer seat, or for somebody already on your friend list** — a
   request to yourself can only fail, a bot has nobody behind it, and Steam has already accepted the third. The friend
   list is read live, but the table is built once, so a request accepted while the table is on screen keeps its button
   until the table is rebuilt.

## 3. Map text and language switching

**What is proven:** all 12 game catalogs validate; the map script and map metadata were rebuilt and parsed; a language
change now retranslates a `RichTextLabel` (verified against Godot 4.7: `Label` auto-translates at draw, `RichTextLabel`
does not).

**Needs your review:**

1. **ChaosArena's other nine languages still have no instruction pages.** I translated the eleven pages into Spanish
   only (the language you reported). The same keys, same command: `skill-translate-map`. Until then those players read
   the instructions in English — though they will now at least *follow a language switch*.
2. **The map must be re-published** for players to receive the new Spanish catalog: the published package is built at
   publish time and the repo's `manifest.json` is still the authoring one.
3. **The three match-settings strings** (`Visible`, `Explored`, `Full` and their tooltip) were translated in one pass.
   Please read them in German and Japanese at least once; they are short and I could not see them rendered.
4. `MeleeRuleset`'s `HowToPlayInstructions` was removed from the engine in the network-split refactor. I folded its text
   into `HowToPlayObjective` so nothing the player was told is lost — check the lobby briefing still reads well.

## 4. Hero and blacksmith icons (ChaosArena)

**What is proven:** all 40 templates state distinct icons that exist on disk, none collide with an ability or item icon,
and the map's strict metadata parse accepts them.

**Needs your review:**

1. **The mappings are thematic guesses.** They are listed in the commit message; rename any you dislike — it is one
   field per template.
2. **Eight icons were copied into the map from the rules pack's icon set** (`defensive_shield`, `blue_magic_shield`,
   `golden_boots`, `golden_hammers`, `healing_scroll`, `life_tree_banner`, `mana_crystal`, `gold_coin`). They are from
   the same first-party pool the map already ships 117 icons from, but this is the one place where I added binary assets
   to a map. Worth a glance at `Attributions`.
3. **`unit/builder` (the Avatar) states no icon** — it is the unit whose Build menu is being read, not something a
   player buys.

## 5. Ground offsets (YOffset) in ChaosArena

**What is proven:** the map now states exactly what the editor's own auto-detect (`-minY × Scale`) computes, and
re-running the measurement reports zero changes.

**Needs your review:**

1. **Look at the units in game once.** Headless measurement proves the lowest point lands on the ground plane; it cannot
   prove it looks right for a model authored with an odd pivot. All 21 offsets are in the commit.
2. **Only `unit/*` templates were corrected.** Buildings, props and resources already had distinct, consistent offsets
   and were left alone. The same measurement would cover them on request.
3. **The editor now measures a re-imported asset at its template's own scale.** A brand-new template still takes the
   category default (1.0 for units). If a map author changes `Scale` by hand afterwards, the offset does not follow —
   there is no hook for that, and adding one is a larger change than this fix.

## 6. Warning hints (red label and sound)

**What is proven:** every affordability/refusal path calls one HUD method that draws the warning red with an outline and
plays the warning sound; the fade is restarted so a repeated press is not answered by a fading message; the whole
solution builds and all catalogs validate.

**Needs your review:**

1. **The sound is the shared UI warning buzz** (`alert_warning_buzz.ogg`), not an in-match one. It may be too soft or too
   loud under battle audio — tune `ShowFeedbackWarning`.
2. **All feedback changed very slightly**: the label is no longer tinted by `Modulate` as well as by its font colour, so
   every message is a little brighter than before. Check that the gold/blue success messages still read as you like.
3. **Map-script warnings are untouched.** ChaosArena says things like "costs 25 stone; you have 10" through
   `player.SendMessage`, which is the map's own channel; making those red needs a map-side change (or an engine-side
   "warning message" API for scripts). Not done.

## 7. Shroud (vision), allies and the lobby mode

**What is proven:** the authority now describes allies' units to each other and treats their sight as shared vision;
sight is measured by `SightRange` instead of `ScanRadius`; the lobby publishes the shroud mode it will play and the
authority applies it; the popup offers Visible/Explored/Full; Melee's lobby states Visible by default; the melee shroud
options and cheat tests pass (22/22).

**Needs your review:**

1. **Heroes still see 18 m while their soldiers now see 48 m.** You named workers, soldiers and buildings for the 3×
   change, so I left the three `hero/*` templates alone. A hero alone in the field is now the blindest thing on the
   board — say if you want them tripled as well (three `SightRange` values in `MeleeRuleset/metadata.json`).
2. **The three modes were only verified headlessly** (no rendering). Not looked at with a person's eyes: the black→grey
   transition, props and buildings showing last-known data under grey, the minimap alpha, and the `aura` cheat matching
   Visible. This is the item most worth a manual pass.
3. **One piece of the shroud UI changed**: the popup no longer offers "Map default" (you asked for three options). A map
   that states no lobby default shows the mode its `metadata.json` names, so the dropdown always reads a real mode.
4. **The lobby writes its shroud default into the match options when the match starts.** That means a script reading
   `api.MatchOptions` sees a value the host did not type. It is the value the match actually plays, but it is new
   information appearing in that set.
5. **`GetPlayerLanguage` / per-player vision** is unchanged: vision is still shared per team, so an ally's sight reveals
   enemies for you. That matches your description of allied vision; if you wanted individual sight with allies merely
   visible, that is a different design and not what is implemented.

## 8. Melee acceptance suite (`Realm.Simulation.Tests`)

**State: 217 of 240 pass.** Before this batch the suite could not run at all in this checkout: three maps stated metadata
this build does not read (`MeleeRuleset` a briefing list, `ugc-SecludedCrest` a test-player count and a per-model table),
so the world files failed strict parsing. After upgrading those maps, 90 of the 240 failed; repairing the draft harness
took that to 34 with no test regressing, and fixing the rules the failures exposed (below) took it to **23**.

**Rules fixed in `MeleeRuleset` along the way** (each one was a real defect the suite caught, not a test edit):

- a player holding every building they may own was refused the last legal draw, because the count the map measured
  already held the Nexus and the site just placed;
- a refused placement kept the 100 wood the placement had charged, and its site went on offering packages for the tick
  before it was removed;
- a board that declares no spawn anchors was seated anyway, because the engine answers a point on the board for maps
  that place their players themselves - the map now reads the anchor count;
- heroes state `MaxLevel: 6`. **This one is a judgement call**: the progression and the requisition/scaling tests are
  all authored around six levels, and the cap was absent from the metadata, so level 6 was unreachable and one
  requisition case could not pass. If six is not the intended cap, that number is the thing to change.

**What the harness repair changed:** `BeginConstruction` now sends the placement order a player's click sends instead of
casting the card's ability (the card arms placement; nothing is cast), the site is read from the ability's
`PlacementTemplateId` instead of a hardcoded `unit/blueprint`, a seat's open sites are counted separately from raised
buildings, and draft tooltips have their `{{Ability.*}}` placeholders resolved the way the client resolves them.

**The 23 that still fail, by category — each asserts a literal number the content no longer states, so each needs a
decision on which side is wrong:**

| Category | Tests |
| --- | --- |
| Hero ability scaling by level (expects +3 Intelligence and fixed damage/duration literals; the heroes now grow by 2.2/4.4/2.5 Int per level and the abilities scale from that) | `EmberLanceDamageGrowsWithTheHerosIntelligence`, `BackdraftDamageGrowsWithTheHerosIntelligence`, `CinderfieldBurnsItsAuthoredDamageForTheDurationItsLevelStates`, `GroundingShoutDamageGrowsWithTheHerosIntelligence`, `HoldFastDurationGrowsWithTheHerosIntelligence`, `ShieldBashStunGrowsWithTheHerosIntelligence` (12 cases) |
| Bounty and income literals | `KillingTheMonsterPaysItsBountyToTheKillingTeam`, `AlliedSeatsSplitTheBountyANeutralMonsterPays`, `KillingAnEnemyHeroPaysItsLargerBounty`, `ADormantBuildingStopsPayingItsIncome`, `DeepGranariesRaisesTheGoldIncomeByATenth` |
| Composed board instance counts and the removed model table | `TheBoardHoldsTheInstancesAndTheRulesHoldTheTemplates` (the board now places 19 nodes where the test expects 12), `TheBoardPlacesTheCreepCampsItsRulesetAuthors`, `TheComposedMatchPlaysOutOnItsTerrain`, `TheComposedMetadataHoldsBothMapsUnderThisBoardsIdentity` (asserts the merged metadata keeps a `Models` table this build does not read) |
| Draft and production literals | `OpenPoolDraftingKeepsAPickedBuildingAvailable`, `ABuildingPublishesTheSoldiersItMaintainsAgainstItsCapacity` |

Each of those needs a **design decision** (is the authored number wrong, is the rule wrong, or is the test stale?) and
that decision is yours. My judgement: all 23 read as tests holding older authored numbers, so the cheapest path is to
derive the expected values from the metadata the way the harness already derives the draft tooltips and the placement
template. I stopped short of rewriting them because that decides what the map's tuning is supposed to be.

**Map metadata dropped to make the maps loadable at all** (nothing in this build reads either table, and the values they
carried are stated on the templates that use them):

- `MeleeRuleset`: `MapProperties.HowToPlayInstructions` (folded into `HowToPlayObjective`).
- `ugc-SecludedCrest`: `MapProperties.MaxTestPlayers` and the top-level `Models` table.

## 9. Match settings panel, hidden settings and the create-lobby confirmation

**What is proven:** the solution builds, the 12 game catalogs validate, both maps compile against the new
`ILobbyAPI` orders (and their wasm carries the new import names), and a temporary probe against a real `ServerLobby`
confirmed that a lobby now starts with Teams Together and the spectator delay on, that a map can state the spectator
delay, and that a **hidden** setting refuses the host's order while a visible one is applied.

**Needs your review — this is the one area I could not look at:**

1. **The panel has never been seen by a person.** No UI suite can run in this checkout, so the sectioned layout, the
   scroll height (430 px of rows inside a 760 px card), the label column (260 px) and the five section headings were
   written from the existing style helpers and checked by building, not by looking. Expect to nudge spacing.
2. **The create-lobby confirmation needs one manual run**: create a lobby on a map whose board is slow to start, change
   a setting, press CREATE LOBBY, and confirm that the lobby appears in the browser with those settings; then press BACK
   on another attempt and confirm that no lobby is listed and the port is free afterwards. Single-player lobbies and the
   editor's Test Map flow skip the confirmation (there is no registration to confirm before), which is deliberate.
3. **The seat-count error is client-side as well as server-side.** The panel refuses a count below the highest *playing
   human* seat and names that seat; computers do not block it because the authority does not count them either. If you
   would rather the panel simply disabled the control, that is a small change.
4. **Hidden settings keep their value.** A map that hides a row does not change what the match plays for it; the core
   default applies (for example Melee hides the two draft switches, which stay off). Hiding is a presentation rule plus a
   refusal, not a deletion.
5. **`LobbySetting` mirrors `MatchOption`'s numbers 1–9 by hand** and adds the two switches at 10–11, because the two
   switches are lobby state rather than options. A future `MatchOption` must be mirrored in `LobbySetting` or the panel
   will not offer it and the authority will not refuse it.
6. **The 15 new strings are my translations** in all 10 non-English languages (`PLAYERS` existed; `BUILDINGS`,
   `MULTIPLIERS`, `VISION`, `MATCH RULES`, the five section descriptions, the two subtitles, the zero hint, the seat
   error and the confirm title are new). Worth a read in German and Japanese at least.
7. **`IS_AUTOBATTLE_FESTIVAL_ACTIVE` still hides the Match Settings button** in the running lobby (as you said it could
   stay), but the confirmation panel is shown regardless, because creating a lobby now goes through it.

## 10. Test suites at a glance

| Suite | State | Failures caused by this batch |
| --- | --- | --- |
| `Realm.Ecs.Tests` | 49 / 49 | none |
| `Realm.Network.Tests` | 41 / 41 | none |
| `Realm.Simulation.Tests` | 217 / 240 | none — the 23 are the content literals listed in section 8 |
| `Realm.Distribution.Tests` | 21 / 26 | none — the 5 fail identically at the commit this batch started from (`11e8d13`), which I checked in a throwaway worktree: they are the CAS dedup/header-merge and asset-package scenarios, and `Scenario5` reads an external `C:/temp/Asset_Pack` fixture that is out of date |
| `Realm.Client.Tests` | not run as a UI suite | the scene-based suites cannot run in this checkout (the Godot project root is `Realm.Client/`, the test project is not), so the game-over table, the instructions dialog and the lobby popup were verified by build plus the logic tests only |

## 11. The build stamp (`version.txt`)

`scripts/sync-version.ps1` now also writes `version.txt`: the version the build was asked for, the versions derived from
it, the game build number, the Steam app ids, whether the run was a publish build, and the run's number, commit, ref and
URL. It is git-ignored, it is copied into both published archives, and every Actions run uploads it as a `version-stamp`
artifact. Nothing in Realm reads it — the game keeps taking its version from the assembly it was compiled into and its
Steam application from the Steam client — and `docs/review.md` is not a substitute for that file when a bug report needs
to name a build.

**Needs your review:** the workflow change itself can only be proven by an actual Actions run (it was validated as YAML,
and the script was run locally with and without a `-VersionNumber` and with `-Publish` on and off, producing the expected
stamp). Check after the next publish that `version.txt` inside the archive names the version you typed.

## 12. Smaller decisions made without asking

- **Warning colour and outline** are constants on the HUD, not theme entries; if you want them in `UIStyle` they move
  cheaply.
- **`Buildings(slot)` in the melee harness no longer counts an open construction site.** This is the single change with
  the widest reach inside the suite; it fixed more tests than it touched and broke none, but it *is* a semantic change
  to a test helper.
- **ChaosArena's `unit/bulwark_hall`-style names are untouched**; only icons, offsets and the Spanish catalog changed.
- **I did not bump any `GameBuildNumber`.** Maps now state fields (`IconPath` on units, no `HowToPlayInstructions`) that
  an older build would reject, so the maps and this build belong together; a release that ships this build should tag
  the map folders with the matching number.

## 13. Map script symbols, the debug build and the optimized build

### What the PDBs are actually for

Measured, not assumed. A map workspace compiles its script against `lib/Realm.MapAPI.dll` and `lib/Realm.Ecs.dll`, and
the script compiler (ILC) reads a referenced assembly's line information from the symbols beside it. With
`MapTemplate/lib/Realm.MapAPI.pdb` and `Realm.Ecs.pdb` in place, a `Debug` publish of MeleeRuleset carries
`GameAPI.cs` and `EcsSharedMemory.cs` in its DWARF; with every matching copy removed from the machine, the same publish
emits neither file name and is 81 KB smaller, so stepping into `IGameAPI` wrappers or `EcsSharedMemory` shows no source
line at all. Reproduce with `llvm-dwarfdump --debug-line --debug-info MapScript.wasm` from the WASI SDK and grep for
`GameAPI.cs`.

- `Realm.MapAPI.pdb` **is** in the archive — at `data_*_windows_x86_64/MapTemplate/lib/Realm.MapAPI.pdb`. It is not
  beside `Realm.Client.dll` because the client deliberately does not reference `Realm.MapAPI`; Godot copies symbols only
  for the assemblies the client loads. The map API therefore travels as part of the map template a workspace is built
  from, which is exactly where a map author's compile reads it (`EnsureApiLib` copies `.dll`, `.pdb` and `.xml` into
  `lib/`). Nothing was missing from the published archive.
- `Realm.Ecs.pdb` is needed for the same reason (`EcsSharedMemory` lives in `Realm.Ecs`).
- `Realm.EditorAPI.pdb` is needed by nothing: no map script references `Realm.EditorAPI`, no `EditorAPI` source appears
  in the guest DWARF, and `EnsureApiLib` never copies that symbol into a workspace.

### What the workflow now ships

`Keep Only the Symbols a Map Debugger Reads` fails the publish if either of the two symbols above is missing from the
export, then deletes every other `.pdb` under `publish/` **except** the authority's own. Judgement call: the authority
keeps its symbols because the log it writes puts file and line in the stack traces a bug report carries, and the editor
shows that log to map authors. Everything the client side loses describes a process that is never stepped and whose own
assembly is exported without symbols anyway, so those copies could resolve no client trace either.

### The two builds

`bin/script` now holds two artifacts instead of one: `<Assembly>.wasm` (optimized, what every match runs) and
`<Assembly>.debug.wasm` (full DWARF, what a playtest debugger steps). Each compile publishes into `bin/.build-<flavor>`
and is moved into place, so one flavor's build never disturbs the other's artifact, and a failed compile leaves both as
they were. The flavor is stated by the file name, so the retired `bin/script/.build-flavor` marker is no longer written
and is removed where found. The authority asks for the flavor the match needs
(`ServerMapScript.TryFindScript(mapPath, wasmDebug, …)`): `btnTestMap` runs the playtest build, every other match — single
player, multiplayer, dedicated — runs the optimized one, and a map that lacks the requested flavor runs the other and
says so in its log. `MeleeRuleset` was used to prove both directions; the packaging filters were proven with a synthetic
folder that held a debug artifact, a retired marker and a staging folder, none of which reach the manifest or the
`.rmap`.

**Needs your review:** a playtest whose sources are newer than the optimized build now **deletes** that optimized
artifact (`MapEditorHUD.RetireOptimizedScriptLeftBehind`), on the principle that nothing in a workspace may run code the
author has already changed. Publishing or exporting rebuilds it, and until then a match runs the playtest build and its
log says so. If you would rather keep the stale release and accept it being used, that is a one-line change.
**Also unproven here:** the two Windows UI paths (`btnTestMap`, export/publish) compile a Godot-side service that cannot
be exercised outside the game, so they were verified by building and by driving the same publish command and the same
lookup code directly, not by clicking the buttons.

## 14. Quitting a playtest goes back to the editor

`Quit Match` in the resource bar now leaves a playtest the way the `Back to Editor` button does, and both go through one
method (`BackToEditorButton.Return`) so they cannot drift apart; a match that is not a playtest still leaves to the main
menu. The confirmation sentence is new (`Are you sure you want to quit the current match and return to the map editor?`)
and added to all eleven catalogs, English everywhere except Korean, which already translated the main-menu sentence this
one mirrors — the rest of the catalogs are English placeholders for that sentence too, so this matches the state the
translation pipeline left them in.

**Needs your review:** this is a Godot UI path that cannot be driven from here, so it was verified by building and by
reading the one call chain, not by clicking the button in a running game. `GameOver`'s close button and the editor-return
harness now call the same method, so a click-through of any one of them exercises the shared code.

## 15. The food cap holds construction, and an unfinished site can be cancelled

The core now owns four rules that used to be nobody's, so a map that uses the engine's own placement and construction
gets them without asking:

1. **A site takes no food while it is unfinished.** `ResourceEconomyService` counted every entity with a template and an
   owner, so a building being raised consumed its `PopCost` from the moment it was placed. It is skipped now (like a
   `BypassPopulationTag` unit), which is what makes the next rule meaningful: a placement that cannot be held is refused
   *before* anything is charged (`BuildingPlacementService.TryPlace`, new `BuildingPlacementRefusal.FoodCapReached`).
2. **Work stops where it is when the cap has no room.** `ConstructionTick` asks `PlayerPopulationRules.FitsWithinCap`
   for the site's owner before it moves progress, and releases the builder when the answer is no, so the worker idles
   instead of standing at a site that cannot move and the progress stays at the value it had reached. Once room appears
   the same order can be given again and the site carries on from that value.
3. **Both silhouettes say so in red.** The placement preview gained a `FoodCapReached` state painted in the same red as
   terrain blocking (it was blue whenever the ground was clear), and clicking in that state warns instead of charging. An
   unfinished site whose owner has no room dresses its own silhouette in red through a new `silhouette_tint` shader
   uniform (default is the old dark fill, so dormant and ruined buildings are unchanged).
4. **`cancel_construction` takes a site down and refunds its whole cost.** The card that used to sit in an unfinished
   building's grid with an empty callback now issues this core order, drawn red through a new `CommandCardItem.IconTint`,
   and the authority pays back the full gold/wood/stone the placement charged before destroying the site. The site is
   taken down rather than killed: no death, no bounty, and the map's ability state for that id is forgotten.

**What this was verified against:** a scratch harness drove `MeleeRuleset` through the real `ServerSimulation` (the same
path the smoke verifier uses) and confirmed each step - refusal at the cap charging nothing, a site taking no food, work
advancing then freezing with the builder's task dropped, and `cancel_construction` destroying the site and returning
exactly its 100 wood. The melee construction/draft suite (`MeleeConstructionDraftTests`, 20 tests) still passes, and the
shader loads in Godot with the new uniform.

**Judgement calls and limits:**

- **Maps that raise buildings themselves are outside this.** A map that spawns a site through its own spells and governs
  its own completion keeps its own rules; the pause and the refund are the engine's, so they apply to the engine's
  placement order and to sites the engine is raising. This is the caveat you accepted.
- **The engine's cancel card is offered only on sites the engine is raising** - a map holding a site short of completion
  (`ConstructionCeilingFraction < 1`, which is how `MeleeRuleset` holds a draft site before the player picks) keeps that
  site's grid to itself, so the engine cannot take a draft site out from under the map. Those maps keep their own way
  out, which they already had.
- **A refused placement is not a paused one.** Clicking with no food room raises nothing at all; only a site that was
  already placed pauses. If you would rather the click still raised the site so its progress could be seen paused at
  zero, that is a different rule and a small change.
- **The red site silhouette is derived on the screen**, from the replicated population and the template's own `PopCost`,
  rather than from a new replicated flag. It therefore matches the authority's arithmetic without a wire change, but it
  re-reads two components per unfinished site per rendered frame.

## 16. The shroud warning is read from the seat the test run will seat the player in

The editor warned "You have not placed any units, you won't see anything due to Shroud" whenever the board held no
unit at all, which never looked at the seat the run gives the player. A map whose test settings randomize the slots can
seat the player anywhere, so a board full of that seat's units could be called empty, or a board with nothing on the
player's seat could pass unmentioned.

The seat plan now lives in one place. `MapTestSeating` reads a map's own test settings into a
`MapTestSeatingPlan` - the player's slot, the computer players' slots, and whether the player's slot is actually known -
and `LobbyManager.HostSinglePlayerGame` seats its run from that plan instead of its own copy of the shuffle. The warning
then asks `MapTestSeating.CouldSeatLocalPlayer` for each unit's owner: only a unit on a slot the player could be given
counts, so a seeded randomized run is warned about the seat it will really take, and a run that randomizes without a
seed counts every playable slot (the warning then holds only when no seat could show anything, which is never wrong).

**Proven:** the extracted plan was compared against the lobby's previous shuffle for 612 combinations of slot count,
computer count and seed, and every one matches, including that a seeded run is repeatable and an unseeded one is not.
`CouldSeatLocalPlayer` was checked for 2870 slot/plan combinations, and the warning's own rule was checked for an empty
board, a unit on the player's seat, a unit on another seat, gaia-owned units and an unseeded run. The editor click itself
cannot be driven from here, so `btnTestMap`'s warning is verified by those rules and by building, not by clicking.

**Judgement call:** gaia-owned units count as "something the author placed". Gaia (slot 0) is the placement palette's
default owner, so a board an author never gave a seat to - scenery and creeps - would otherwise be called unplaced on
every test. Gaia units do not lift anybody's shroud, so if you want the warning to fire on a gaia-only board as well,
delete that one line in `MapEditorHUD.Dialogs.LocalTestSeatHasUnits`.

## 17. A greenlight override now names the creator it is for

An admin greenlight only marked a map title as approved. Publishing authorizes by maintainer, and a title with no
maintainers authorizes anybody, so the first key to publish after a greenlight kept the map - the override was a
permission anyone could spend.

The override now names a creator, and that name is the thing that decides who may publish: `greenlight -c <username>`
signs `greenlight:<title>:<creator-slug>`, and the server resolves the username through the name locks to the key that
holds it. The title is then claimed for that key (`MapMaintainerHelper.TryClaimOwnership`, which writes the same
`map_ownership` spellings the lookups read), so publishing is refused for every other key until an administrator
releases the title or adds that key as a maintainer. A greenlight naming a name nobody holds is refused (404), a
signature that does not name the creator is refused (400), and a greenlight for a title another creator already holds is
refused (409) rather than taking the map away from them. Cluster peers apply the same rule from their own name locks and
reject an event they cannot resolve, so no server is left with a greenlit title that nobody owns.

**Proven against a real `Realm.AdminServer`** (started on loopback with a generated admin key): the 404 for an unheld
name, the 400 for a title-only signature, the 200 that names the resolved key, the 409 for a second creator claiming a
held title, `/api/publish_map/initiate` refusing the stranger's key with "owned by different maintainers", and the named
creator passing that same check.

**What this does not cover:** the admin tool has no command to release a title or hand it to another key; that is still
`addmaintainer`/`removemaintainer` plus, for a full transfer, the manifest-removal path. The `admin_greenlight` event's
signature names the creator, not the resolved key, so a peer resolves the name itself; if a name is re-registered to a
different key between the two servers seeing the event, each applies the key its own name locks state, which is logged as
drift rather than merged.

## 18. The Steam surface map scripts may opt into: achievements, stats and cloud saves

`IGameAPI` gained fourteen Steam members (`Realm.MapAPI/IGameAPI.Steam.cs`). Nothing in the engine calls them and no map
has to: a map that says nothing about Steam behaves exactly as it did. They exist so a future map can carry Steam
requirements without the engine changing again.

**Achievements and stats** - `IsSteamAvailable`, `UnlockSteamAchievement`, `IndicateSteamAchievementProgress`,
`SetSteamStatInt`, `AddSteamStatInt`, `SetSteamStatFloat`. The map states its own identifier; the screen turns it into
`realm_<map>_<identifier>` (`SteamScriptNames.ForMap`), so two maps may both call an achievement `chapter_one` without
touching the same Steam achievement, and a map can never reach one of the game's own.

**Cloud saves** - `RequestSteamCloudFiles`, `IsSteamCloudSynced`, `GetSteamCloudFileCount`, `GetSteamCloudFileName`,
`HasSteamCloudFile`, `ReadSteamCloudFile`, `WriteSteamCloudFile`, `DeleteSteamCloudFile`. Files live under
`maps/<map>/` inside the player's Steam cloud, which is the folder that separates one map's saves from another's. Reads
are a round trip because the authority cannot read Steam: the map asks, the seat's screen uploads what it holds with a
`NetworkFrameKind.SteamCloudUpload` frame carrying the request's handle, `ServerSteamCloud` keeps that copy per seat and
the map polls until it is there. Writes and deletes update that copy at once and are sent to the screen, so a map reads
back what it just wrote. Limits are one shared rule (`SteamCloudLimits`): 64 characters of plain file name, 32 KB per
file, 64 files and 128 KB per player for one map.

**How it travels:** the authority has no Steamworks, so every call becomes a `PresentationKind.Steam*` request addressed
to the player slot, applied by `SteamScriptBridge` only on the screen sitting in that seat, never by an ally, a spectator
or a replay. The requests are state, so they ride the reliable presentation stream. A seat counts as being on Steam when
its own hello stated a SteamID64 (`ServerSimulation.SetPeerSteamId`, inherited from the lobby seat for a lobby-started
match), so computer seats, unheld seats and screens without Steam answer false and every other call is a no-op for them.

**Proven (57 checks against MeleeRuleset through the real `ServerSimulation`):** every member raises the request it should
with the right seat, identifier and payload; a seat without Steam raises nothing and its cloud request settles empty at
once; names are namespaced per map and an unusable identifier names nothing; the cloud round trip keeps file order, drops
an answer to an older request, refuses a name with a folder in it and a file past the size limit, and reads back what it
wrote; a real map script built against the new surface (all fourteen imports are present in the wasm) calls every member
on its first tick and the authority answers each one without trapping, including a cloud request the probe then answers
and the script reads.

**Not proven here:** the Steam calls themselves need Steam running, so `SteamService.Script.cs` was written against the
Steamworks.NET signatures (checked by reflection: `GetFileNameAndSize(index, out int)`, `FileRead/FileWrite/FileDelete`,
`SetAchievement`, `IndicateAchievementProgress`, `SetStat`, `StoreStats`) and built, but no achievement has been unlocked
or cloud file written against a real account. Note also that the Steam achievement and stat names a map asks for do not
exist in the application's Steamworks configuration yet - they have to be registered before they mean anything, and
Steam refuses an unknown name without failing the match (the refusal is logged).

## 19. The cinematic camera surface map scripts may opt into

`IGameAPI` gained eight camera and cinematic members (`Realm.MapAPI/IGameAPI.CameraCinematics.cs`). Nothing in the engine
uses them and no map has to: a map that says nothing about cinematics leaves the camera exactly as it was. They exist so a
campaign map, a racing map or any map with a scripted moment can drive the camera itself.

**Camera** - `LockPlayerCamera`, `MovePlayerCameraTo`, `FollowUnitWithPlayerCamera`, `StopPlayerCameraFollow`,
`ReleasePlayerCamera`. A move is a shot rather than a pan: the camera is pointed at the point the map named for the whole
move, a duration of zero cuts, and a positive duration eases at both ends. A follow sits at a stated distance, height and
bearing behind a unit (bearing either fixed in the world or aligned to the unit's own facing) and looks at it with the
lag stated in seconds, so it reads the same at any frame rate. Both keep the camera above the terrain and write the
reading back into `CameraState`, which is what keeps the minimap, the HUD and the player's own controls consistent with
where the shot left the camera.

**Dressing** - `SetCinematicBarsVisible`, `ShowCinematicSubtitle`, `SetScreenFade`. The overlay is a `[GlobalClass]
CanvasLayer` created under the match host when a map first asks for it, so it leaves with the match; bars are ten percent
of the screen, a subtitle carries a speaker and fades itself out, and the fade covers the HUD as well, which is what a
scene change needs.

**Addressing** - every member takes a player index, and `IGameAPI.EveryPlayer` (`-1`) addresses every screen, which is what
a co-operative cutscene or a cinematic between rounds uses. The requests are state, so they ride the reliable presentation
stream.

**Proven (35 checks against MeleeRuleset through the real `ServerSimulation`):** every member raises the request it should
with the right seat, place, look-at point, unit id, duration and payload; a negative duration becomes a cut, a shot to a
place that is not on the board is dropped, an invalid unit stops a follow rather than starting one, `EveryPlayer` is
addressed as -1, and a request naming a slot that is not a seat is dropped. The same run built a real map script against
the surface - all eight imports are present in the wasm and the authority answered every one without trapping. A headless
Godot run then drove the client half: a cut lands where it was told, points exactly at the point it was given, a hold
takes and returns the camera, a release keeps the place the cutscene left, and the overlay builds, draws, sizes and clears
its bars, subtitle and fade and lives under the match that asked for it.

**Not proven here:** the timed move and the unit follow need a live match, because `CameraControl._Process` only runs a
script camera when the world holds a `CameraState`; both use the same ground clamp and look-at code the cut was verified
with, and the follow's framing was reviewed against the same math instead. The end-to-end path a real match takes
(presentation request -> client handler -> camera) is likewise only compiled, not clicked.

**`origin/feature/chariot-racer` was reviewed and left alone.** That branch already has a chase-camera pair
(`SetPlayerCameraFollowUnit` / `SetPlayerCameraView` / `ResetPlayerCamera`, plus `PlayerId` extensions), and the review
found it sound for what it does: the follow snaps on its first frame, derives its position from the rotationally smoothed
yaw (which is what its own fix commit was for), clamps the camera above terrain and restores the player's controls on
reset. It is deliberately a chase camera rather than a cutscene camera - it cannot move the camera to a place, and it has
no bars, subtitles or fades - so the generic surface here is the thing that racing map should move to later, and none of
its member names collide with the new ones. No change was made on that branch.
