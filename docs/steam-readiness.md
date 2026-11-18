# Steam branch readiness

What the Steam-first branch has actually been verified against, and what has not. It is the companion to
[the Steam test plan](steam-test-plan.md), which holds the steps: this document says which of those steps start from a
proven state and which start from an assumption.

Nothing here was played. The branch was coded and audited by compilation, static reading and Steam's documentation
alone, so every item under **Unproven** is unverified until someone runs it.

## Statically verified

Each of these holds for the current head of `steam_rewrite` and was checked by the method named, not inferred.

| Claim | How it was checked |
| --- | --- |
| The solution compiles | `dotnet build Realm.slnx` with `-c Debug` and `-c Release`: 0 errors in both, and 188 warnings, which are almost all nullability warnings in the editor dialogs. One of them is new and deliberate: the localization generator reports `MapDetails.Content.cs` translating a variable, because the stat rows take their template as a parameter so the literal stays at the five call sites, which the same generator does read. Release matters because `btnPublish` and the CI upload compile it, so a Debug-only green would not have covered the bytes that ship. |
| The map API surface has not changed | `Realm.WasmBindgen`'s `MapApiSurface.baseline.json` guard runs as part of the build and fails it on a changed surface, so a green build is a passing check. |
| A script built against an older engine is refused by name | `WasmScriptHost` registers every host binding before it validates the module's imports, and the host surface lives under `custom:game/game-api`, which the check does not treat as runtime-provided. A module importing a host function this build no longer defines is rejected with the list of missing `module::name` entries and the instruction to rebuild the workspace, so the contract "never remove a host import" fails loudly instead of as an opaque linker error. |
| A map still compiles against the published API | `dotnet publish MapTemplate/MapScript.csproj -c Release` compiles `MapScript.dll` with no errors, which is the one check that consumes `lib/Realm.MapAPI.dll` and `lib/Realm.Ecs.dll` the way a map author's workspace does. The final WASI link needs a Wasi SDK (`WASI_SDK_PATH`), which this machine does not have, so the artifact was not produced here and a workspace publish is still unproven. |
| The replicated wire table is intact | Reproduced `ReplicationContractTests` from source: all 67 components in `ReplicationComponents.baseline.json` still resolve to the id they ship under through `ComponentTypeRegistry`'s ordered canonical list, no id is reused, and the 67 codecs `ReplicatedComponents` registers are exactly those components. No replicated type sits outside the canonical list, so no id depends on registration order. |
| The component ids map scripts read are stable | `ComponentTypeRegistry.VerifySharedComponentIds` compares the canonical order with `ComponentIds` at startup and throws, naming the drifted type, so a reordering bug cannot boot. |
| The client cannot call the authority in process | `Realm.Client.csproj` carries only the `ReferenceOutputAssembly="false"` ordering reference; no `using Realm.ListenServer` exists in client source. |
| Nothing references a deleted page or file | Every `GetNode`/`GetNodeOrNull` literal resolves against its scene apart from fallback chains and nodes inside instanced sub-scenes; every `res://` literal resolves against the Godot project that owns the referencing file apart from the optional `config/featured_workshop_items.json`, whose absence the trust policy treats as "features nothing"; no orphaned `.cs.uid` remains. |
| A seat's SteamID is the identity Steam asserted | `ListenServerHost.ResolveSeatSteamId` takes `_transport.TryGetPeerSteamId` first and uses the hello's number only when the transport asserts none (the host's own screen on loopback, and a match started without a lobby). It is the identity used for the lobby seat, the slot reclaim, the kick list and `SetPeerSteamId`. |
| No catalog carries a key its call sites lost | `Realm.TranslationTool -- prune` reports nothing to prune for all ten catalogs, and each catalog's key set is a subset of `en.json`. |
| Every catalog carries the strings this branch added | `Realm.TranslationTool -- verify` reports 0 missing, 0 empty, 0 placeholder mismatches and 0 BBCode mismatches for all ten catalogs. The wording is unread by anyone (see **Unproven**). |
| A built label reads in the new language, not the one it was built in | Every Steam-surface label that mixes a translation with a live value is registered with `LocalizedText.Bind`/`Set`: the lobby's players heading, the map library's creator line, the storage detail labels, the version rows and the map-details title and stat rows. `AddStatRow` now splits the *translated* template on `:` or `：`, so the dull-gold label half is translated and a full-width colon still separates it. The download button and subtitle are the exception: they translate a template at the moment the flow writes them, because registering them would put `Downloading...` back on a button the next language change arrives after. The editor follows the same rule for what it draws and the opposite rule for what it says and forgets: the object tree's `Units (12)` headings translate the category key and the animation count badge and the map filter's `📦 [Map] ...` items are registered, while the two-second feedback lines (`Coordinate 'x' created.`, `'{0}' Operation Completed`, `Overlay Mode: ...`, `Camera -> ...`, `Buy ...`, `Upgrade ...`, `Lvl ...`, `Up to N times.`) format a translated template where they are raised, since a label that fades has nothing to heal. Text that becomes map data stays untranslated on purpose: a template's `Damage: 5 | Range: 2 | Type: pierce` description and a `Custom Preset 3` name are written into `metadata.json` and read back as content, so translating them would write one author's language into every player's map. |
| The object manager's type filter works in every language | `InstanceManagerDialog` resolves the selected category from the option's own id against a canonical key list, not from the item text it displays. The displayed text is translated, so the previous comparison against the English key matched nothing as soon as the language was not English: a German editor's `Einheiten` filter showed an empty tree while `All Types` worked, and every category heading read `Units (12)`. |
| The authoring schemas are one vocabulary | `metadata`, `terrain` and `manifest` schemas are byte-identical between `Realm.MapEditorExtension` and the copy in `MapTemplate/.vscode`. |
| A re-publish only updates an item its creator owns | `MapPublishController` finds the item to update with `SteamWorkshopService.FindOwnedByMapNameAsync`, which filters on the owner Steam reports, because Steam reserves no map name and no update can be aimed at somebody else's item. A same-name item owned by another account is named in the console and the map is published as a new item. |
| A lobby advertises the host's own item for the host's own map | `ResolveHostedMapTrustAsync` asks `FindOwnedByMapNameAsync` first and falls back to the newest same-name item only when the host owns none, which is the case for a map somebody else published. The advertised id is what a joiner downloads, so a stranger's newer same-name item would otherwise be downloaded and then refused by the hash check. |
| A prepared map survives its own preparation | `MapSessionCopyService.TryPrepare` builds the copy beside the retained folder, removes the old one, moves the build into place and only then sweeps the session root - so the sweep cannot reach the folder it was about to install. It was the other way round, which deleted the copy and refused every map outside the session root. |
| The retired platform has no live path back | No tracked file outside `prompts/` imports or constructs the admin server, the lobby registry, the seeder network, `ServersConfig` or `servers.json`; `HttpClient` survives only in the announcements sign-up and the two editor installers that actually download a tool; no source states a `res://` icon path that does not exist. |
| The menu leads only where a scene exists | Every `GameScreen` member has a scene that exists; the creator directory, whose scene was deleted, is gone from the enum, the transition, the menu and its art. |

## Unproven

Grouped by what a failure would look like, worst first.

**A match that cannot be started or joined.** Hosting publishes a Steam lobby; joining resolves it back to an authority;
the join code is compared on the authority and never travels in Steam metadata; the host's own seat arrives over loopback
while every other seat arrives over Steam. This is the path most of the migration rewrote and the one a mistake would
make unusable, and it has never run.

**A peer that takes another player's seat.** The authority now believes Steam's assertion over the hello's number, but that path
has only been compiled, never attacked: it needs a doctored client that states somebody else's SteamID64 to prove the seat,
the slot reclaim and the kick list all ignore the claim.

**A peer that cannot be seen or invited.** Steam friends invites from any seat, `+connect_lobby` landing a closed client
in the right lobby, and rich presence naming the state and clearing on exit.

**A map that cannot be published, updated or played.** The first publish creating an item, later publishes updating it in
place, the licence acceptance surviving a version bump, the upload being the canonical copy only, a subscriber playing
the downloaded map, and the one retained session copy being replaced rather than drifted from.

**A screen that degrades badly without Steam.** Single player, the editor, local map play and `.rmap` import must all
work with no Steam client, and every multiplayer surface must be absent with a translated reason rather than clickable
and broken. This is where "we hid the button" and "we hid the feature" differ, and only running it tells them apart.

**A match that plays but reads wrong.** Presentation delay, snapshot interpolation, the confirmed-order lead, the
presentation frames a map script addresses, and the HUD's own values are all statically plausible and have not been
watched.

**A translated screen showing English.** Coverage is no longer the question: all ten catalogs carry the keys this branch
added, and `Realm.TranslationTool -- verify` reports 0 missing, 0 empty, 0 placeholder and 0 BBCode mismatches
for every one of them. What has not happened is a native speaker reading them. They were written against the vocabulary
each catalog already used - lobby, map, host, publish and invite keep the words the game already showed - but wording is
not something a compiler checks, so the first pass in each language is a review rather than a proof. [The translation
kit](translation-kit/README.md) is how that review is handed over and how the next string gets added. The other half of
this item is a string rendered through a path that does not register its key, which shows English even where a
translation exists, or a comparison made against text a translator changed, which is how the object manager's type
filter silently stopped matching anything outside English. [Test plan](steam-test-plan.md) group K is that half: one
language played through the menu, the lobby, a match and the editor, with the four failure shapes it is looking for
named, because none of them is visible from a catalog audit.

## The order to check it in

1. **The menu and the local group** in the test plan. Cheapest signal, and it exercises the changes this branch made to
   the menu, the settings and the diagnostics export.
2. **One language other than English** for the same group, to catch a pruned or unregistered key while it is still
   obvious which screen it came from.
3. **Host and join on two accounts on one network.** This is the step that proves the lobby path exists at all.
4. **The same on two networks.** This is where a transport assumption shows up; on one network it would hide.
5. **Workshop publish, subscribe and update**, then the retained session copy check.
6. **No Steam at all**, then Steam in offline mode, which are different states and fail differently.
7. **The failure surfaces** last: a trapping map script, a killed authority, an unacknowledged crash report.

## What to expect to be wrong

The migrations most likely to have a defect that only play reveals, in the order I would look:

- **The collapsed lobby-publish path** — two entry points became one, and the caller that used the "on failure" name now
  calls the plain one. If a publish failure is reported anywhere but the host's progress card, it is here.
- **The trusted mark** — trust is computed from Steam's vote counts and how recently the item changed; the curated list
  that would vouch for a map too new to be voted on is read from `config/featured_workshop_items.json`, which no build
  ships, and membership of a developer-owned Verified Workshop collection is not read at all yet. So on a fresh catalog
  every item is "too new" by design and the library reads uniformly untrusted. That is the threshold working, not a bug
  in the mark - but it is also the gap to close before the official catalog opens, and it is listed as a pivot rather
  than repaired here because no collection id exists to read.
- **The creator panel in map details** — it queries Steam for the creator's other items for this AppID only. A creator
  who publishes for another game should show only their Realm maps, and a private profile should read as neutral rather
  than broken.

## Deliberately left as is

- `ServerArguments.AdminToken` keeps a name from the deleted admin portal: it is the token the host's own screen mints and
  proves back over loopback, which is how the authority recognises the seat that started the match.
- The manifest's `GreenlitReferences` field keeps its name because renaming a manifest field is a map-format change. Every
  string a player reads says "shared assets".
- The global creator directory's absence is the design: it is out of scope, and the creator surface lives in the map
  details panel, the credits and the editor.
