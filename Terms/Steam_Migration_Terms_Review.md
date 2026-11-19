# Terms review: what changed for the Steam build

A working document for the developer and counsel. It is not part of the game, is not shipped in a map, and is not legal
advice. It records what the previous documents claimed, what replaced those claims, and what still needs a lawyer before
the rewritten documents are published.

Companion documents: `RealmPlatform_ToS.txt`, `RealmPlatform_UGC_License.txt`, `TRADEMARK.md`, `RealmEngine_License.txt`
(MIT, unmodified).

## Clauses that no longer described the product

| Old claim | Why it was wrong | What replaced it |
| --- | --- | --- |
| "ONLINE SERVICES & API refers to the centralized coordination layer operated by the Realm Platform Operator, including: databases, UGC registry, and player data" | There is no coordination layer, registry or database. Nothing the game talks to is operated by the developer. | ToS §1 names what Steam provides and states plainly that no Realm accounts, registry, CDN, relay or game server exist. |
| "Official Services are reserved exclusively for authorized official client builds" | There is no service to gate. Access to Steam features is governed by Steam ownership. | ToS §2: Valve's agreements govern Steam services; the developer does not control them. |
| "Community Node Hosting … participant relays within the federated network" and "Unauthorized Ingress … federated community nodes" | The federated node and relay network is deleted. | ToS §1 last bullet and §6: multiplayer is Steam's, a fork uses its own AppID and Steamworks account, and there is no federation. |
| "Route third-party builds or forks through Official Services" (reverse-engineering section) | No endpoints exist to route through. | ToS §5: impersonation, endorsement, scraping and copying another creator's content into an outside product. |
| "They must route through their own independently hosted backend infrastructure, user registries, databases, and endpoints" | Described infrastructure the product does not have and would not want. | ToS §6: a fork is a distinct product with its own AppID and account, with no licence to this AppID's UGC. |
| "Creators may accept voluntary donations … within the Realm Platform. … circumvent the Platform Operator's designated monetization APIs" | No monetization API and no in-platform donation existed; the game now displays no donation or payment link at all. | ToS §7–§8: the game is free, the only products are the developer's own Steam cosmetics, a purchase is Valve's, and a creator's support channel is their own Discord. The publish dialog states Valve's prohibition verbatim. |
| UGC §3 "license to host, cache, route, distribute … across any current or future official Realm Platform services, databases, or client distributions" | Narrow, vague and tied to services that no longer exist; it did not clearly cover a mirror that outlives a deleted item. | UGC §3: a broad, perpetual, sublicensable grant through the Workshop, any mirror or archive, and any future official channel, surviving deletion, and explicitly covering distribution as part of the game. |
| UGC §4 "personal, non-exclusive license to play, stream, and adapt/remix … for use exclusively within the official Realm Platform" | Did not permit mirroring, forking, maintaining an abandoned map, or redistributing a `.rmap`; "registered Realm Platform users" no longer exist. | UGC §4: mirror, fork, maintain and redistribute rights for anyone who lawfully possesses the UGC, under attribution and share-alike. |
| UGC §5a "Community-operated peer-to-peer hosts, server nodes, or relays … indexed by the Platform Operator" | No indexing, no nodes, no relays. | Removed. UGC §10 covers the only mirroring that exists: reviewed Workshop items from a dedicated account. |

## What this rewrite resolved

- **One vocabulary.** Both documents describe Steam as the platform and the developer as the publisher of one free game.
- **A mirror right that survives deletion.** UGC §3 and §4 together let a community mirror or rescue an abandoned map
  lawfully, without asking each creator, and let an already-distributed copy keep existing after the item is deleted.
- **A mirror without a promise of one.** UGC §10 states that mirroring is discretionary, reviewed, recorded per item, and
  hosted only on the Workshop.
- **Standalone permission stated first.** UGC §5 permits publishing your own work as a standalone product before it lists
  the restrictions, and defines "authored personally" against the composition mechanism so a composed map cannot be
  presented as self-authored.
- **No other application may distribute this AppID's UGC** (UGC §6), stated as a permission bound and a reporting policy
  rather than a technical control.
- **Similar and inspired maps are permitted** (`TRADEMARK.md`), with no clause restricting clones or similar maps.
- **Free game, cosmetics only** (ToS §7): no map, mode or creator content is purchasable, nothing purchasable affects play,
  and there is no revenue share.
- **One optional creator link** (ToS §8, UGC §11): the map's Discord invite, with Valve's prohibition on commercial
  Workshop content stated plainly.
- **The acceptance path** (UGC §8): publishing is acceptance, and the editor requires explicit acceptance before the first
  publish and records the version and date locally.
- **The canonical licence URL is the versioned page on the public site**
  (`https://www.realm-game.com/RealmPlatform_UGC_License_v2.txt`). A versioned filename is what lets the URL a map embeds
  keep resolving to the text that map was published under, which is the assumption UGC §13 rests on. `Terms/` stays the
  source, the website's copy is refreshed from it whenever it changes, and the site's unversioned page serves the current
  text for anyone navigating there by hand.
- **Privacy** (ToS §10, UGC §12): SteamID64 in authorship metadata, attribution tags and the optional Discord invite, and
  nothing else. Diagnostics leave the machine only when the player exports them, with the SteamID opt-in.
- **The reporting path** (ToS §9, UGC §9) is the GitHub issue tracker the game already links from the end-of-match screen,
  with no invented mailbox and no dispute-agent process.

## Questions for counsel

1. **A DMCA intake that is a GitHub issue.** Takedown notices arrive as public issues in a repository the developer owns.
   That is a public intake for a legal notice, and it also means the notice is visible to everyone. Is a dedicated private
   address needed before release, and does receiving notices in a public tracker create any obligation the current path
   does not meet?
2. **No designated agent.** The Workshop item is hosted by Valve, so the developer is not a host of the content and has no
   designated agent under 17 U.S.C. §512. Confirm that this holds for mirrors uploaded by the developer's own second
   account — the mirror is uploaded by the developer, hosted by Valve.
3. **Share-alike and the Operator grant together.** UGC §4(c) requires a mirror to carry the same terms while §3 grants the
   Operator a broader, non-reciprocal licence. Confirm the intended interaction: the Operator is not bound by share-alike
   for its own hosting, and a mirror cannot use share-alike to claim rights in the Operator's game.
4. **Mirror account separation.** UGC §10 and the operating rules state that a mirror comes from a dedicated account that
   owns the game, never the partner account, and that this separates exposure rather than granting immunity. Confirm that
   the wording does not misstate Valve's repeat-infringer process either way.
5. **"Authored personally" and references.** UGC §5 treats a map's referenced content as someone else's work. Review
   whether a creator who authored the referenced map themselves is caught by it, and whether the editor's warning needs to
   distinguish that case.
6. **Announcements sign-up.** The main menu offers an optional newsletter sign-up that posts the typed name and address to
   a third-party e-mail provider (`stats.sender.net`). It is not an account and the game stores nothing, but it is personal
   data leaving the machine on the player's action. Confirm the privacy disclosure in ToS §10 is enough, or whether a
   consent line and a link to that provider's privacy policy are needed next to the field.
7. **Cosmetic entitlements and refunds.** ToS §7 states a refund revokes appearance only. Confirm the phrasing is
   sufficient for a Steam inventory item, and that nothing in it reads as a warranty about availability.
8. **The promises in ToS §3.** They are deliberately narrow (MIT source, `.rmap` export, offline single-player, no DRM,
   free). Confirm nothing there can be read as a promise about Valve's services or about the game's lifetime.
9. **Versioning.** UGC §13 makes a material change effective on the next publish and keeps an earlier map on the terms it
   was published under. Confirm that is enforceable alongside a Workshop page that always shows the current text.

## Not changed here

- `RealmEngine_License.txt`: the MIT text, untouched.
- The engine's own licence statements in source files.
- Anything about Valve's terms. They are a separate layer and the documents say so.
