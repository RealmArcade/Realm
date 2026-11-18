# 21 — Base-Game Cosmetics and Creator Support Channels

> Read `00-Architecture-and-Task-Order.md` first, especially "What this migration deliberately removes" and "Steam UGC decisions". This task depends on decisions stated there.

**Depends on:** 01 (Steam identity), 06A (terms), 07 (publish dialog), 16 (creator row and Discord field), 22 (no platform server). Run it after the cutover, on the official AppID.
**Related:** 13 (trust marks), 18 (attribution), 19 (guards).

## User story

As the developer, I want to earn from a small, fixed set of official cosmetic items that Steam sells and Valve accounts for, so the game stays free, no map is ever sold, and I never operate a payment path, a payout, a ledger or a per-creator storefront.

As a creator, I want one sanctioned place to invite players to my own community, where I can talk about supporting my work, without putting my map or my Workshop item at risk.

As a player, I want every map and every mode free, no advantage for money, and to see the supporter flair other players chose.

## Context

The earlier draft of this task sold a cosmetic pack per opted-in map and split the revenue through Steam's Workshop payment rules. **That is dropped, and this task replaces it.** Creators monetize on their own platforms instead.

Why it is dropped, so the decision is not quietly reversed later:

- Steam's per-creator revenue path requires an item definition per product, and only the AppID owner can author one (`IGameInventory/UpdateItemDefs`, a publisher-credential API). Which creators a sale pays is set separately with `IWorkshopService/SetItemPaymentRules` (`gameitemid`, `associated_workshop_files`, `partner_accounts`).
- Every collaborator must complete Valve's tax interview and banking details before they can be paid, Valve withholds payment below a threshold (currently $100) and for a hold period after a work's first distribution, and changing a split does not redistribute earlier sales.
- That makes the developer the seller of record to Valve for content they did not author, and the only party who can create, price or re-point the product — permanently, per product.
- For a free game with a young catalog, that is a standing operational and legal obligation per map, in exchange for money that will mostly sit under the payout threshold. The cost is in the obligation, not the code.

The conclusion is that creators are better served by their own platforms, and that the game's job is to make one compliant channel for that possible and then stay out of it.

**What this leaves.** The only purchasable objects are the developer's own cosmetics for the official game. There are no splits, no payment rules, no per-map products, no item-definition automation, no monetization block in map metadata, no payee rule for composed maps, and no entitlement surface in `IGameAPI`. Steam owns the checkout, the tax, the refunds and the chargebacks, and the game reads one boolean.

**The creator support channel.** A map may declare one optional Discord server invite (task 16), and that is the only external creator channel the game shows. The reasoning, which must survive into the wording the creator reads:

- Valve's rules reach this. The Steam Online Conduct rules incorporated into the Subscriber Agreement prohibit "engaging in commercial activity", with examples including posting advertisements and **begging**, and the Subscriber Agreement grants use of Steam for personal, non-commercial use and states that Workshop contributions are **in principle made available for free**. A payment or donation URL sitting in Workshop content is the clearest possible signal for that rule.
- No rule names Patreon, Ko-fi, PayPal or donations in a Workshop description, and there is no official statement permitting or forbidding a Discord invite there either. That is a fact about the documentation, not about the practice: the prohibition the creator is told about is the commercial-activity rule's effect, stated in plain words. Keep the sourcing in this file, state the conclusion in the UI, and never invent a quotation or attribute a sentence to Valve that Valve did not write.
- **Absence of a named rule is not permission, and the UI must not hedge as though it were.** A label reading "Valve has no specific rule about donation links" invites exactly the behaviour this task exists to prevent, because a reader takes the conclusion and drops the caveat. A payment solicitation in an item is the clearest case the commercial-activity rule can be applied to, so the game renders none and the publish guidance says plainly that soliciting payment in Workshop content is not allowed. Precision belongs in this document, not in a label nobody will read: the accurate basis is Valve's Online Conduct prohibition on commercial activity (which names advertising and begging), the Subscriber Agreement's personal, non-commercial use clause, and its statement that Workshop contributions are in principle free.
- What is documented is the shape of the risk: Valve may "restrict or remove Workshop Contributions for any reason", and enforcement of links is largely automated. A removed or banned item is the creator's whole map and, for demo-era content, the catalog entry players subscribed to.
- A community invite is the normal, Steam-supported kind of link (Steamworks itself has an External Links surface for communities such as Discord), and it moves the conversation to a place the creator moderates and controls. That is why it is the sanctioned channel and a payment URL is not.

So the game renders no donation or payment surface anywhere, and the publish dialog **teaches** the creator why. It does not police map content: it does not scan scripts or descriptions for URLs, block a publish for containing one, or report an item. Teaching plus a single sanctioned channel is the whole policy; the creator who ignores it takes their own risk, and Valve is the party that acts.

## Requirements

### 1. One product shape: the developer's own cosmetics

Sell only AppID-wide items for the official game — the supporter flair first, and at most a small fixed catalogue of appearance variants (a nameplate colour, a banner, a badge).

- Item definitions are authored by the developer for the official AppID, once, and rarely touched. Create them from the Steamworks partner tooling or a script run on the developer's own machine. Nothing about item definitions may live in the repository, a build, a shipped config or a map.
- No product per map, per creator, per asset or per cosmetic bundle that a map declares. No `SetItemPaymentRules` call exists in the project. No creator is ever a payee.
- Define and ship the supporter flair and stop. The catalogue grows when there is a reason to, not in anticipation.

Decline, and say so in the code's own terms rather than a comment: per-cosmetic SKUs, a product per map, loot boxes, keys, randomised drops, timed or consumable cosmetics, paid DLC containing Workshop content (that structure moves contributor payment and tax onto the developer), any paid map, and any product that grants a mode, unit, building, seat, map option, quality-of-life feature, reach, information or matchmaking.

### 2. "Free" has to mean something technical

No map, mode, feature, seat, unit, building or match option may be purchasable or gated on an entitlement, and nothing may be gated on owning the official game's cosmetics either. A player who buys nothing keeps every map, every mode and every seat.

The publish tool does not need a rule for this, because a map can no longer declare a product. If any editor path ever offers a map a price or a revenue split, that is the defect this migration removed.

### 3. Read an entitlement locally, publish it as a cosmetic fact

The player's own inventory is read through `ISteamInventory` in the client, on Steam initialisation and again on the inventory-changed callback.

- The authority never reads a remote inventory. It cannot: `ISteamInventory` only returns the current user's items, and the cross-user path is a player-mediated handshake, not a lookup.
- The choice a player has made (equip the flair or not, which variant) travels with the seat roster like the persona name, not as an ECS component and not as a per-entity property bag.
- Never persist an "owned" flag in Realm's own storage. Re-derive from Steam every session and on the callback, because a refund or chargeback silently removes an item.
- A revoked cosmetic reverts to the default appearance with no dialog, no kick and no change to the match.
- A missing or empty inventory is not an error and not ownership: it is the default appearance.
- Other players' flair is cosmetic and may be reported by that player's own screen. Do not try to verify it: the documented cross-user mechanism (`SerializeResult`, `DeserializeResult`, `CheckResultSteamID`) exists if faking a badge ever becomes a real problem, and it is not worth a handshake per peer for a flair. Write that limit down where the flair is defined, so nobody later mistakes it for a trust signal.

### 4. No `IGameAPI` changes

Do not add entitlement members to `IGameAPI`. With no product a map can sell, no map has a legitimate use for one, and the project rule is that a member belongs in the map API only when a map could reasonably need it. An unused surface is inherited by every map that will ever be written.

If per-map sales are ever revisited, the members are additive — an ownership query by integer id, a revision counter, and a purchase request that routes to the seat's own screen — and they land in that task, not this one.

### 5. No monetization data in the map

Do not add a monetization block to `metadata.json`, do not accept a price or a share list from a creator, and do not add an `EngineGeneration`-style migration for money. A map's only support-related field is the existing optional Discord invite (task 16).

### 6. The Discord invite is the sanctioned support channel

The Discord field already exists and is validated in task 16. This task owns what the creator is told about it:

- The publish dialog carries one translated line. Target wording, to be translated rather than paraphrased: "Steam bans maps that post links to external payment systems (Patreon etc.). Put your donation links on your Discord server instead." One sentence, no citation, no hedge, no support address. It says **map** rather than "Workshop item" or "UGC" because that is the word the creator uses - they are making a map for Realm, and the platform underneath it is not their concern - and because "maps that post links" already covers links in credits, tooltips and loading screens as well as on the page.
- **Do not narrow the line back to the description.** A creator who reads only "no links in the description" will reasonably conclude that a Patreon link in a credits panel, a loading screen or a tooltip is fine. The locked sentence says maps that post links, so it covers the map's own surfaces already; do not restate it as a rule about the Workshop page alone, and do not add a second sentence that enumerates them.
- **Do not advertise a monetization route in the publish dialog.** Per-creator Steam sales were dropped (see Context) and there is no support desk to route a request to after task 22, so a line reading "Steam has an official monetization method, contact Realm support" would promise a path that does not exist and queue requests whose answer is no. If the existence of a Valve-native route needs acknowledging at all, state it as Valve's and disclaim it in one clause - "Selling content through Steam itself is handled by Valve and their own approval process, not by Realm" - with no support address, no form and no promise. Prefer saying nothing.
- Link Valve's Online Conduct rules and the Workshop Legal Agreement beside the line. Do not fabricate a quotation or cite a policy sentence that does not exist, and do not soften the line into "not explicitly prohibited", "Valve has no specific rule" or a bare "at your own risk": that register is what reads as permission, to a creator and to a coding agent alike. If Valve ever publishes guidance that changes the conclusion, update this one line rather than bolting a caveat onto it.
- The same guidance appears in the editor once, beside the Discord field, and in the publish dialog, not in a popup on every publish. Do not repeat it on every screen.
- The generated Workshop description includes the invite and the credits line only. No payment URL, no price, no "buy" and no link list.
- The map details render the invite as one external action (task 16). Nothing in the game opens a payment page, shows a price for a map, or asks the player for money on a creator's behalf.

Do not build a donation surface: no in-game donate button, no Patreon or Ko-fi field, no per-creator support panel, no "support the creator" flow, no revenue or earnings display, and no "you earned" summary for anyone.

### 7. Sell on the official AppID only

Items and purchases are scoped to one AppID, and the demo is unpublished at cutover. The supporter flair and any other official cosmetic are created for the official AppID only; nothing is sold under the demo AppID. If the flow needs exercising before release, use a Steam beta/sandbox configuration without real-money sales and record what was not verified.

### 8. Do not become a payments party

No wallet, cart, checkout, invoice, refund flow, purchase history, revenue ledger, statement, tax handling or payout. If a design would have the developer move money to another person, it is the wrong design and this task's decision is what makes it unnecessary.

## If per-map sales are revisited

Recorded here so a future task starts from the facts rather than rediscovering them. The trigger is a specific creator asking for a specific map, with enough sales volume to clear Valve's payout threshold after the hold period; not a general "the catalog is big enough now".

If it happens, these are mandatory rather than optional:

- **Price comes from a developer-set band**, never from the creator, so nobody prices a map as a full game.
- **Composition is a payee.** A map built from another map's references (`references.json`, task 09) carries that map's content, so it may not monetize unless every referenced author appears in its split. Otherwise the map is a warranty over someone else's work.
- **The entitlement surface enters `IGameAPI` in that task**, with the cosmetic-only limitation documented, and no core path may read it.
- **The product count stays small.** One product per monetized map, created only when asked for, never offered automatically at publish.

## Out of scope

- Per-creator products, revenue splits, `SetItemPaymentRules`, item-definition automation, a publisher service or queue, and any creator payee.
- A Realm store, wallet, currency, cart, checkout, invoice, purchase history or earnings display.
- Donation or payment links in the game, a creator support panel, a "support us" prompt, or a link list.
- A second entitlement system in Realm's own storage, an account to hold purchases, or an ownership cache.
- Paid maps, paid features, paid DLC containing Workshop content, subscriptions, battle passes and expiring cosmetics.
- Policing map content for links, or reporting a creator's item to Valve on the strength of this task. Moderation stays with task 13 and the partner site.

## Completion

- The supporter flair is defined for the official AppID, bought in a sandbox configuration and worn, and a player who owns nothing sees it on someone else.
- A free player has every map, mode and seat, and no screen shows a price for a map or a purchase control for UGC.
- A refund removes the flair at the next session without a dialog, a kick or a change to the match.
- `IGameAPI`, map metadata and the ECS contain nothing about entitlements, prices or revenue.
- No `SetItemPaymentRules`, `UpdateItemDefs` or other publisher-credential call exists in the repository except in developer tooling that never ships.
- The publish dialog states, in translated text, what to keep out of a Workshop item and why, links Valve's rules rather than quoting a ban that does not exist, and the editor shows the same guidance once beside the Discord field.
- Nothing in the client, the editor, the authority, the repository or a map package contains a publisher credential.
- Reported, not assumed: which Steamworks configuration steps were performed on the partner site, and which parts of the purchase flow were verified only in a sandbox.
