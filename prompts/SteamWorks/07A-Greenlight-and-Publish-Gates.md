# 07A — Playtest Greenlight, Unlisted-First Publishing and Publish Validation

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 04 (a seated match with verified SteamIDs), 07 (publishing works).
**Unblocks:** 13 (it shows the playtest line), 15.

## User story

As a map author, I want a reason to put my map in front of a few people before the public catalogue sees it, so the first thing strangers find is a version somebody has actually played.

As a map author, I want the votes to be collected while people are playing, with nothing to sign up for and no service to wait on, so asking my Discord for one lobby of testers is enough.

As a player, I want the map browser to contain maps that other people have played, not a thousand blank test maps, without the game hiding maps from me on its own.

As the developer, I want that bar to cost me no database, no queue, no approval step and no moderation time.

## Context

The old greenlight was a server feature: 100 verified reviews, an author-facing request, an admin queue and admin endpoints, all inside `Realm.AdminServer`. It is deleted with that server (task 22), and `00` records the removal as a requirement change rather than a regression — a map is published at the visibility its author chose.

What that leaves is a specific gap. Steam's own floor is low and is about abuse, not quality: a Limited account cannot submit Workshop content at all, so the spam floor is "an account that has spent about $5", and nothing on Steam asks whether a map was ever played. Meanwhile the editor opens on a blank map, so "press Publish the moment the editor appears" is the default path for a new player, and it produces a legitimate Workshop item.

The result is a catalog of accidentally-published test maps. Sorting by votes and trend buries them, which is enough for a catalog of real maps and not enough for a catalog where they are the majority. Every option that fixes it centrally needs a service, and a service is the thing this migration exists to delete.

So the friction goes where it costs nothing to operate: **the author's own machine, applied to the one transition that matters — from unlisted to public.** Steam supplies two primitives this rests on:

- `ISteamUGC::SetItemVisibility(handle, eVisibility)` on the update handle, with `Public`, `FriendsOnly`, `Private` and **`Unlisted`**. An Unlisted item is reachable by link and **absent from Workshop browse and search**, so it can be handed to testers while being invisible to the catalogue.
- `SubmitItemUpdate(handle, changeNote)`, which records a change note per content revision. That note is what a later "Revert to this version" is chosen by (requirement 13).

Nothing in this task stores anything outside the author's machine, and nothing in it is a security boundary.

## Requirements

1. **A first publish is Unlisted, never Public.** The item is created (or updated) with `k_ERemoteStoragePublishedFileVisibilityUnlisted` set through `SetItemVisibility` before `SubmitItemUpdate`. The author still chooses between Unlisted, FriendsOnly and Private; **Public is not offered until the map is greenlit** (requirement 4). Because an Unlisted item cannot appear in browse or search, an accidental publish is a URL nobody has rather than a row in the catalogue, and the "ten thousand blank maps" scenario costs the catalogue nothing.

2. **The item exists immediately, and it is useful immediately.** The author gets the item ID on the first publish, can update it freely, and can hand the link to anyone; an Unlisted item is also the state a playtester's client can subscribe to, so a test lobby can use a real Workshop item before the map is public. Do not withhold the item ID as a reward, and do not delete an ungreenlit item.

3. **Playtesting is a HUD vote during a real match.** While a map is being hosted and played in a lobby — not the editor's preview, not the local test map — the in-match HUD shows one small control in a corner: playtested, not playtested. It is **disabled until five minutes of match time have elapsed**, and its tooltip explains in one translated sentence why it exists and what voting does. A player may ignore it entirely; it never blocks, covers or interrupts anything, and it is dismissed after a vote. One vote per player per map per session, changeable until the match ends.

4. **The authority receives the vote and keys it by the seat's verified SteamID.** The vote is one small client-to-authority message on the existing match connection. The authority must not accept a SteamID from the client — it uses the identity the handshake already established (tasks 02 and 04) — and it records the vote against that SteamID. A **computer seat has no SteamID and cannot vote**, so an author cannot greenlight a map by filling a lobby with bots. Votes are valid only while the seat is seated; a dropped player's vote stands.

5. **The ledger is an editor-only sidecar on the author's machine.** The host's client appends each accepted vote to `%LOCALAPPDATA%\Realm\playtest\<map key>.bin`, keyed by map name the way `MapHistoryService` keys its stores, so two folders of the same map share one ledger. Each record is written in the encoded form requirement 6 describes, so the file is not a readable list of SteamIDs and votes. It is never written inside the map folder, never included in the staged package, never in the `.rmap` export, and never uploaded anywhere. It must survive the workspace being moved, renamed on disk or re-cloned, which is why it is not keyed by path or by package hash: a package hash resets on every edit and the threshold would never be reachable.

6. **The ledger travels by hand, as an opaque code.** After voting, the voter gets one short code they copy and paste into chat, and the author pastes it into an "Add a tester's code" field in the publish panel, which checks it and appends one vote. That is the entire transport for a small community, and the code is deliberately unreadable: an authenticated envelope, `base64url(version ‖ nonce ‖ tag ‖ ciphertext)`, wrapping a small JSON payload `{steamId, mapKey, vote, issuedAt}`, encrypted and authenticated with `AesGcm` under a key derived with `SHA256` from a constant compiled into `Realm.Shared`. A 12-byte random nonce per code and the GCM tag mean a code whose bytes were edited fails to open rather than producing a different vote, the same code pasted twice appends nothing because the ledger de-duplicates by SteamID, and a code minted for one map key is refused by another. Version the envelope in one leading byte so a later format is never misread as this one. Do not use plain base64 of JSON — it is a well-known format that any web decoder reads in one click — and do not print the SteamID, the map key or the vote as words beside the code. The envelope is what lets a tester drop a vote into a public channel without publishing who they are or which map they were lukewarm about, which matters because a SteamID is public data that most players do not know is public.

    The key is not a secret, and the place it is defined says so: it ships in every build, so anyone who reads the source can mint a code for any SteamID. That is the same line the rest of this task holds — tamper *resistance* against an accidental edit or a casual nudge, never a defence against someone who has decided to cheat. Keeping the payload JSON inside the envelope is the right trade: the format stays legible to the next person who opens the source, and a vote code is generated once per vote and never touches a wire, so its size does not matter. The on-disk ledger (requirement 5) stores the same encoded records rather than readable JSON, so the file is not a browsable list of who played what.

7. **The threshold is map data with a neutral default.** The default is **25 distinct SteamIDs** for a first publication, cumulative across as many sessions as it takes. A map or mode may state its own threshold in metadata and lobby settings — a single-player campaign, a two-seat board, a duel map — and a map that states nothing gets the default. Downvotes are **recorded and shown to the author only** ("3 of 8 testers asked for more work"); they never subtract from the count, never block a publication and are never shown to other players. A vote from the same SteamID twice counts once, ever.

8. **Greenlight belongs to the map, not to a build.** Once a map is greenlit, later publishes may be Public without new votes, because the alternative is a crash fix costing another 25 votes and authors learning not to fix crashes. Greenlight is retained by Workshop item ID, so it follows the item rather than the folder. The one reset is a change of item owner.

9. **Promotion is one explicit action.** The publish panel shows a progress line ("7 of 25 testers have played this") and a **Publish publicly** action that is enabled only when the map is greenlit. Selecting Public before that is refused with the progress line and one sentence, not a modal lecture. Do not promote automatically on reaching the threshold: the author decides when the map is ready, and a map can stay Unlisted for as long as they like. Steam's own Workshop page can change an item's visibility afterwards, which is how a determined author gets around this; that is acceptable and needs no defence.

10. **A greenlit map carries a marker both parties can read.** At promotion, write the key-value tag `greenlit=1` with `SetItemTags` (alongside the existing tag catalogue, not replacing it) on both the demo item and the official item (task 07). The tag is self-declared, so it is a hint for ordering and display and is never a trust or security signal — that is the Verified mark's job (task 13), which the creator cannot write.

11. **Validation replaces the vote on updates.** The reason a sanity check existed was a bad publish breaking a good map, and a validator catches that earlier and without players. Before any upload, run the validation the client already runs when it opens a map — at least one player start, non-empty terrain, every referenced asset present, manifest and metadata valid — and refuse the upload with the specific failure. An update needs no votes: the author has Steam's per-version revert (requirement 13), their own `MapHistoryService` history, and roll-forward.

12. **A blank map cannot be published by accident.** The editor records the package hash of a freshly created map; a staged package matching it is refused with "This is still the blank map you started with." A default or placeholder title and, for a public publish, an empty description are refused the same way. This is in addition to task 07's public checklist, and it applies to Unlisted publishes too, because the accidental case is exactly the one that never reaches the public path.

13. **Every publish writes a change note.** `SubmitItemUpdate` is called with a generated note: game build, package hash prefix, object and player counts, and the version label the author last saved. Steam's **Revert to this version** on an item's Change Notes tab is the free rollback and it is chosen by these notes, so an author who publishes a bad build can put the previous one back with one click and no client code. Do not build a version picker: Steam serves the latest version to every subscriber and offers no per-subscriber choice.

14. **No service, no queue, no upload, no account.** The vote is decided on the author's machine; the ledger never leaves it except by a copy the author makes; nothing is signed, verified or attested; nothing is sent to Realm. State in the code and in the task report that the ledger can be edited by anyone who tries, that this is accepted, and that the only thing the bar is defending against is a player who did not realise their map was unfinished.

15. **This is not a discovery filter.** The greenlit state may be shown in the map browser as one small "Playtested" line and offered as an optional sort or an opt-in view (`AddRequiredKeyValueTag("greenlit", "1")`), and it must never be the default view, never gate a join, and never hide a map (task 13). Unlisted items are not shown at all, which is Steam's own behaviour and not our filter: nothing that is published is hidden.

## UX

- The vote control is one small chip in a corner of the in-match HUD: an up and a down choice, translated, with a tooltip. Before five minutes it reads as locked and its tooltip says to play a little longer.
- Voting is one click, then a short thank-you with the copyable code and a "Copy" action. No text box, no star rating, no comment, no review.
- The thank-you says plainly what the code carries — that it identifies this player to the map's author so one person counts once — because a code nobody can read should still be one nobody is surprised by.
- The publish dialog's first-publish line names the state plainly: "Published as unlisted — only people with the link can find it. Get it played to unlock a public release." with the progress line under it.
- The publish dialog has one field for a tester's code, and one line saying where a tester finds theirs.
- A greenlit map's own publish dialog replaces the progress line with "Playtested by 25 people" and enables **Publish publicly**.
- Every new string is a `TranslationServer.Translate("literal")` call site.
- No screen mentions Realm greenlight review, verification, moderation or an approval queue: there is no queue and nothing reviews the map.

## Out of scope

- Public discovery, search, trust marks and moderation (task 13).
- The Workshop upload itself, packaging, tags and the public checklist (task 07).
- Steam's own up/down votes on the item, which are unrelated to this ledger and are read by task 13.
- Any per-version selection or in-client rollback: Steam's Change Notes tab covers it.
- Reviews, comments or a star rating in the game.

## Acceptance criteria

- A first publish creates the item Unlisted, and it does not appear in Workshop browse or search.
- The publish dialog offers Public only for a greenlit map, and refuses it with the progress line otherwise.
- A player in a hosted match sees the playtest control, cannot use it before five minutes, and can vote once.
- Computer seats never produce a vote, and a client cannot vote on another seat's behalf.
- Votes accumulate across sessions, keyed by distinct SteamID, in a binary sidecar under `%LOCALAPPDATA%\Realm\playtest`.
- A tester's code can be pasted into the editor and appends exactly one vote, and pasting it again appends nothing.
- A code whose bytes were edited is refused instead of counted, and a code minted for another map is refused.
- The code, and the ledger file, contain no readable SteamID, map key or vote.
- Varying the stated threshold changes the unlock point, and a map that states none uses 25.
- An update to a greenlit map publishes publicly with no new votes.
- A blank template map, a placeholder title and a missing player start are each refused before upload.
- A public publish is still gated on task 07's checklist.
- Every upload carries a generated change note, and the item's Change Notes tab lists one entry per publish.
- No network call, account, queue or server is involved anywhere in this feature.
- The map browser's default view is unchanged by this task, and no join is gated on it.
- The solution builds with no reference to the deleted greenlight endpoint or admin queue.

## Pitfalls

- Do not rebuild the greenlight service. There is no request, no queue, no reviewer, no admin endpoint and no HTTP call; the decision happens on the author's machine and the only record is a local file.
- Do not keep a server-verified greenlight "just for accuracy". Verifying a vote needs an identity service, which is the server this migration deleted, and the bar is not worth one.
- Do not publish Public by default and hide later. Unlisted is the default because hiding a published map is a filter, while never publishing it publicly is not.
- Do not key the ledger by package hash or by folder path. The first resets on every edit, so the count never arrives; the second breaks when the author renames or moves the workspace.
- Do not write the ledger inside the map folder. It would be uploaded, exported and shipped, and a tester's SteamID would travel with the map.
- Do not accept a SteamID from the voting client. It comes from the handshake, or it is not a vote.
- Do not let computer seats vote, and do not let the host cast more than one vote for its own seat.
- Do not subtract downvotes or block on them. A downvote that can prevent publication is a griefing tool; a downvote the author reads is feedback.
- Do not require votes on an update, and do not reset a map's greenlight when it is republished.
- Do not require votes from a mode that cannot seat other Steam accounts without letting it state a lower threshold or none. A single-player campaign that can never reach 25 is a map that can never be public.
- Do not treat the ledger, the envelope or the tag as a security control. The key is compiled into the game, so anyone who reads the source can mint a vote for any SteamID, and anyone who uses SteamCMD skips all three; the design accepts that and says so where the key is defined.
- Do not ship the code as readable text, as plain base64 of JSON, or with the SteamID printed beside it. A tester's vote should be safe to paste in a public channel, and "safe" here means "not casually readable", not "confidential".
- Do not reject a code for being old, for being reused by a different author, or for arriving after the match. There is no clock and no issuer to check it against, and a vote a tester sent in a Discord message two days later is exactly the case this exists for.
- Do not gate `.rmap` export, local hosting, host transfer or the editor on greenlight. A map that is not greenlit is a map that is not public, not a map that cannot be played.
- Do not add a version picker or an in-client rollback. Steam has none and its Change Notes revert is the supported path.
- Do not silently accept a publish whose change note is empty. The revert is chosen by that note.
