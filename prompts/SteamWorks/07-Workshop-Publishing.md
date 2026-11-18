# 07 — Publish Maps to Steam Workshop from the Editor

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 01, 06A.
**Unblocks:** 08, 09, 11, 13.

## User story

As a map author, I want to press Publish in the editor, choose a title, description, preview image and visibility, and upload my map to Steam Workshop with a progress bar, so I never sign into a Realm account, wait for a greenlight threshold, or upload to a custom map portal.

As a map author, I also want a separate Export action that writes a complete `.rmap` I can share on a website or Discord, so publishing to Workshop is not the only way to hand someone my map.

As a player, I want the maps I see in Realm to be the maps that exist on Steam Workshop, with the same names, previews and authors.

## Context

The current publish flow is built around a Realm account, an authorship key, a custom publish handshake, a beta/greenlight phase and a map portal. It also has a "full export" option and a 15 MB per-asset limit that exist because of the custom CDN.

This task replaces the publishing backend with Steam Workshop. The editor still builds the map exactly as it does now: it compiles the Release WASM, validates the map, stages its runtime package and writes its metadata. Only the destination changes.

## Requirements

1. **Steam identity is the author.** Publishing requires Steam, not a Realm account. Remove the "sign in to publish" gate. The Workshop item owner is the author of record.
2. **Create once, update forever.** The first publish creates a Workshop item and stores its published file ID in the map's metadata. Later publishes update that item. Do not create a duplicate item on every publish. If the item no longer exists on Steam, say so and offer to create a new one.
3. **Stage a runtime package, not the workspace.** Upload the files a player needs to run the map: metadata, terrain, runtime assets and the Release WASM. Exclude the `.csproj`, `lib/` DLLs, source files, `bin/.build-*` folders, the Debug WASM, the workspace history and the author's own `.git`.
4. **Use the existing packaging logic.** The editor already knows how to build and validate a map. Reuse that pipeline and add the Workshop upload at the end. Do not write a second map packer.
5. **Title and description.** The title is the map's display name. The description includes the summary, author name, version, game build number, player count, the optional Discord server invite the map declares and any controls or credits the map already declares. Keep it readable in the Workshop page.
6. **Preview image.** Upload a preview image with the item. Keep it under Steam's preview size limit; downscale or warn rather than failing late. Prefer the map's existing thumbnail or the editor's screenshot.
7. **Tags.** Use the Workshop tags configured for the app on the Steamworks partner site. The editor chooses values from the existing tag catalogue; it does not invent a parallel tag vocabulary. If a tag the game needs is not configured for the app, report the missing configuration instead of silently dropping it.
8. **Visibility.** First publish defaults to private or unlisted so the author can check the page before exposing it. The author can choose public, friends-only, unlisted or private. There is no Realm greenlight, beta prefix or review threshold. Making an item public is the author's action; the editor must not publish publicly on their behalf.
9. **Upload progress and errors.** Show the Workshop upload progress in the publish dialog. Map Steam's result codes to readable messages: legal agreement not accepted, file too large, not enough Workshop storage, not logged on, rate limited, item not owned. Do not silently fall back to the old portal.
10. **Workshop legal agreement.** The first publish can fail because the author has not accepted Steam's Workshop Legal Agreement. Detect that case and show a clear message with a button to open the agreement, then let the author retry.
11. **Only playable maps are Workshop items.** A Workshop item must be a complete map that can run a lobby. The existing "Reusable Asset Pack" flow already publishes a playable showcase/demo map; keep that shape, but do not create a non-playable asset-only item or a "sword" mod that patches another map. Shared assets travel inside the map package, not as a dependency on another map and not as a separate non-playable Workshop entry. Remove any editor path that offers an asset-only publish.
12. **Authorship keys are optional and being replaced.** The custom authorship key is no longer required to publish. Task 18 replaces the public-key provenance record with SteamID-keyed attribution. Do not add a new signature requirement; do not gate publishing, updating or joining on one.
13. **Remove the old publish backend.** No call to the Realm publish endpoint, no creator registration, no greenlight request, no beta prefix and no account ownership check. This includes the "full export" checkbox in the publish panel (the one that bypasses greenlit deduplication) and the greenlit deduplication behavior; Workshop uploads the staged package as-is. **Keep the local `.rmap` export** as a separate action: it is how authors share a map outside Steam, it carries every asset, and it is not a fallback for Workshop publishing. Do not upload the `.rmap` as the Workshop content; upload the runtime folder so the client can read it in place.
14. **The author owns the item.** The Workshop item belongs to the author's Steam account. The author can update it, make it private, or delete it at any time; Steam's `DeleteItem` does not prompt and cannot be undone. Realm cannot take ownership, transfer the item, or prevent its deletion. State this in the publish dialog in plain language, and do not promise the author that Realm controls the listing. If Realm needs a map to remain available, mirror it under the UGC license, with the author's knowledge.
15. **Moderation is after the fact, and it applies to both items.** Realm can ban an item (visible only to its owner) or mark it incompatible with the game hub; it cannot delete another user's item. A dual-published map is two items and a moderation action hits one at a time, so treat "this map" as the pair: ban or mark both, and record both IDs in the moderation note. Banning only the demo item leaves the same content waiting to appear in the official catalogue at release. Document the partner-site moderation path; do not build an in-game delete button for items the player does not own.
16. **Public publishes pass a quality checklist; private and unlisted ones do not.** The in-game publish tool gates a public upload on an objective checklist: valid Release build, valid manifest, preview image, description, at least one player start, no missing asset references, no placeholder or template title, and a total size within the limit. The dialog shows each item as pass/fail and links to the fix. Private and Unlisted publishes skip the checklist so an author can test the Workshop flow. This is product policy in the author-facing tool, not a Steam-side approval and not a security boundary: an owner of the AppID can still publish through SteamCMD, and an author can change visibility later on the Workshop page. That is acceptable because someone who uses SteamCMD has cleared the low-effort bar, and the trusted filter is the ongoing quality control. Do not build a publish-approval service.
17. **Accept the Realm UGC license at first publish.** Before the first upload, show the Realm UGC license and require an explicit acceptance. Link Steam's Workshop Legal Agreement separately. Record the acceptance locally with the license version and date so the author is not asked on every update. The license also states that publishing a Workshop item for the AppID, by any tool, is acceptance, so an item published through SteamCMD is covered; state that on the app's Workshop page as well.
18. **Use the Steamworks SDK, not SteamCMD.** The in-game publish uses `ISteamUGC` from the running Steam client. Do not embed SteamCMD in the game; it adds a login and Steam Guard dependency the player does not need. SteamCMD is used only by the developer archive tool (task 17) and CI.
19. **Publishing is unavailable in a preview build.** The artifact uploaded to the Steam beta branch is compiled with Workshop publishing disabled, both creating an item and updating one. This is a compile-time constant, not a runtime probe: a client cannot reliably tell which branch installed it, so the build that CI uploads to the branch is the build that carries the rule. The publish dialog still opens and states plainly that publishing is disabled in the preview build and that the author should switch to the public build to publish or update a Workshop item, with no override, no confirmation prompt and no hidden path. Everything else keeps working in preview: playtesting, importing, and the local `.rmap` export, so a creator can still hand a map to a tester. The reason is the compatibility contract (task 09A): a map published from a preview surface can carry a newer generation or schema than the public build understands, and the public build is what players run, so publishing has to happen on the channel players are on. This stops the accident, not a determined bypass: someone can install the public build or use SteamCMD, and that is acceptable for the same reason the SteamCMD bypass of the quality checklist is.
20. **Link the creator's Steam pages and credits.** The publish dialog offers "View Steam Profile", "Edit Steam Profile" and, after the first publish, "Edit Workshop Item" (task 16), and the generated Workshop description includes the attribution and credits line from task 18. Include the map's optional Discord server invite when it is set. Do not put donation, payment or general link-list entries in the description; the creator's Steam presence is the supported surface and the Discord invite is the only extra creator link.
21. **Publishing on the demo AppID also prepares the official Workshop.** The only AppID available before the official one is the demo, so publishes happen under it. Steam Workshop supports writing into another app's Workshop when that app grants permission: in the official AppID's Workshop Configuration there is an "App Publish Permissions" field, and adding the demo's AppID there lets a client running the demo create and update items in the official Workshop. Both AppIDs also need cloud quota enabled, because the preview image is stored in cloud and an item without quota fails.

    **Validate it before building the flow.** Publish one real item to the official Workshop from a demo client on day one, when the catalog is small and nothing depends on it. If the official AppID refuses the write, the fallback is the older design — a demo-era notice and a republish at cutover — and that is a decision for the owner, not something to design around from the start.

    **One publish action writes two items.** The demo item is what players can subscribe to today. The official item is dormant: it exists in the full game's Workshop, is owned by the same author, and nobody can see it until the official app is released. When the demo is unpublished, the demo item stops resolving and the official item simply becomes the live one, so the catalog carries over instead of being rebuilt.

    **Write the demo item first and the official item best-effort.** The publish succeeds or fails on the demo item, because that is the one players use; an official write that failed must not fail the publish, must be recorded, and must be visible to the author as a map that is not yet synced (requirement 22). Record both published file IDs, both content hashes and both `time_updated` values so the sync state can be computed without guessing. Keep the two items' visibility in step, so the release does not open with a map that was public on the demo and private on the full game. The dialog states this in one sentence with the visibility choice — that the map is published twice, once for demo players and once as a hidden listing the full game inherits — and keeps it out of the way everywhere else: no banner on every update and no blocking confirmation.

    **When the official AppID goes live**, a map that has only a demo item offers one action, "Publish to the full game", which runs the normal dialog with the title, description and preview already filled and creates the official item. Do not bulk-transfer, mirror or rewrite other creators' items, and do not keep a demo-era ID as a soft reference to a page that no longer exists.

    **Revoke the permission when the demo retires.** `App Publish Permissions` is a standing grant: any client that can initialise under the demo AppID can create and update items in the official Workshop for as long as it is configured. Remove the demo's AppID from the official AppID's permission field when the demo is unpublished, and make a demo build refuse to publish once the official AppID is live, so a player running an old demo build cannot push a stale map over a live listing.
22. **A silent second write is a catalog that is empty at release, so make the sync state visible.** The editor keeps a short migration list of maps whose official item is missing or behind their demo item, with one action, "Sync to full game", that republishes to the official Workshop only. It is a view computed from the two IDs and hashes the map already records; it is not a background reconciler, a queue or a service, and it must not run while a match is live. A creator who never opens the editor again still leaves a usable official item behind, because the write happened at publish time; the list exists for the writes that failed and for maps published before this feature existed.

    Developer-uploaded curated content on the official AppID is a separate, complementary path and costs nothing to use: an upload from the partner account can populate the full game's Workshop with reviewed maps before release, and it needs no client code.

23. **A map published on the demo stays playable outside Workshop after the switch.** Importing its `.rmap` into the local library, or receiving it from a host through the lobby's host-transfer path, still works on the full build. Do not add an AppID check that refuses a map whose metadata names an older AppID; the map's content and its `EngineGeneration` are what decide whether it runs, not the AppID it was published under.
24. **A composed map uploads the content it composed, and the uploading account is the one warranting it.** A map built from references (task 09) physically contains the referenced maps' assets, their merged metadata and a rewritten copy of their C# under `composed/`, so its Workshop item republishes that content under the item the uploading author owns. Composition is exactly what the referenced map's license permits, so this is not a violation; but the speaker is the uploader, and Steam's Workshop agreement has the uploading account warrant it holds the rights to everything in the item. So a public publish reads `references.json`, names the referenced maps and their authors in the checklist it already shows, includes them in the generated credits line (task 18), and states in one plain sentence that the item also carries another creator's content. Do not block the publish, and do not try to upload only the parent's own delta: there is no per-file Workshop API and a partial item would not run. Keep `references.json` and `composed/` in the upload, because they are what lets the next author open the map and keep extending the composition.

## UX

- The publish dialog keeps its familiar shape: map summary, title, description, preview, visibility, publish button and progress.
- A public publish shows a short checklist before upload; a private or unlisted publish goes straight to upload.
- The long greenlight/beta explanation is replaced by a short "Your map is published to Steam Workshop. You can update it at any time." message.
- The dialog also states the ownership limit once, in plain language: "Steam hosts this item. You own it and can delete it. Realm can hide it from in-game discovery, but cannot delete it."
- "Publish to Workshop" and "Export .rmap" are two separate actions in the same dialog, not one replacing the other.
- In a preview build the publish action is replaced by one plain line saying publishing is disabled there and to use the public build; the export action is untouched.
- On the demo AppID the dialog carries one plain sentence, with the visibility choice, that the map is published twice: once for demo players now, and once as a hidden listing the full game inherits. When the second write failed, the same line says so and offers "Sync to full game".
- The "sign in to publish" prompt is gone.
- Errors appear in the dialog, not only in the console.
- After a successful publish, offer "Open Workshop page" and "Copy link".
- The dialog links the author's Steam profile and the Workshop item's edit page, and includes a generated credits line.

## Out of scope

- Downloading, subscribing and installing Workshop maps (task 08).
- Map identity and versioning in the game (task 09).
- Deleting the old distribution server (task 10).
- Reviews, ratings, greenlight and moderation (task 13).

## Acceptance criteria

- A map author publishes a map from the editor with a progress bar, and the item appears on the Workshop page.
- Publishing the same map again updates the same item and does not create a duplicate.
- The uploaded package contains no Debug WASM, no source, no `.csproj` and no `lib/` DLLs.
- A new Steam account can subscribe to and install the item.
- The author can choose public, friends-only, unlisted or private.
- The legal-agreement failure is explained with an actionable link.
- The Realm UGC license is shown once and accepted before the first upload.
- A first publish defaults to private or unlisted and is not made public automatically.
- A public publish is blocked until the checklist passes; a private or unlisted publish is not.
- No SteamCMD process is started by the game.
- Only full playable maps can be published as Workshop items.
- The local `.rmap` export still works and is separate from the Workshop publish.
- The dialog states that the author owns the item and that Realm cannot delete or transfer it.
- A preview build cannot create or update a Workshop item, and says so without offering an override; its playtest and `.rmap` export still work.
- A publish under the demo AppID creates or updates both the demo item and the dormant official item, and both IDs, hashes and `time_updated` values are recorded.
- A failed official write does not fail the demo publish; the map appears in the editor's sync list with one action to complete it.
- No Realm account, creator registration, external creator profile or greenlight call is made.
- The publish dialog opens the author's Steam profile and Workshop item edit page.
- The generated description includes the map's Discord invite when set, and no other creator links.
- The old publish endpoint is no longer called by the editor.

## Pitfalls

- Do not upload the whole workspace. It contains build output, symbols and source that players do not need and should not receive.
- A Workshop item belongs to one AppID, so publishing for two AppIDs means two items and two IDs. Do not try to make one item visible to both apps, do not let a failed official write pass silently, and do not let a map that has only a demo item look broken at cutover.
- Do not build the dual write on an unvalidated assumption. Publish one item to the official Workshop from a demo client first; if the official AppID refuses it, fall back to a demo-era notice plus a republish at cutover and say so in the task report.
- Do not use or embed SteamCMD in the in-game publish flow. SteamCMD is for the developer archive tool (task 17) and CI; the author-facing flow uses the Steam client API.
- Do not publish individual assets or asset packs as separate Workshop items. A Workshop item is a complete map. A composed map is still one complete map, not a parent plus dependencies; do not split it into items or upload a reference instead of the content it resolved to.
- Do not keep the 15 MB per-asset limit if it only existed for the custom CDN. Keep a sensible total-size warning; Steam has its own limits.
- Do not delete the map's manifest or local export format. Workshop replaces the transport, not the package format.
- Do not make the Workshop page the only place the map exists. The author's local map remains the source of truth and must still be editable and testable offline.
- Do not assume Realm can delete, take over or transfer a Workshop item. Only the author or Valve can delete it.
- Do not build a publish-approval queue to keep test maps out of discovery. Use the trusted filter in task 13.
- Do not remove the local `.rmap` export. It is the only way to share a map outside Steam, and it is intentionally a complete package.
- Do not upload a `.rmap` as the Workshop content. Upload the runtime folder so the client can read the installed folder in place.
- Do not detect the preview branch at runtime, and do not leave a setting, an override checkbox or a "publish anyway" path that re-enables it. The rule is a constant in the build CI uploads to the branch, or it is not enforced.
- Do not disable the editor, playtesting or the `.rmap` export in a preview build. Only the Workshop upload is turned off, because that is the only action that can leave an artifact behind on the public channel.
- Do not link to a Realm creator profile page. It does not exist; use the creator's Steam profile and Workshop item.
- Do not add a general creator-link list, a website field or a donation section to the publish dialog. The only optional external creator link is the Discord server invite already in the map metadata.
