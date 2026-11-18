# 09B — Three-Tier Map Authorship Verification

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 07, 08, 09.
**Related:** 09A (the session copy this verification runs beside), 13 (trust marks), 16 (persona display), 17 (mirrors), 18 (attribution metadata).

## User story

As a player who received a `.rmap` from somewhere other than Steam Workshop, I want to know whether the map I am about to play is the one its author published, whether it is a newer version of the copy I already have, and — when Steam can answer — which Steam account confirmed it, so a casually re-uploaded or quietly re-balanced copy is visible instead of indistinguishable from the original.

As a map author, I want one signature over my whole package that anyone can check offline, so someone cannot take my map, change a unit cost or add a cheat and pass it off under my name, and so a legitimate update of mine is not mistaken for an impostor's.

As a curator or community registry, I want a stable package hash and a key fingerprint printed by the game's own tool, so I can key a trust database on them without reimplementing the format.

## Context

`.rmap` export is a first-class sharing channel — a website, Discord, a regional community that cannot reach Workshop — so a map reaches players from sources Steam does not vouch for. That is the entire threat model: a modified copy of a *known* map, distributed as if it were that map, damaging the author's reputation and the players' experience. Modifying a map is not the problem; the map format is open and the editor is a feature.

The repository already carries a local Ed25519 authorship key (`AuthorSignatureHelper`, `AuthorshipKeyHelper`, `Realm.Tools.Cli/KeygenCommand`). It is close to the right tool and wrong in three ways:

1. **It signs the wrong thing.** `LobbyRoom.MapAuthorship.VerifyAuthorSignatureAsync` checks an Ed25519 signature over `BLAKE3(SaveUnsignedMetadataToJson(metadata)) + ".json"`. That covers `metadata.json` only, so the script (`bin/script/<Assembly>.wasm`), shaders and every asset binary are outside it. Most templating lives in metadata, so the common tamper is caught; a swap of the WASM beside a valid metadata file is not.
2. **It proves a key, and is presented as an identity.** The signature verifies against the `author_key` stored in the same document, so it is valid for anyone's key over anything. That is drift detection, not authorship. The UI calls it "authorship", "ownership" and "your maps", which is a stronger claim than the check makes.
3. **It forgets everything.** Nothing notices that v1.1 of a map states a different key than the v1.0 the player already has, which is the pattern that actually damages a reputation: the victim is a player who already owns the original and accepts an update from a hostile channel.

What is missing is not a service. It is a binding and a memory. This task builds both, in three tiers, plus the one Steam attestation that costs nothing.

## The three tiers

| Tier | What is true | What it needs |
|------|--------------|---------------|
| 1. Signed package | Every byte of the package is what the key signed | Ed25519 key, one hash over the package |
| 2. Continuity | This map's key is the same key this machine saw last time | A small local record |
| 3. Steam-confirmed | The Steam account that owns the Workshop item states this key, and the package names that account | `ISteamUGC` item details |

Each tier is independent and additive. Tier 2 works with no network and no Workshop item; Tier 3 is a bonus that may never fire in a region where Steam's UGC backend is unreachable, and its absence must never be reported as a failure.

## Requirements

1. **Keep the key; stop calling it ownership.** The authorship key stays. Remove every claim that losing or replacing it changes "ownership" of a map, because it never did: the Workshop item belongs to a Steam account (task 07), and only the item's owner or Valve can delete it. The publish panel states what the key actually does — it signs the package so a copy can be checked against it — and says that publishing, updating, installing and joining never require one.

2. **Sign the package, not only the metadata.** Add a signed attestation to the package that names the hash of the whole package. Use the folder-walk hash the codebase already computes (`MapSessionCopyService.ComputePackageSignature`), computed over the package's own file set with `metadata.json` replaced by its unsigned serialization (`MapFileService.SaveUnsignedMetadataToJson`), so the signature can cover the file that carries it without being circular. The lobby keeps the manifest-derived hash it advertises today (`LobbyManager.GetMapMetadataInfo`), and so does the compatibility check (`ReadStatedPackageHash`); this task changes neither and adds no third hash. The folder-walk hash is the signed one, and because `manifest.json` is one of the files the walk covers, a verified package is also the manifest those two values were read from.

3. **One attestation text, pinned by a version.** The signed bytes are exactly this UTF-8 text, with `\n` separators and no trailing newline:

    ```
    realm-map/1
    package:<lowercase hex package hash>
    metadata:<lowercase hex blake3 of the unsigned metadata JSON>
    files:<decimal file count>
    ```

    `signature_version` in the metadata states `1`. Verification reads the version and checks only what that version defines; a future change to the file set or the text is version 2 and both versions stay readable. Do not silently change what version 1 covers — that would invalidate every signature ever made, which is the one failure this format cannot repair.

    The **key fingerprint** used everywhere in this task is the first 16 lowercase hex characters of SHA-256 over the raw 32-byte Ed25519 public key that `author_key` decodes to. It is one short string that can be printed, compared and published, and it is short enough for a Steam metadata block, a `.rmap` name or a forum post.

4. **The package file set is the exported set, and it is stated.** The signed file set is defined as the files an `.rmap` export carries: sorted ordinally, walk order normalised, no directory entries, forward-slash paths, each file's length and bytes in that order. Skipped, because a compile rewrites them or they are authoring state rather than package content: `.git/`, `.godot/`, `obj/`, `bin/.build-*`, `lib/`, the `.csproj`, `*.debug.wasm`, `metadata.partial.json`, and any history or symbol file. `bin/script/<Assembly>.wasm` (the release artifact) **is** included. A folder and the same map extracted from its own `.rmap` must produce the same hash; if they do not, the skip list is wrong and the fix is the list, not a tolerance.

5. **Metadata fields, schema-generated, in the file that already exists.** Add `signature_version` (int), `signed_package_hash` (string), `author_steam_id` (string, the SteamID64 of the author as the publishing account), and keep `author_key` and `signature`. Do not add a separate `.sig` file or a new entry to the archive: the signature stays in `metadata.json`, so a `.rmap` is still one package with the same shape it has today. All are `SchemaDerived("publish")` and dropped by every non-publish save, exactly as `signature` is today — a save changes the bytes a signature covers, so a saved-but-stale signature is worse than none. A sequential `MapUpgradeService` migration adds the fields for older packages; there is no fallback reader.

6. **Sign every publish, and make an unsigned one a stated choice.** The publish flow signs the staged package after it is built and before it is uploaded, writing the signature fields into the staged `metadata.json` last. If no key can be used, the author may publish unsigned after one message that says the copy cannot be checked against them; the item still publishes. Do not block publishing on a key, and do not sign with a key the author did not choose.

7. **The name shown is the Steam persona, never the metadata name.** When a package states `author_steam_id`, the author row, the credits and the Workshop description resolve the display name and avatar from Steam for that SteamID64. `MapProperties.Author` is shown only when no SteamID is stated (a local map, a package shared before the field existed). Fix the editor's publish-name resolution so it does not prefer the key file's cached `UserName` over the live persona (`MapEditorHUD.GetPublishAuthorName` currently does), and do not copy a persona into the package as the author name — a renamed player would keep publishing the old name.

8. **Tier 2: remember the key this machine has seen.** Keep a small local record, `user://authorship_known_maps.json`, keyed by Workshop item ID when the package states one and by normalised map name otherwise. Each entry holds the key fingerprint, the persona as last seen, the package hash, and first/last seen timestamps. On opening, registering or joining a map, compare: same fingerprint is `SignedSameKey`; a different fingerprint for the same identity is `KeyChanged`. Name-keyed entries only compare with other name-keyed entries, so two unrelated local maps that share a title do not produce a false alarm. Do not key continuity on the package hash: a new version of a map is supposed to change it.

9. **A changed key is a decision, not an error.** `KeyChanged` shows: "This version was signed by a different key than the copy you have. Either the creator replaced their key, or this is not their update." Offer "Accept this key" (which updates the record) and "Keep my copy". Never auto-accept, never block the map from loading, and never describe it as a virus or a hack — the map script is sandboxed either way and the claim is about authorship.

10. **Tier 3: one Steam call, one owner match.** The publisher writes the key fingerprint into the item's existing `WorkshopMapMetadata` block (`SetItemMetadata`, the same submit that uploads the content, so the fingerprint always matches the content beside it) — not into public tags, which would clutter the Workshop page, are length-limited and would accumulate across a republish. That type already carries `AuthorSteamId`, `ContentHash` and `Version`; add only `AuthorKeyFingerprint`, under its existing `MaximumMetadataBytes` budget. Verification of a package that states `author_steam_id` and a Workshop item ID is then: `GetItemAsync` returns the item; `m_ulSteamIDOwner` equals the package's `author_steam_id` and the item's own `AuthorSteamId`; the item's `consumer_app_id` is the running AppID; the item is not `m_bBanned`; and the item's `AuthorKeyFingerprint` equals the fingerprint of the package's `author_key`. All four hold means `SteamConfirmed`. The item's `ContentHash` may disagree with the locally installed package without breaking the tier, because Steam serves whatever it currently holds and a stale local copy is a join-time hash mismatch (task 09), not an authorship failure; report the two, do not conflate them.

    The two failures cancel out and that is why this works without downloading the item: an attacker who keeps the original key cannot sign modified bytes (Tier 1 fails), and an attacker who signs with their own key does not match the fingerprint the owner published (Tier 3 fails). An attacker who copies the item ID and SteamID64 but strips the signature is `Unsigned`, and requirement 11 is what stops that from reading as the victim.

11. **An unconfirmed SteamID claim is a claim, and looks like one.** A package can state any SteamID64. When `author_steam_id` is stated but the item is not `SteamConfirmed` (no item ID, or the checks did not hold), the author row shows the persona text with the line "Name claimed by the map, not confirmed by Steam" and suppresses the avatar, the profile action and the Workshop action. Only a confirmed package gets the "Steam-confirmed" mark with the avatar and the links. A package with no SteamID at all shows its metadata author with no Steam mark.

12. **Never turn "could not ask" into a negative claim.** Steam offline, the item not returned, a timeout and a rate limit are `SteamUnknown`, not `Unsigned` and not `Wrong`. Render them as "Signed; Steam could not confirm", keep the Tier 1 and Tier 2 results, and retry on demand rather than on a timer.

13. **Verify once per package, not per frame.** The package walk is the expensive part. Verify when a map is registered (the session-copy step already computes the same hash), when the details panel opens, and when the signature fields or package hash changed; cache the result in memory and in the session copy's record. Never run a full hash from a card render or a lobby list tick, and never on the main thread while a match is running.

14. **The authority logs, it does not police.** The authority computes and logs the state of the map it was given, because a joiner's copy proves nothing about the host's. Do not refuse a match, refuse a seat or alter gameplay based on any tier: the signature is not a security boundary, the WASM sandbox is, and a hard gate would make a legitimate re-key or an older map unplayable.

15. **Both surfaces, one vocabulary.** Show the state in the map details panel and in the credits, and in the lobby's map row for the map the host advertised. The words are fixed: `Signed`, `Unsigned`, `Modified after signing`, `Signed by the same key as your copy`, `Signed by a different key than your copy`, `Steam-confirmed`. The word "verified" stays reserved for membership in the developer's Verified Workshop collection (tasks 08 and 13); a Tier 1 or 2 result is never called verified.

16. **A package is signed by whoever publishes it.** A Realm mirror (task 17) and a reupload by a different account are signed with the publisher's own key, state `MirroredFrom` (original item ID and author SteamID64) inside the package, and never carry the original's key or fingerprint. The client must therefore treat a mirror as `Signed` by the mirror account, not as the original author, and must show the original creator from `MirroredFrom` as a credited author rather than as the signer.

17. **Developer and curator tools.** Extend `Realm.Tools.Cli` with `verify <map folder>` (prints each tier's result, the package hash, the fingerprint and any `MirroredFrom`) and `fingerprint <key file>` (prints the fingerprint the Workshop metadata block should carry). These are the commands a community registry, a curator or a rights reviewer runs; printing them from the same code path the client uses is what keeps a third-party database honest. No service is added.

18. **Localization and schema.** Every new string is a `TranslationServer.Translate("literal")` call site; formatted lines are registered with `LocalizedText` at the point they draw, per the project's localization rules. Schema documentation is generated from the attributes above rather than hand-written, and `MapTemplate/lib`'s API docs are not affected because nothing here touches `IGameAPI`.

## UX

- Map details shows one author row and one integrity line. A confirmed map reads "Steam-confirmed author: **persona**" with the avatar and the Steam links. A signed map with no Steam claim reads "Signed by **persona from the package**" plus the fingerprint's first eight characters in the advanced field.
- The integrity line is one sentence, not a table: "Package matches the author's signature" / "This copy was changed after it was signed" / "No signature — this copy cannot be checked against an author" / "Steam could not confirm this copy right now".
- `KeyChanged` is the only state that interrupts, and it interrupts once, on the map being opened or the lobby being joined, with the two actions from requirement 9. It is not a modal on the map library list.
- The publish dialog shows the fingerprint the author's key will publish, with a copy action, so the author can post it where their community can see it. It explains in one line that anyone holding the `.rmap` can check it offline.
- The advanced details field shows package hash, fingerprint, signature version and signed date, each copyable, so a curator can compare them with a registry entry by eye.

## Out of scope

- Any Realm-hosted key directory, revocation list, registry or reputation service. A community may build one; the game only prints the values it would key on.
- Map protection, obfuscation or any attempt to stop a map being edited. Editing is a feature; being visible is the defence.
- Requiring a signature to publish, install, join, host or play.
- Downloading a Workshop item to re-hash its bytes against a `.rmap`. The item's owner plus the package's own signature already bind the two; a byte-level comparison is a possible later addition, not this task.
- Attribution of imported assets (task 18), creator panels (task 16) and the trust filter's vote thresholds (task 13), which gain a mark from this task but keep their own rules.
- Changing `IGameAPI`, the ECS, the snapshot protocol or the engine generation contract.

## Acceptance criteria

- A published map carries `signature_version`, `signed_package_hash`, `author_key`, `signature` and `author_steam_id`, and the signature verifies over the attestation text in requirement 3.
- Changing any packaged byte — a unit cost in `metadata.json`, a byte in the release WASM, an asset — makes an existing signature `Modified after signing`.
- A folder and the `.rmap` extracted from it produce the same package hash, and `realm verify` agrees with the client.
- A second copy of the same map signed with a different key reports `KeyChanged` on the machine that holds the first, and "Accept this key" clears it without touching any other map.
- A first-seen signed map reports `SignedSameKey` after it is recorded, and a new version signed by the same key stays `SignedSameKey`.
- A package whose `author_steam_id` matches the Workshop item owner and whose fingerprint matches the item's metadata is `SteamConfirmed`, and shows the Steam persona and avatar.
- A package claiming a SteamID64 whose item does not match, or whose fingerprint does not match, is not `SteamConfirmed`, shows "Name claimed by the map, not confirmed by Steam", and hides the avatar and Steam links.
- With Steam offline, a confirmed map degrades to `SteamUnknown` and keeps its Tier 1 and Tier 2 result; nothing is reported as unsigned or wrong.
- No publish, install, join or host path is blocked by any tier, and a local map with no Workshop item is fully playable and verifiable.
- A mirrored package is `Signed` by the mirror account and shows the original creator from `MirroredFrom`, never the original's key.
- The publish dialog no longer claims the key confers ownership, and `GetPublishAuthorName` returns the live Steam persona.
- No new network call is made for a map with no Workshop item, and no new service, endpoint or secret exists.
- The solution builds with no reference to a removed `asset_author` endpoint, and the new metadata fields round-trip through save, export and publish.

## Pitfalls

- Do not sign the metadata a second time and call the old one "v1 kept for compatibility". The old signature is over metadata only; keep reading it if a package has no `signature_version` so an existing signed map still shows something, but present that as metadata-integrity only, not as a package signature.
- Do not include the signature fields in the hashed bytes. The package hash must be computed with `metadata.json` in its unsigned form, or every verification fails by construction.
- Do not hash the folder as the filesystem lists it. Sort ordinally, normalise separators, and exclude the skip list, or Windows and Linux disagree on the same map.
- Do not follow symlinks out of the map folder, and do not hash a file that changed while it was read: take the length and modification time before and after, and re-read once rather than reporting a false mismatch.
- Do not treat a missing `author_steam_id` as an error. Every local and older map has none, and requirement 11's neutral presentation is the normal case.
- Do not write the fingerprint into a public Workshop tag. The item metadata block already exists, is typed, is updatable without re-uploading content, and does not accumulate stale values across republishes.
- Do not assume Steam's `GetItemAsync` is free. It is a UGC query; cache it per item for the session, as task 08 already requires, and never call it from a card render.
- Do not let a failed Steam query clear the recorded key of a map. Continuity is local and must survive being offline for a year.
- Do not key the continuity record on the map's folder path. Maps move, and a moved map is not a new author.
- Do not call an unsigned map "unsafe". The sandbox is the safety boundary; this is about authorship.
- Do not describe a mirror as a forgery. It is a licensed copy under the UGC terms, signed by whoever republished it, and the original creator's credit comes from `MirroredFrom`.
- Do not put the verification on the simulation tick, on a capture, or on any match-critical path. It is a package read that happens when a map is registered or a panel opens.
- Do not add a "sign my map" button to the player's flow. Signing happens at publish, with the author's own key; a player re-signing a map they did not author is exactly the behaviour the tiers exist to make visible.
