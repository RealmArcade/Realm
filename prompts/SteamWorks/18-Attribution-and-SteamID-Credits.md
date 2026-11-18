# 18 — Attribute Imported Assets by SteamID

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 01, 09 and 09B.
**Related:** 07 (credits in the Workshop description) and 13 (credits panel).

## User story

As a map author, I want the editor to record which creator each imported asset or script came from, keyed by SteamID, so credits are correct and I can see when an export contains someone else's work.

As a player, I want the map's credits to name the creators whose assets appear in it.

As the developer, I want to remove the public-key *asset attribution* that existed for anonymous server nodes, without losing the provenance record that keeps imported assets inside the Realm ecosystem, and without deleting the map's own package signature that a `.rmap` shared outside Steam depends on (task 09B).

## Context

The editor currently tracks imported assets with public-key signatures because the old platform had anonymous, untrusted server nodes. Steam gives a stable SteamID64, so that half of the key infrastructure is no longer needed. The signature there is provenance, not access control. The UGC license (task 06A) allows a creator to export only assets they authored personally, so the editor should warn when an export contains imported assets.

Two things share `AuthorSignatureHelper` today and they part company here. Asset attribution moved between servers and accounts, so it becomes SteamID-keyed. The map's own signature stays and is extended by task 09B into an attestation over the whole package, because the copies that leave Steam as `.rmap` files need something a player can check offline. This task removes the attribution record; it does not remove key generation, signing, verification or the key files.

## Requirements

1. Replace the public-key identity in **attribution records** with the creator's SteamID64. Remove what existed only for the old attribution model: the `asset_author` lookups, the import-header signature path (`AuthorSignatureHelper.MergeMetadataHeaders`) and the fields that carried a public key beside an imported asset. Keep content hashes used for integrity. Do **not** remove `AuthorSignatureHelper.GenerateKeyPair`/`SignMessage`/`VerifySignature`, `AuthorshipKeyHelper`, the key files or `Realm.Tools.Cli`'s key commands: task 09B keeps them and signs the package with them, and its fingerprint is what a Workshop item publishes.
2. Record an attribution tag when an asset, script or template is imported from another map: source map Workshop ID (optional), source author SteamID64 (optional), source asset ID or hash, imported time, license note and a cached display name. Both identifiers are genuinely optional, because a map received as a `.rmap` over Discord carries no Workshop item and may carry no SteamID at all; in that case the cached display name is the whole record.
3. Carry the tags in the map package. Use the existing manifest and schema generator; do not hand-edit generated schemas. If the schema changes, add a sequential `MapUpgradeService` migration and no fallback reader.
4. Show the tags in the editor's asset inspector and in a map-level "Credits" panel. The panel groups by source map and lists the author, the Workshop link if there is one, and the imported assets.
5. Show a "Credits" panel in the game's map details and in-match, with each source creator's Steam persona and a link to their Steam profile and source Workshop item. Do not show donation links there. The creator's Steam page is the only place for anything Steam permits, and a map's optional Discord invite belongs to that map's details rather than to the credits list (task 16).
6. Warn, do not block, when an export contains imported assets. The warning names the sources and explains that the UGC license allows exporting only assets the author created personally. The author may have permission; the editor is not the judge.
7. Publishing to Workshop includes the attribution metadata. The Workshop description can include a short credits line generated from the tags.
8. A local map with no Workshop ID records whatever identity its source package carried: a SteamID when it has one, and a name alone when it does not. Name-only is a normal, expected state, not a damaged record: show the name with no link, and do not substitute a guessed SteamID or a placeholder identity.
9. Nothing in attribution is a signature requirement: publish, install, join and host never depend on one, and a map runs without it. The map's own package signature (task 09B) is a separate record, is never consulted by these paths, and is not something this task touches.
10. Keep the existing `IGameAPI` and map script ABI unchanged. This is editor and package metadata, not a simulation feature.
11. **Credits cover composed maps, not only imported files.** A map built from another map's references (`references.json`, task 09) carries that map's assets, merged metadata and a copy of its C#, and today records no credit at all: content carried by composition has no `Attribution_ImportedFromMap`, and composition writes no `Attribution_SourceMap`, so a shipped map can contain another creator's units, art and scripts and list nobody. The terrain-and-ruleset split is this project's own shipped example of that pattern. Treat each reference as a credit entry beside the imported assets - source map name, its author's SteamID64 and Workshop item when the referenced package states them, the name alone when it does not - and add it to the manifest's `Attribution_SourceMap` so the existing credits surfaces and the generated Workshop credits line pick it up with no second vocabulary. A referenced map is not a single asset with a hash, so do not force it into that shape.

## UX

- The asset inspector shows a "Source" line with the creator's persona, the source map and a link to the Workshop item when there is one.
- The editor's map panel has a "Credits" view that groups imported assets by source map.
- The in-game map details and the in-game credits panel show the same grouping, with the original map title and author.
- An export that contains imported assets shows a warning that names the sources and explains the license rule. It offers "Review credits" and "Export anyway", not a hard block.
- A missing or unreadable attribution record shows "Unknown source", not an error.

## Out of scope

- DRM or technical enforcement of the license. Attribution can be stripped; the license and moderation are the remedy.
- Changing the map script ABI or the ECS.
- The map's own package signature, its fingerprint and its continuity record (task 09B).
- The Steam creator panel (task 16) and the Workshop download path (task 08).
- A public attribution database. The tags live in the package.

## Acceptance criteria

- Importing an asset records the source author's SteamID64 and source map when the source package states them, and records the cached display name alone when it does not.
- The editor and the in-game credits panel show the tags grouped by source map, with links to each source creator's Steam profile and source Workshop item.
- The package round-trips the tags through save, export and publish.
- A map that composes another map shows that map's author in the credits panel and lists it in the manifest's `Attribution_SourceMap`, alongside any individually imported assets.
- The old public-key attribution fields are gone from the new schema, and the `MapUpgradeService` migration builds.
- Key generation, signing, verification and the key files still exist and are reachable from the publish flow, because the package signature (task 09B) uses them.
- No publish, install or join path requires a signature.
- An export with imported assets warns and names the sources; it does not block.
- A missing attribution record shows "Unknown source" and the map still loads.

## Pitfalls

- SteamID is not a display name. Cache the persona at import time, but prefer the live persona when it is available.
- Do not use the attribution tag as a license check. It is a record, not an enforcement mechanism.
- Do not block a map from loading because a tag is missing. Show "Unknown source" and let moderation handle it.
- Do not confuse "no SteamID" with "unknown source". A map shared as a `.rmap` that was never published has a real author name and no SteamID, and its credits should read normally.
- Do not put Godot nodes or scene references in the ECS. This is editor and package metadata.
- Do not keep the old public-key *attribution* fields "for compatibility". Per the repository policy, old maps are upgraded by `MapUpgradeService`; there is no fallback reader.
- Do not delete the map's package signature while removing asset attribution. They live in the same helper and the same key files, and deleting one takes the other with it; task 09B depends on these helpers surviving.
- Do not require a signature for a Workshop map. Steam's item ownership is the attribution and the access control.
- Do not treat an imported asset's SteamID64 as a signature or a licence check. It is a record of where the asset came from.
- Do not treat composition as an attribution shortcut. Composition brings in another creator's whole contribution, so it needs at least the credit an imported file gets; a map that composes a ruleset and lists no author is the same omission as an untagged imported asset, at a larger scale.
- Do not treat the Steam profile as the attribution record. The package tag is the provenance; the Steam page is where the creator presents themselves.
