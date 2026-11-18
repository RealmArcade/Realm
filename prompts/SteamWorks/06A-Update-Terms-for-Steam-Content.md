# 06A — Update the Terms for Steam Content and Community Mirroring

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 01. Run this before 07 (the first Workshop publish) and before 17 (the archive tool).
**Unblocks:** 07, 17.

## User story

As the developer, I want the Terms to describe a Steam-first game and to grant every user the right to mirror, fork and maintain any map, so a community mirror or an abandoned-map rescue is lawful without asking each creator individually, while imported assets stay inside the Realm ecosystem.

As a creator, I want to know that publishing a map grants Realm and other players those rights, and that I can still delete my Workshop item.

## Context

The current documents assume Realm operates accounts, a UGC registry, a CDN, a relay and community nodes. They grant the Operator a license only for "official Realm Platform services, databases, or client distributions", and grant "registered Realm Platform users" a license to play and remix. Neither clause clearly covers:

- a user mirroring another creator's Workshop item;
- a user maintaining an abandoned map;
- a mirror that survives the creator deleting the Workshop item;
- distribution through Steam Workshop, which Valve hosts under its own agreement.

The game is open source under MIT, but the MIT license covers the engine code only. The UGC license is the only thing that keeps imported assets inside the Realm ecosystem. There is no Realm creator profile, account or website: the creator's Steam profile and Workshop page are their public presence, and the game only links to Steam plus the optional Discord server invite a map declares in its metadata.

This is a drafting task, not a legal opinion. Flag every clause that needs a lawyer.

## Requirements

### A. Rewrite the Terms of Service (`Terms/RealmPlatform_ToS.txt`)

1. **Scope.** Describe the game client, the editor, local map tools and the in-game curation. State that Steam provides identity, lobbies, transport, Workshop hosting, stats, moderation and the creator's public profile and Workshop pages. State that Realm does not host creator profiles, accounts or a website. Delete the claims that Realm operates a centralized coordination layer, a UGC registry, an account database, a CDN, a relay or community nodes.
2. **Remove the build gate over Steam services.** Delete "Official Services are reserved exclusively for authorized official client builds." Replace it with: access to Steam services is governed by Steam ownership and Valve's agreements; Realm does not control them.
3. **Community nodes and relay.** Delete the node and relay section, or replace it with "Realm does not operate community servers, relays or a lobby registry." Keep the rule that a fork must use its own Steam AppID and Steamworks account.
4. **Reverse engineering.** Narrow it to: do not impersonate Realm, do not scrape the in-game curation, and comply with Valve's terms. Remove references to endpoints that no longer exist.
5. **Standalone builds.** Keep the clause, updated: a fork or standalone build is a distinct product, uses its own AppID and backend, and has no license to Realm's Workshop items, curation or UGC.
6. **Monetization.** Keep the no-pay-to-win rule, and state it in the strongest form the product can keep: the game is free, no map or mode is purchasable, and nothing purchasable modifies gameplay, reach, information or matchmaking. Add that the only purchasable objects are the Operator's own cosmetic items for the official game, that no map, mode or creator content is sold, that Steam's terms govern any transaction and Valve processes the payment, and that the Operator processes no payment, holds no funds and hosts no creator profile. State that any donation or commercial link belongs to the creator's own platform and must comply with Valve's rules, and that the game displays no donation or external payment link. It may show a Steam-managed purchase control for the Operator's own cosmetics and the map's optional Discord server invite; neither is a link out to a payment page. There is no revenue share with a creator, which is the decision recorded in task 21.
7. **Steam terms.** Add a clause incorporating the Steam Subscriber Agreement and the Steam Workshop Legal Agreement. State that Valve may remove content, suspend accounts and change availability; Realm does not control purchases, refunds or account enforcement.
8. **Moderation.** Add a clause: Realm may hide, ban or mark incompatible any UGC in its in-game surfaces, remove a mirror, and report an item to Valve. Only the creator or Valve can delete a Workshop item.
9. **Takedown.** There is already a takedown path in the product: `GameOver` opens a `DMCA/Copyright Infringement Report` issue on GitHub, and the main menu's BUG REPORT button opens the same tracker. Keep it, and state in the terms where it is and what happens after a report arrives; do not invent a second address and do not describe a dispute-agent process. If counsel wants a dedicated mailbox later, that is a one-line change.
10. **Creator identity and links.** State that the creator's public presence is their Steam profile and Workshop page, that Realm does not host a creator profile, bio or link list, and that the game does not display donation or payment links. The only creator link the game shows beyond Steam is an optional Discord server invite declared in a map's metadata; it is the creator's responsibility and must comply with Valve's rules.

11. **State the promises that remain, positively.** The document must not read as a list of withdrawn claims. State, in the developer's own voice, what a player and a creator can rely on because none of it depends on a Realm-operated service: the engine source stays MIT-licensed; a map exports to a self-contained `.rmap` and runs offline; single-player, the editor and local maps need no Steam login; there is no DRM; the game stays free. Say what those actually guarantee — nobody can lock a player out of work they already made — and stop there. They are the promises the Operator controls, and they are the reason a Steam-hosted game is still not a walled one. Do not extend them into a claim about how long Steam will operate.

### B. Rewrite the UGC License (`Terms/RealmPlatform_UGC_License.txt`)

**Definitions before the numbered clauses.** Define "official game" as the Realm client distributed by the Operator under an AppID the Operator publishes (the demo AppID during pre-release and the full game's AppID afterwards), including its editor and local tools, and "UGC" as the maps, scripts, art and other content published for it. Every mirror and derivative right below is limited to the official game.

1. **Ownership.** The creator owns the UGC. Ownership is not transferred to Realm or Valve.
2. **Representations.** The creator has the rights, the UGC does not infringe, complies with Valve's agreements and the Steam Online Conduct Rules, contains no malware, and (unless the product allows mature content) contains no sexually explicit content.
3. **Operator license.** Grant the Operator a perpetual, worldwide, non-exclusive, royalty-free license, irrevocable for copies already distributed, sublicensable, to host, cache, store, mirror, archive, reproduce, distribute, display, format and create derivative works for compatibility, moderation, preservation and in-game display, through Steam Workshop, any Realm mirror or archive, and any future official channel. It must survive the creator deleting or privatizing the item. Do not narrow this grant with a non-commercial carve-out, and do not add a clause that limits it to hosting, distribution and display in a way that excludes a paid product. The Operator's own monetization of the base game is exactly what this grant has to cover without creating a claim for a creator whose UGC is merely present in it, so it is written broad and stays broad. If counsel wants that stated explicitly, the narrow addition is that the grant covers distribution as part of the official game, including where the Operator makes parts of the game available for a fee; that is an addition for counsel's review, not a change of direction for the licence.
4. **Community mirror and fork license (new).** Grant every person who lawfully possesses the UGC a perpetual, worldwide, non-exclusive, royalty-free license, irrevocable for copies already distributed, sublicensable, to:
   - store, mirror, archive and redistribute the UGC in whole or in part, in its original form or as a `.rmap`, for use with the official game;
   - create, publish, distribute and maintain derivative works (forks, fixes, updates, ports) for use with the official game, including maintaining an abandoned map;
   - let end users do the same.

   Conditions:
   - attribution to the original creator and a link to the original Workshop item where it exists;
   - no implication that the mirror or derivative is the original or is endorsed by the original creator;
   - the mirror or derivative is licensed under these same terms (share-alike);
   - the mirror or derivative may not be used to distribute the UGC as a standalone product outside the official game, except for assets the mirror author authored personally;
   - a mirror or derivative of a composed map carries the referenced creator's content as well as the parent's, so it credits both, and reliance on this clause for the referenced content rests on that creator's own publication of it rather than on the parent's grant;
   - a creator may ask the Operator to remove a low-effort mirror that adds no fixes or derivatives. The Operator may honor that as a policy, but the license for copies already distributed survives.

   If counsel agrees, add a covenant not to assert claims against users or the Operator for uses permitted by this license.
5. **External and standalone.** State the permission before the restriction, because a creator reading only the restriction concludes their own work is trapped. A creator may export and publish, as a standalone product on Steam or anywhere else, any map whose every asset and script they authored personally, built on the MIT-licensed engine, including the maps that engine embeds. That product is theirs and needs nobody's permission. It must use its own AppID and its own Steamworks account, it carries no right to the Realm name or logo (`TRADEMARK.md`), and it has no license to Realm's Workshop items, curation or UGC. Assets imported from another creator may not be carried into it without that creator's separate written permission, and the editor warns when an export contains them (task 18). "Authored personally" therefore has to be defined against the mechanism that brings other people's work in: a map built from another map's references (`references.json` and the `composed/` sources and assets it resolved, task 09) contains that map's content, so it is not a map of the creator's own authorship, and the editor's export warning names those referenced maps the same way it names imported assets.

6. **Nobody else's application may ship this AppID's UGC.** No third party may operate a Steam product that distributes UGC taken from an AppID the Operator publishes, whether by mirroring Workshop items, by harvesting `.rmap` files or by republishing a map under their own item. Every grant in clauses 3 and 4 is limited to the official game, a creator's Steam grant runs to Valve and to the users of the AppID the item was published under, and neither the Operator nor the creator has licensed that content to any other application. State plainly that this is a bound on permission and a policy the Operator acts on by reporting to Valve, not a technical control: nothing prevents a determined party from copying files, and claiming otherwise would be the kind of promise this engine cannot keep.
7. **Steam and Valve.** The creator grants Valve the license required by the Steam Subscriber Agreement and the Workshop Legal Agreement. Valve's terms govern the Workshop item; this license is separate and supplementary.
8. **Acceptance.** Publishing a Workshop item for any AppID published by the Operator — including the demo-era item and the dormant item written into the full game's Workshop by the same publish action (task 07) — by any tool, is acceptance of this license. The editor also shows the license and requires explicit acceptance before the first publish, recorded locally with the version and date. The "publishing is acceptance" sentence must also be stated on the app's Workshop page so a creator who publishes through SteamCMD is covered.
9. **Moderation and removal.** Realm may hide, ban, mark incompatible or remove a mirror; the creator may delete the Workshop item; the license for distributed copies survives. Provide a takedown contact.
10. **Donations and links.** Realm does not host a creator profile or process payments, and the game does not display donation or external payment links. A creator's Steam profile and Workshop page are governed by Valve's rules. The only creator link the game shows beyond Steam is an optional Discord server invite in map metadata; it is the creator's responsibility. A Steam-managed purchase control for the Operator's own cosmetic items is not a payment link. The game never shows a price, a purchase control, a "support" request or a donation action for a creator or for a map, and a creator's own support channel is the Discord server they run. State that Steam's rules prohibit commercial activity in Workshop content, including soliciting payment, so a donation or payment solicitation belongs in the creator's own community rather than in a Workshop item or in the game, and that the creator is responsible for what they publish in either place. No pay-to-win.
11. **Privacy.** Realm stores only the SteamID64 in map authorship metadata, the attribution tags, the optional Discord server invite in map metadata, and — inside the map package itself — the author's public key and a signature over the package. The public key identifies a signing key, not a person, and no private key ever leaves the author's machine or Realm's servers receive it. Realm does not store a bio, a link list, an email or a profile copy. Steam's privacy settings govern what the creator's profile and Workshop pages show; a creator can hide or delete their Steam content through Steam. No password or email is required.
12. **Versioning.** Version the license and record the accepted version. Material changes take effect on the next publish. `MapMetadata.License` embeds the canonical licence URL as a default, and every published map carries it, so bump it to the new version and confirm the URL resolves to the committed text; a map pointing at a retired licence URL is a licence nobody can read.

**No creator revenue clause.** Do not add an opt-in per-map commercial grant, a revenue share, a payee list or a split. The game sells only the Operator's own cosmetics, so there is no creator revenue to license, and a clause providing for one would describe a product that does not exist. Task 21 records why per-creator sales were dropped; if they are ever revisited, the clause returns in that task, with counsel, alongside the product.

### C. Brand and engine

- `Terms/TRADEMARK.md`: add the Steam store page name to the examples that require permission. A mirror may say "built using Realm Engine" and link to the original Workshop item, but may not use the Realm name or logo as its own product name or imply endorsement.
- `Terms/TRADEMARK.md` and the UGC license: state that a map inspired by another map is permitted and lawful. A creator may build a similar map from scratch, copying nobody's assets, scripts or text and asking nobody's permission, and the reverse-to-prompt workflow the editor's generation tools support is a normal way to do it. Ideas, rules, mechanics and pacing are not what this license protects; only the expression of them is. Do not write a clause restricting "clones", "similar maps" or "inspired works": it would be unenforceable, it would read as a threat to the practice that produced every lasting map in this genre, and a competing map built honestly is exactly what an open engine is for.
- `Terms/RealmEngine_License.txt`: do not modify the MIT text. Add a note in the UGC license and the README that MIT covers the engine code only, not UGC, assets, the Realm name, the AppID, Workshop items or creator names.
- **Create `Terms/Steam_Migration_Terms_Review.md`.** It does not exist yet — do not go looking for it and do not treat its absence as a reason to skip this. It is the review artefact beside the two rewritten documents, and it must list: every clause that no longer describes the product (with the replacement written above), every item this task resolved, and every question that needs a lawyer before the documents are published. Keep it out of the game and out of any map; it is a working document for the developer and counsel.

### D. Mirror operation and account separation

1. **A mirror is uploaded from a dedicated Steam account that owns the game, never the partner account.** The account that uploaded a Workshop item is the account a rights complaint, an infringement notice and an account-level strike are served on, and the account Valve's repeat-infringer process acts against. Keeping that account separate from the partner account keeps the partner account out of that path. The game is free and has no DRM, so owning it on a second account costs nothing and is not a constraint worth designing around.
2. **State plainly that this is a separation of exposure, not immunity.** The mirror is still a Workshop item for this AppID, so the app itself is still the surface a complaint is about, Valve can still contact the partner team about an item under the app, and accounts under common control can still be judged together when a pattern is assessed. Do not write a clause, a README line or a review note that implies the second account shields the app.
3. **Mirroring is discretionary and reviewed, never automatic.** The license permits a mirror; it never obliges the Operator to make one. Mirror only content the developer has personally reviewed, and record per mirror: the source item, the creator's SteamID64, the licence version and clause relied on, the reviewer, the date, the account used and the review note. That record is what answers a counter-notice or a creator's question later.
4. **Mirrors are Workshop items only.** Realm does not mirror content outside Steam: no Realm-hosted mirror site, mirror storage or mirror download page exists or is planned. Valve hosts every mirror, so a takedown contact is sufficient and no formal dispute-agent process is needed. Say this plainly in the terms, so nobody reads the license as a promise of an archive Realm hosts. If that ever changes, hosting user content on a Realm-controlled site would make the Operator the host and would require a designated dispute agent and a repeat-infringer policy rather than only a takedown contact; flag that to counsel before such hosting exists, and do not write a clause that anticipates it now.
5. **The license must not promise a mirror.** Say that a creator may request a mirror and that the Operator may decline, and that only content actually mirrored survives the creator deleting the item.

## UX

- The first publish shows the Realm UGC license with a scrollable summary, a link to the full text, and an explicit "I accept" action. The acceptance is recorded locally with the license version and date. The editor's single Discord invite field carries the non-expiring-invite helper instructions.
- The publish dialog links Valve's Workshop Legal Agreement separately.
- There is no Realm creator profile page; the editor and the publish dialog link the creator's Steam profile and Workshop item instead, and the UGC license links the Steam Workshop Legal Agreement.

## Out of scope

- Building a creator profile website, database or OpenID login (task 16 explicitly removes it).
- Implementing the archive (task 17).
- Changing the MIT license.

## Acceptance criteria

- No Terms clause claims Realm operates Steam services, a registry, a CDN, a relay or an account database.
- The UGC license grants the Operator a mirror right that survives deletion.
- The UGC license grants every user the right to mirror, fork and maintain a map for use with the official game, under attribution and share-alike.
- The license permits a mirror without promising one, and the mirror-operation rules name the separate uploading account, the review record, the limit of that separation and that mirrors live only on Steam Workshop.
- The standalone permission is stated affirmatively before its restrictions, the rule that only assets the creator authored may be exported remains, and composition counts against personal authorship so a composed map cannot be published standalone as if it were self-authored.
- The license states that no other application may distribute this AppID's UGC, and states that this is a permission bound and a reporting policy rather than a technical control.
- Similar and inspired maps are explicitly permitted, and no clause restricts them.
- A modified copy is not presented as the author's release, the clause says so plainly, and it points at the package signature a player can check rather than at any server of Realm's.
- The Steam/Valve relationship and the acceptance path are stated.
- The trademark policy covers the Steam store page.
- `Terms/Steam_Migration_Terms_Review.md` exists, lists the clauses that no longer describe the product, the items resolved here and the questions for counsel.
- No clause implies a Realm creator profile or a general creator link list; the only in-game creator link beyond Steam is the optional Discord server invite in map metadata.
- The UGC license carries one broad royalty-free Operator grant and no per-creator revenue, split or payee clause, and the ToS states that the only purchasable objects are the Operator's own cosmetics for the official game.
- The task report lists the clauses that need a lawyer.

## Pitfalls

- Do not claim the MIT license covers UGC or assets. It does not.
- Do not grant a standalone right to imported assets. That would break the ecosystem lock.
- Do not make the mirror license revocable for copies already distributed; that defeats the purpose.
- Do not confuse a takedown policy with a license term. The license survives; the policy is a courtesy.
- Do not state that Valve's terms are replaced by Realm's. They are separate layers.
- Do not use "registered users" or "Realm account"; those no longer exist.
- Do not promise that a mirror makes a map permanent unless the developer has actually mirrored it, and do not describe a Realm-hosted archive: mirrors are Workshop items and nothing else.
- Do not state or imply that a mirror uploaded from a second account puts the app beyond Valve's reach. The item is still UGC under the AppID; the second account changes who receives an account-level notice, not whether the app is involved.
- Do not mirror anything unreviewed, and do not let the archive tool (task 17) republish automatically on the strength of this license.
- Do not invent a Realm creator profile, bio page or donation page. The creator's Steam profile and Workshop page are the only public presence.
- Do not state that Realm stores a bio or social links. It stores only the SteamID64 in map authorship metadata, attribution tags, an optional Discord invite in map metadata, and the package's own public key and signature.
- Do not leave the licence silent about a modified copy. A map may be edited and republished under the licence, and a copy that has been changed is not the author's release: the licence should say so plainly and point at the signature a player can check, so 'open source' is not read as 'any copy is the original'. Never ask for, store or accept a creator's private key.
- Do not write a general external-links or creator-website clause. The only creator link the game shows beyond Steam is the Discord invite, and the terms must not imply a profile or link-list service.
- Do not promise continuity of Steam's services on Valve's behalf. Describe Steam as the storefront and platform that hosts identity, lobbies, transport, Workshop and stats under its own published rules. A promise about Valve's permanence is one the Operator cannot keep, and writing it down converts a sound decision into a liability.
- Do not leave the "community volunteer infrastructure" claim anywhere, in any softened form. There are no community nodes, no volunteer relay and no registry, so a surviving claim is exactly the gap between what the game says and what it does that this migration exists to close.
- Do not deploy or publish the Terms without the developer's review; flag the legal questions.
