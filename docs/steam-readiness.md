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
| The solution compiles | `dotnet build Realm.slnx -c Debug`: 0 errors. The 187 warnings are pre-existing, almost all of them nullability warnings in the editor dialogs. |
| The map API surface has not changed | `Realm.WasmBindgen`'s `MapApiSurface.baseline.json` guard runs as part of the build and fails it on a changed surface, so a green build is a passing check. |
| The replicated wire table is intact | Reproduced `ReplicationContractTests` from source: all 67 components in `ReplicationComponents.baseline.json` still resolve to the id they ship under through `ComponentTypeRegistry`'s ordered canonical list, no id is reused, and the 67 codecs `ReplicatedComponents` registers are exactly those components. No replicated type sits outside the canonical list, so no id depends on registration order. |
| The component ids map scripts read are stable | `ComponentTypeRegistry.VerifySharedComponentIds` compares the canonical order with `ComponentIds` at startup and throws, naming the drifted type, so a reordering bug cannot boot. |
| The client cannot call the authority in process | `Realm.Client.csproj` carries only the `ReferenceOutputAssembly="false"` ordering reference; no `using Realm.ListenServer` exists in client source. |
| Nothing references a deleted page or file | Every `GetNode`/`GetNodeOrNull` literal resolves against its scene apart from fallback chains and nodes inside instanced sub-scenes; every `res://` literal resolves against the Godot project that owns the referencing file apart from the optional `config/featured_workshop_items.json`, whose absence the trust policy treats as "features nothing"; no orphaned `.cs.uid` remains. |
| A seat's SteamID is the identity Steam asserted | `ListenServerHost.ResolveSeatSteamId` takes `_transport.TryGetPeerSteamId` first and uses the hello's number only when the transport asserts none (the host's own screen on loopback, and a match started without a lobby). It is the identity used for the lobby seat, the slot reclaim, the kick list and `SetPeerSteamId`. |
| The translation catalogs match their call sites | `Realm.TranslationTool -- prune` reports nothing to prune for all ten translated catalogs, and each catalog's key set is a subset of `en.json`. |
| The authoring schemas are one vocabulary | `metadata`, `terrain` and `manifest` schemas are byte-identical between `Realm.MapEditorExtension` and the copy in `MapTemplate/.vscode`. |
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

**A translated screen showing English.** The ten catalogs were pruned to `en.json`'s key set. If a string is rendered
through a path that does not register its key, it shows English in a translated session rather than failing, which is why
the test plan's local group should be run in one non-English language as well as English.

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
- **The trusted mark** — the featured-item list is gone, so trust comes from Steam votes, item age and a Workshop
  collection. A fresh item is "too new" by design; if the library looks uniformly untrusted on a first run, that is the
  threshold, not a bug in the mark.
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
