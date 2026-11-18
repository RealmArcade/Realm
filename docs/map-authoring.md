# Map authoring

A map is a folder the editor owns and a Workshop item Steam owns. The folder is the source of truth while you work; the
item is the map's identity once you publish.

## The folder

```
metadata.json          everything the map says about itself and its templates
terrain.json           the board's terrain
manifest.json          written when a package is built: files, hashes, attribution
references.json        optional: the maps this map takes content from
bin/script/<assembly>.wasm         the optimized build every match runs
bin/script/<assembly>.debug.wasm   the build a playtest attaches a debugger to
lib/                   the map API this map compiles against
```

`metadata.json` carries `GameBuildNumber` (the migrations the map still needs) and `EngineGeneration` (the engine
generation it was authored for). Neither is a playability gate for the player: the game reads the generation, upgrades
older data on a private copy, and refuses only a map made by a *newer* engine, with a message asking the player to update.

## The Workshop item is the identity

- The published file ID is the canonical key. A title is a label: two items may share one, and renaming or moving the
  folder never breaks who can play the map.
- The item's owner is the author of record. Steam's item is the attribution and the access control; nothing requires a
  signature to publish, install or join.
- The manifest hash is what a match actually plays. Two machines with different hashes are holding different builds of the
  map, and the host is the side that can fix it: cancel the lobby, let Steam update the map, open the lobby again.
- Steam's `time_updated` or revision change number is a cache hint, not the map's version.

## Publishing

Publishing goes to the Workshop through the editor. The publish dialog compiles the script for release, builds the
package, and uploads it. It states, once, that a map must not carry links to external payment systems, and links Valve's
Online Conduct and Workshop Legal Agreement rather than quoting them.

Money and support belong on the creator's own Discord, which is the one creator link the game shows beyond Steam: an
optional `DiscordServerInviteLink` in the map's metadata, validated to an `https` invite on a Discord invite host. The
game never shows a price, a purchase control or a donation link for a map or a creator.

## Reusing another map's work

Reuse is vendored when the map is authored, never resolved at load time. A map folder keeps `references.json`; on every
save the referenced map's files are copied into the folder (read-only), its `metadata.json` is deep-merged, and its C# is
rewritten into `composed/<Map>/`. Every downstream step — testing, export, publish, download, play — reads one ordinary
self-contained folder.

That means a map declares no dependency on another map, nothing resolves at install or join, and an unresolvable
reference is not a broken install. Because composition carries another creator's units, art and scripts, it is credited:
the reference records the source map's author SteamID and Workshop item when the source states them, the manifest lists it
in `Attribution_SourceMap`, and the credits panel names it beside imported files. An export containing imported work warns
and names the sources; it does not block, because the license permits it and the author may have permission.

## Engine updates

Maps keep playing across engine updates because of two rules:

1. **The map's data is migratable.** `MapUpgradeService` moves a map forward one release at a time. The game runs it when a
   map is opened for a lobby or a match, on a private copy under the user data directory, never on your folder and never on
   Steam's. Exactly one copy is retained — the most recently opened — and it is reused only while its key, recomputed from
   the installed map, still matches.
2. **The map-script surface is additive.** A host import is never removed, never retyped and never given a new meaning, and
   a replicated component id is never reused. `MapApiSurface.baseline.json` is compared against the generated surface at
   build time (`REALMAPI001`), and `ReplicationComponents.baseline.json` against the replicated table in the contract test.
   A break is only allowed with an `EngineGeneration` bump in the same change.

If a map's script ever needs an import that no longer exists, it says the map needs an update from its author and links its
Workshop page. Nothing is shimmed at runtime.

## Replays

A replay records a simulation, not a map. It is supported while its recorded format stays readable; an unreadable replay
names the version it was recorded with and opens nothing else. There is no migration of old replays and no promise that
one opens here.

## Sharing a map outside Steam

A `.rmap` package is how you hand a map to somebody directly. It carries the same folder, the compiled script and the
attribution. Importing one installs the map locally; a map that was never published is marked as a local map, and a lobby
that hosts it says whether the host can send it to the players who join.
