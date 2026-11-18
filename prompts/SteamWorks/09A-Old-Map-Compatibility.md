# 09A — Keep Old Maps Playable After Engine Updates

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 09.
**Unblocks:** the first public Workshop publish, and 19 (which records the guards in `AGENTS.md`).

## User story

As a map author, I want a map I published to keep working after Realm updates, so my Workshop item does not silently stop being playable and I decide for myself when to rebuild it.

As a player, I want to join a lobby for an older map without switching game versions, installing a second copy of the game or reading an error about build numbers.

As the developer, I want to keep changing the engine without promising that every historical build of Realm stays runnable and without maintaining a library of old clients.

## Context

Today the game cannot play an old map at all. `metadata.json` carries `GameBuildNumber`, and it is compared with `RealmVersion.GameBuildNumber` for exact string equality in `MapSelectionService` and `LobbyCreate` (which shows a build-mismatch modal), and again in both authority handshakes (`server_rejected_build`). `MapUpgradeService` can move a map forward, but a player never reaches it: it is invoked from the editor only, in `MapEditorHUD.MapUpgrade.cs` and `MapEditorHUD.MapLoad.cs`. So a map authored on `v0.0.4` is refused by a `v0.0.6` build with no path forward for a player who only has the Workshop item.

The current client also carries a half-built answer to this: a `versions/<build>/` folder convention, a version dropdown in the main menu, and relaunch-or-download popups in the lobby browser, lobby creation, the lobby room and the replay list, all pointing `LobbyManager.GetVersionExecutablePath` at another installed build or at GitHub releases. **It is coded, it has never been exercised, and it is removed rather than finished** (requirements 26-29). It is not implementable as a shipped feature — no Steamworks API installs an old depot, a beta branch needs the player to switch manually, and a second AppID per version means a store review per version — and it is not needed once map data migrates forward and the script import surface is additive. Its existence is a documentation trap: a task that only closes the mismatch gate leaves the dropdown and four relaunch paths orphaned in the build.

Three separate things can break an old map, and each has a cheaper remedy:

1. **The map's data does not parse.** Metadata, terrain, templates and package layout are read by a strict canonical loader with no fallbacks. `MapUpgradeService` already repairs exactly this, one sequential migration per release.
2. **The map's script calls a map-API surface that changed.** The compiled `.wasm` is instantiated by the authority and its imports are satisfied by the current host. If a host import was removed or its signature changed, instantiation fails.
3. **The engine's behaviour changed meaning.** A component, a rule or a unit semantic now means something else while still parsing and linking.

Cases 1 and 2 are cheap to make permanent. Case 3 is the only genuine break, and it is the only one worth charging for.

## What is possible, and what is not

**Not possible on Steam.** The game cannot install an old depot of itself: there is no Steamworks API to download an arbitrary depot, SteamCMD needs a login and is a programmer tool, and Steam beta branches require the player to switch manually in the Steam client, which is the friction this migration exists to remove. A second AppID per version means a store review per version. Shipping an old build as a Workshop item is a second distribution channel for executables under Valve's content platform, and it still would not work: an old build can only join a Steam lobby if it was compiled with Steam networking and the lobby protocol of the current build, which builds that already shipped were not. Finally, the launcher that tried it never ran: there is no tested behaviour to preserve, no player who has used it, and no failure it has been shown to fix.

**Therefore no old build can ever be retrofitted.** Whatever mechanism is chosen has to exist in the current build and every build after it. Since every map that exists today predates it, the guarantee starts at the first build that ships with this task, and older maps are upgraded once by their author or by a reviewed mirror. Say this plainly in the release notes instead of implying a guarantee that cannot be kept.

**Possible and cheap.** Keep the map-data path migratable at load time, and keep the map-script import surface additive. Under those two rules an old map keeps running on a new engine, with a new client, with no second build, no version switch and no depot: the map's data is upgraded on a private copy, and its unchanged `.wasm` still links against a host that never removed the imports it uses.

**The single most important rule.** Never remove a WASM host import, never change an existing import's signature or meaning, and never reuse a component id. A data migration can be run for a player without the author; an ABI break cannot, because only a recompile fixes it, and only the author (or a mirror maintainer) can recompile. Everything else in this task is secondary to that rule.

## Requirements

### 1. A generation number that states what the engine promises

1. Add `EngineGeneration` (integer) to the map's info metadata and to the generated schema. It states which semantic generation of the engine the map was authored for. Start at `1` for the first Steam-era build, and add a sequential `MapUpgradeService` migration that writes `1` into older maps. No fallback reader.
2. `EngineGeneration` changes only when a change cannot be expressed additively, that is when requirement 3's rules have to be broken. It is not the game version and it does not change per release.
3. Keep `GameBuildNumber` as the migration key it already is: it says which migrations a map still needs. It stops being a playability gate for the map.
4. Change the gate. `MapSelectionService` and `LobbyCreate` must stop refusing a map because its build string differs, and instead refuse only when `EngineGeneration` is newer than this build (the map was made by a newer game) or older than the oldest supported generation. A map of a supported generation is playable after migration, and the lobby must not show the build-mismatch modal for it.
5. Keep the peer handshake's exact build comparison between two screens. Both peers run the same Steam install, and a player who has not restarted after an update must be told to restart. That check is about the two *clients*, not about the map.

### 2. Migrate the map's data at load, on a private copy

6. Run `MapUpgradeService` when a map is opened for a lobby or a match, not only in the editor. The client owns the service and already resolves the map folder in `PrepareHostedLobbyAsync` before the authority is started, so it migrates there and hands the authority the migrated folder.
7. Never migrate in place. A Workshop item's folder belongs to Steam and is replaced by its next update; a local map folder belongs to its author. Migrate into a session folder under the user data directory, keyed by the map's Workshop item ID (or local map identity) and its package hash, and play from that copy. **Keep exactly one copy after the match ends: the most recently used one.** Players commonly replay the same map several times and ignore the rest, so reopening the same key reuses the copy instead of copying and migrating again, which also removes the first-time upgrade cost on a replay. Opening a lobby for a different map, or for the same map after its package hash changed, replaces it: build the new copy first and delete the previous one only once the new copy is complete and verified, so a failed copy never leaves the player with nothing. Sweep any other session folder at startup as an orphan. Reuse is allowed only on an exact key match and the key is recomputed from the installed item on every open, so a retained copy can never make a stale version of a map playable.
8. The migration must be deterministic: the same package always produces the same bytes. Joiners run the same migration on their own installed copy, so the host's presentation data and theirs agree.
9. Keep the map's identity hash tied to the **published package**, not to the migrated copy. The lobby advertises the hash every machine can compute from the Workshop item it already has. Record the original hash in the session folder so the authority can report it.
10. A migration that fails, or a generation that is no longer supported, refuses the map with one actionable line: "This map was built for an older version of Realm and needs to be upgraded by its author." Do not attempt a partial load and do not fall back to reading the old shape.

### 3. Make the map-script surface additive

11. Never remove a host import, never change an import's signature, and never change the meaning of an existing import or property. A new capability is a new import and a new member; `IGameAPI` grows, it does not change.
12. Never reuse a component id, and never renumber or repurpose an existing replicated component. Additive growth only.
13. When a map's script cannot be instantiated because an import it needs is gone, fail with a clear error naming the map and the missing capability, and tell the player it needs an updated build of the map. Do not try to shim the import at runtime.
14. Keep the editor's compile-time surface consistent: a map always compiles against the current `Realm.MapAPI`, and the editor warns when the map's `EngineGeneration` is older than the current one, so the author knows they are working on a map that has already been charged a generation.

### 4. Guards that keep the contract from eroding

15. Extend the existing `ReplicationContractTests` to assert that the replicated component table is append-only: no id removed, renumbered or reused. Compare it against a **committed baseline**, not a git tag. The release workflow checks out at depth 1, so tags are absent there, and this repository's tags are `0.0.1_Pre-Alpha_Rev_*` rather than a comparable release version, so a tag comparison would compare against nothing and pass. This is the one place the repository already protects the wire contract, and the guard belongs beside it.
16. Add a build-time check that compares the map-script import surface this build exposes (the generated bindings/WIT and the assembly's public members that the generator discovers) with a **committed baseline** (`MapApiSurface.baseline.json` at the repository root), and fails the build when an import disappears or its signature changes. A generation bump is the only way to pass that check, and it must be a deliberate, visible edit in the same change.
17. Create the agent skill that performs and explains the analysis: `.agents/skills/skill-map-api-surface-guard/SKILL.md`, in the shape of the skills already in that folder (frontmatter with `name`, `description` and `metadata.opencode/slash`, then a Purpose and a numbered Workflow). It must:
    - regenerate the surface and the baseline with the one documented command the build uses, so the skill and CI cannot disagree about what "the surface" is;
    - diff the regenerated surface against the committed baseline and report every change per member, classified as added, removed or retyped;
    - treat added as normal, and report each removal or retype with the map-script members that would stop linking and the generation gate that follows;
    - check that `EngineGeneration` was bumped in the same change when the diff is breaking, and fail the analysis when it was not;
    - print the regeneration command rather than editing `MapApiSurface.baseline.json` on its own, so accepting a surface change is always an author's deliberate act rather than a tool's;
    - run `dotnet build Realm.slnx` at the end, per the repository's rule that code is compiled after it is changed.

    The point of the skill is that the rule in this task is enforceable by a person reading a pull request: the baseline diff is the evidence, and the skill is how it is produced and read without remembering which generator emits which file. Neither guard may forbid adding: growth is the normal path, and only removal and retyping are failures.

### 5. State the support window, and the way out

18. Support at least the current generation and the one before it, and show which generation a map was authored for in the map details and in the lobby when it is not the current one. The window is a product decision recorded in one place, not a per-task judgement.
19. When a generation is dropped, the map stays in the library and states that it needs an update; it does not disappear and it does not fail at join with a protocol error.
20. Do **not** build old-engine hosting, a version switcher, side-by-side builds or a per-generation depot. If a future break makes an old generation genuinely worth running, that is a new product decision with its own task, taken when there is content that justifies it.

### 6. What to do with the decoupled simulation layer

21. Decoupling the in-match runtime (authority, replication, presentation) from the menus and editor into separate projects is worth doing for its own reasons: it makes the dependency direction explicit and it gives requirement 16 a clear surface to check. Do it as a packaging and structure change if the task that needs it exists.
22. Do not treat that decoupling as a version-switching mechanism. Swapping the simulation layer per match does not work: the client presents the world and must read the map's data itself, so a versioned authority alone leaves the client unable to draw the map, and loading a second version of the same assembly into one .NET process is not possible without a separate process and a separate client.

### 7. Release operations that make a bad release survivable

23. **A pre-release branch, not a second product.** Run preview builds on a Steam beta branch (opt-in, optionally password-protected) so creators and players can report map breakage before the build reaches everyone. A preview build carries the same `EngineGeneration` unless its ABI genuinely changed, and Workshop publishing is disabled in it (task 07), so no map can be authored against the preview surface and left in the catalog when the release lands; creators test existing maps and their own work locally, export a `.rmap`, and publish from the public build. Never put a preview build on the default branch, and never use the branch as a way to run an older engine: it is a forward channel only.
24. **Rollback is a real operation, and it must be safe to use.** Keep the previous release build available so it can be set live again from the Steamworks Builds page if a release breaks a wide part of the catalog. Rollback is only safe if the newer build did not change persisted local state the older build cannot read — settings, the local map store, the asset index — so either keep that state forward-compatible across a release or state in the release notes what a rollback discards. Do not roll back silently: a rolled-back player whose maps were published from the newer build sees them refused by the generation gate, which is honest but has to be explained.
25. **Keep the two remedies distinct.** A wide break is answered by rollback plus a corrected build; a narrow break is answered by the affected authors rebuilding and republishing their maps. Do not answer a wide break by asking every creator to recompile, and do not answer a narrow break by reverting the engine for everyone.

### 8. Retire the version launcher, and state what a replay promises

26. **Delete the launcher machinery rather than closing its gate.** All of it exists today and all of it goes: `LobbyManager.GetBaseGameDirectory` and `GetVersionExecutablePath` including the `/versions/` path parsing, `MainMenu.PopulateVersionDropdown` and the dropdown it fills, `LobbyBrowser.ShowVersionMismatchPopup` with its relaunch and download actions, the `VersionMismatchCheck` filter beside the browser, `LobbyCreate`'s map build mismatch modal, `LobbyRoom.CheckHostVersionMismatch` and its `ShowVersionMismatchPopup`, and `ReplayListPanel.ShowVersionMismatchPopup`. Remove the nodes they read, their scene entries and their localization keys. Do not leave a disabled control, a hidden checkbox or a method with no caller: an unreachable relaunch path is the same defect one refactor later.
27. **Give each context the one line it actually needs.** A build difference between two screens is a Steam update, not a version to go and install ("Restart Realm through Steam to update"). A map whose generation is newer than the build keeps the "needs a newer version of Realm" line with an update action. A lobby whose map is on an older supported generation opens, per requirement 4. A replay this build cannot read names the version it was recorded with and says this build cannot open it. None of these is a dialog offering to start another build.
28. **State what a replay promises, in the code and in the release notes.** A replay is supported while its recorded format stays readable, which is the intention behind changing the format only when there is no other way. There is no promise that an older format opens, no migration of old replays and no retention policy beyond whatever the player keeps locally. `ReplayListPanel` already reads `GameVersion` from the replay header: keep that field, use it to decide readability, and refuse with one line instead of relaunching.
29. **Do not replace the launcher with anything.** No depot download, no branch-switch UI, no bundled second client and no "install the version this lobby needs" service. If an unreadable replay or an unsupported generation ever justifies an old build, that is a new product decision with its own task, exactly as item 20 says about old-engine hosting.

## UX

- A map authored on an older build shows a small "Made for an earlier version of Realm" note in the map details, not a warning dialog.
- Hosting such a map works. If the upgrade takes a noticeable moment the first time, the existing lobby loading stage covers it; do not add a new progress dialog.
- Joining such a map works the same way; the joiner's own copy is upgraded quietly.
- A map from a newer build than the player's game says "This map needs a newer version of Realm" with a Steam update action, not a build-mismatch modal.
- A map whose script can no longer run says "This map needs to be updated by its author" and links to its Workshop page.
- A build difference between two screens says to restart through Steam. There is no relaunch button, no download button and no version picker anywhere in the client, including the main menu and the replay list.
- A replay recorded by a build whose format this one cannot read says so in one line, names the version, and opens nothing else.
- Nothing in the primary flow mentions generations, hashes or migrations. The advanced map details may state the generation the way they state the version.

## Out of scope

- Downloading or launching old game builds, and per-version AppIDs. The launcher that already exists is deleted by requirement 26 and nothing replaces it; the beta branch in requirement 23 is a forward-only pre-release channel and never serves an older engine to a player.
- Publishing old engine versions or simulation layers as Workshop items.
- Recompiling another creator's script automatically. Only the author or a mirror maintainer can do that.
- Changing `MapUpgradeService`'s migration model. Each release still transitions from the immediate prior version.
- The Workshop identity and version rules (task 09) and the publish flow (task 07).

## Acceptance criteria

- A map whose `GameBuildNumber` is older than the game runs in a hosted lobby without the build-mismatch modal, after its data is migrated onto a private session copy.
- The original Workshop folder is unmodified after a host and a join have played the map.
- The lobby's advertised hash is the same on the host and on every joiner, and matches the published package.
- The authority receives a migrated folder, and a failed migration refuses the map with the actionable message rather than a partial load.
- A map from a newer generation than the game is refused before the match starts, with an update action.
- `EngineGeneration` round-trips through save, export and publish, and the migration that writes `1` into older maps builds.
- The append-only component guard fails when a replicated component id is removed, renumbered or reused, and it compares against the committed baseline so it runs in a depth-1 CI checkout.
- The import-surface check fails when a WASM host import is removed or retyped, and passes when one is added.
- `.agents/skills/skill-map-api-surface-guard/SKILL.md` exists, reproduces the CI comparison from the documented command, and reports a breaking diff without a generation bump as a failure.
- The version launcher is gone: no `versions/` path resolution, no version dropdown, no relaunch-or-download popup in the lobby browser, lobby creation, lobby room or replay list, and no orphaned scene node, filter checkbox or localization key.
- A replay from an unreadable format is refused with one line naming its version, and nothing in the client offers to start another build.
- An old map whose script still uses only surviving imports runs unchanged on the current build.
- No old build, depot download, version switcher or per-generation runtime exists in the solution; the beta branch is a pre-release channel only.
- The most recently used session copy survives the end of a match and is reused when the same map is opened again without a Workshop update; opening a different map, or the same map with a new package hash, replaces it and never leaves a half-built copy behind.
- A preview build is available on a Steam beta branch, a preview that changes the ABI carries a new generation, and no Workshop item can be created or updated from it.

## Pitfalls

- Do not keep the map's `GameBuildNumber` as a playability gate "for safety". It is the exact check that makes every published map unplayable after the next release, and it is what this task exists to remove.
- Do not migrate a Workshop item in place. Steam owns that folder and will overwrite it, and writing into it breaks its update tracking.
- Do not hash the migrated copy for identity. Two machines that migrated the same package must agree, and the package they already share is the thing they can both hash.
- Do not migrate on every launch of the library. Migrate when a map is opened for a lobby or a match, and reuse the session folder while it is valid.
- Do not add a per-generation reader, a "version 1 metadata" parser or a fallback branch in the loader. The migration chain is the only path, as the repository policy already states.
- Do not treat a script ABI break as fixable at runtime. If an import is gone, only a recompile fixes the map; say so instead of shimming.
- Do not build a compatibility promise for builds that already shipped. They cannot participate; the guarantee starts here.
- Do not leave the version launcher half-removed. Closing the mismatch gate and leaving the dropdown, the four relaunch paths and the browser's mismatch filter is the failure mode this task replaced a "previous plan" with: the code looks load-bearing, nobody knows it never ran, and the next agent either finishes an unimplementable feature or deletes it in a different task.
- Do not promise replay portability. Replays are not maps: they record a simulation, and a format change makes an old one unreadable with nothing to migrate it from. Say that plainly instead of adding a second compatibility contract nobody asked for.
- Do not let the generation number become a second game version. It changes only on a break that the additive rules cannot express.
- Do not silently drop an old generation. Mark the maps and say what the player can do.
- Do not hard-link the retained copy to Steam's folder to make reuse cheap. A hard link shares the file, so an in-place Steam write still changes the map under it; reuse is keyed on an unchanged package hash, not on a shared inode.
- Do not reuse a retained copy whose key no longer matches the installed item. Recompute the key on every open and discard a stale copy.
- Do not describe rollback as free. It works only while local state written by the newer build can still be read, and maps published from the preview become "needs an update" for everyone.
- Do not let retention grow into a cache of many maps. One copy, the most recent, and everything else is swept.
