# Core Game Rules

Status: Working Draft

Purpose: Define the game's foundational rules and systems independently of specific heroes, soldiers, buildings, maps, or other content.

This document is the parent specification. Child documents (Content Design Guidelines, Content Specifications, Co-op Campaign Design) must operate inside these rules rather than revise them. Anything not settled here is listed explicitly under Deferred Decisions rather than filled in with an assumption.

## 1. Game Identity

The game is a MOBA-inspired real-time strategy game (RTS).

It combines:

- Direct control of a powerful hero.
- An autonomous army whose behaviour is decided by the targeting rules and formations of the soldiers a player's buildings bring.
- Dynamic army composition through doctrine drafting.
- Economic and military development through drafted, unique buildings.
- Strategic competition over territory and resource expansions.
- Hero progression through combat veterancy.

It does not use predefined factions, player-issued production orders, individually controlled soldiers, player-toggled stances, or predefined lanes with scripted minion waves.

### 1.1 Design Principle: Monotonicity

Formulas throughout the game preserve intuitive monotonicity. More investment never unexpectedly produces less total value, and more meaningful participation never unexpectedly makes a team less effective. Where a formula governs a player-facing reward or penalty, it must not create a situation where the intuitively correct decision produces the wrong outcome.

## 2. Terminology

| Term | Meaning |
| :--- | :--- |
| Hero | The player's only directly controlled unit, and their avatar in the match. |
| Nexus | The player's starting building. Identical for every hero and every player, present at match start, never constructed, the hero's revival location, the relic vendor, and the sole elimination condition. |
| Army | The autonomous soldiers produced by the player's buildings. |
| Targeting Rule | The deterministic rule deciding which enemy a soldier type focuses, drawn from a shared enum. |
| Formation | The deterministic position a soldier type holds relative to the hero, drawn from a shared enum. |
| Doctrine | The player's accumulated identity for the match, formed from drafted building packages and the relics purchased along the way. Working term; the concept is a dynamically assembled faction. |
| Building Deck | The shared pool of building options. A chosen building leaves the deck; the unchosen options return; the deck is replenished when it runs out. |
| Building Supply | How many building locations a player may hold. The standard ruleset caps it at eight, and every constructed building costs one point. |
| Unlock | The package a constructed building grants: one soldier type with that building's mana pool, mana regeneration, auto-cast cooldown, capacity contribution, and production time, plus its worker profile: capacity, worker auto-cast cooldown, worker production time, assigned resource, and the permanent buffs it gives the workers it raises. There is no separate research tree or post-construction upgrade path. |
| Combat Veterancy | The progression earned by fighting and killing, which raises hero level and sets the level of every army unit. Relics are not part of veterancy. |
| Bounty | The experience and gold awarded when an enemy unit, enemy hero, enemy building, or neutral monster dies. A bounty never pays wood. |
| Relic | A player-wide upgrade purchased at the Nexus for 100 gold and equipped in one of the hero's six inventory slots. |
| Innate | A hero's permanent player-wide passive. It shares the relic design space, is always active, and is never cast or toggled. |
| Buff Source | A hero active ability, a hero innate, or a relic: the only systems that modify an entity's static metadata during a match. |
| Bulwark | A physical and magic resistance mechanic. Buildings scale it with the attacking force and building count; heroes scale it with nearby soldiers owned by the hero's player. |

## 3. Players and Heroes

### 3.1 Hero Selection

- The lobby presents hero selection as a single dropdown labelled Race / Faction, with Random as an option. The label is the lobby's affordance, not a rules concept: no race or faction exists, and no selection grants a distinct roster.
- Each player enters the match with exactly one hero, either chosen directly or resolved from Random.
- The Nexus is identical for every hero and every player, so hero choice never changes a player's starting structure.
- The hero is the player's only directly controlled unit, and serves as both avatar and commander.
- Every hero has Bulwark, like the Nexus, granting physical and magic resistance that scales with the number of that player's own soldiers nearby. Allied soldiers do not count.

### 3.2 Hero Activities

- The hero gathers resources, constructs buildings, repairs buildings, and fights.
- Active gathering is faster than passive building-based gathering but requires player attention and mechanical input.
- A hero can perform only one major non-combat activity at a time. Starting another major activity cancels or pauses the previous one.
- Construction and repair are visible, timed commitments, and they are orders: the hero raises or mends only the building the player right-clicked for it. Standing near a site or a ruin, idle or otherwise, does nothing, and an order for somewhere else stops the work where it stands. While the hero is working at a site or gathering, it plays its labor animation.

### 3.3 Hero Abilities and Progression

- Heroes and soldiers begin the match with every ability unlocked. Abilities are never acquired and never consume skill points.
- Every hero has three active combat abilities and one innate passive. The Q, W, and E slots are input positions, not power tiers; there is no ultimate category, and all three actives and the innate are available from the first moment of the match.
- An innate is a permanent player-wide effect that changes a fundamental rule of the player's army, buildings, or economy, in the same design space as a relic. It is always active, is never cast, and is not a fourth ability slot.
- The three actives may interact with one another directly. A combo inside one hero's kit is allowed because the whole kit is one piece of content and creates no cross-content dependency.
- Active ability effectiveness scales from raw stats, so a level increase raises active output automatically with no separate allocation step. An innate may be flat like a relic or scale from stats, but it never requires a separate allocation step.
- Every enemy unit, enemy hero, enemy building, and neutral monster awards a bounty of experience and gold when it dies. A bounty never pays wood.
- A neutral monster's bounty scales with the monster's level.
- Hero and building bounties are larger than soldier bounties.
- The experience portion of a bounty advances combat veterancy. The gold portion is currency and is awarded in addition to gold gathered from nodes.
- A weaker victim awards less experience, so efficient progression requires engaging progressively stronger opposition.
- A hero level grants raw stats to the hero and sets the level of every army unit. Every army unit always matches the hero's level. Each soldier type has its own per-level scaling values, so the same level produces different growth per type. This is the only effect of veterancy on the army.
- Relics are purchased from the Nexus for 100 gold each, independent of hero level (section 7).
- Hero progression is earned through battlefield activity, not through passive match time. Bounties are the reward for that activity: experience advances the hero, and gold rewards successful aggression with resources.

### 3.4 Hero Death and Revival

- When the hero dies, its entire army is removed and every production queue is cleared.
- The hero revives at its nexus after a revival timer.
- The revival timer increases with successive deaths.
- On revival, every building's production mana is refilled to its maximum and its auto-cast production resumes automatically.
- A hero whose nexus has been destroyed still revives at that destroyed nexus.

### 3.5 Hero Movement

- Heroes pass through allied and enemy soldiers without being body-blocked by them.
- Heroes pass through allied buildings.
- Heroes pass through destroyed enemy buildings and enemy buildings still in their initial construction silhouette.
- Heroes cannot pass through damaged or functional enemy buildings, or through other heroes.
- Soldiers never ghost any collision source, including one another.

## 4. Armies and Combat

### 4.1 Army Control

- Armies are autonomous. Players never individually select soldiers, issue them orders, or toggle their behaviour.
- The player influences the army in exactly two ways: by drafting which building packages exist (section 6), and by controlling where the hero stands, where the hero moves, and which enemy the hero attacks. Every soldier derives its target and its position from those two inputs.
- Each soldier type carries two static, deterministic rulesets declared when the type is authored and never chosen by the player: one targeting rule (section 4.2) and one formation (section 4.3). Both come from small shared enums, so a behaviour is learned once and applies unchanged to every soldier that uses it.
- Soldiers maintain formations and engage according to rules a player can learn and anticipate. No army behaviour depends on hidden state.
- The army stays in close proximity to the hero through its formation behavior, which is why hero death removes it.

### 4.2 Targeting Rules

| Targeting rule | Behavior |
| :--- | :--- |
| Standard | Focuses the hero's most recent damage target, so the army concentrates fire on what the hero is fighting. A debounce prevents rapid target churn, and the soldier falls back to the nearest valid enemy in range when the primary target leaves its own range. This is the default rule for most soldiers. |
| Tank-Shredder | Always attacks the highest-health enemy within range. |
| Assassin | Always attacks the lowest-health enemy within range. |

Rules:

- A targeting rule selects among valid enemies and never leaves a soldier idle while an enemy is in range.
- Targeting is deterministic. The same positions, health values, and hero damage history always produce the same target.
- Standard's debounce is what makes focus fire legible: the army commits to the hero's target rather than re-evaluating on every attack.

### 4.3 Formations

| Formation | Position relative to the hero |
| :--- | :--- |
| Front | Ahead of the hero, on the hero's facing. |
| Flank | To both sides of the hero. |
| Back | Behind the hero. |

Rules:

- A formation is a slot, not a script. Soldiers occupy it through ordinary pathing and collision.
- Army composition therefore decides army coverage. An army whose packages grant only Flank soldiers holds nothing in Front or Back, and leaves the hero exposed from those directions.
- Soldiers are tethered to the hero within a leash distance and are drawn back to their formation when the hero moves. They still follow pathing and collision rules while tethered.
- Soldiers do not ghost any collision source. They body-block one another, especially while compressing through sharp turns.
- Heroes ghost through soldiers, and through only the building states listed in section 3.5. They never ghost through a functional enemy building or through another hero.
- Formation changes are debounced against hero movement and turning. A soldier repositions through a standard movement command rather than a teleport, so a retreat cannot instantly swap archers with tanks, and the army is briefly disordered while it reshapes.
- A soldier that has begun attacking prioritises continued attacking. It stops only when no enemy remains in range, or when the hero has moved far enough away that its target is no longer reachable from formation.

### 4.4 Soldier Characteristics

- Each soldier type has fixed characteristics, including its targeting rule and formation.
- Individual soldiers cannot be upgraded or modified by the player. A given soldier type is always the same unit at a given hero level.
- Every soldier matches the hero's level, so army strength rises with veterancy as well as through quantity, capacity, production rate, and formation coverage. There are no per-unit upgrades, promotions, or equipment.
- Because soldiers carry no per-unit customization, an enemy army composition is parseable at a glance; army-wide buffs are listed in the stat panel.
- Army-wide effects come from buff sources (section 7.2). They are listed in the stat panel of every affected unit and do not change what a soldier type is.

### 4.5 Auto-Cast Abilities

- Soldier abilities are auto-cast, each with its own deterministic rule drawn from a shared enum of auto-cast rules. Soldiers never receive a player order.
- A typical auto-cast is an area-of-effect cast onto the entire army and hero simultaneously, triggered on cooldown when its criteria are met: sufficient mana, an available target, and a valid target inside cast range. A heal, for example, requires an allied unit below 100% health.
- Because every unit has its own stats, one cast resolves differently on each unit. A stun reduced by high tenacity or ignored by spell immunity is the expected outcome, not a special case.
- Abilities are deliberately simple, both to keep cognitive load low and to keep the engine implementation tractable. More creative abilities can be added in later patches without changing these rules.

### 4.6 Combat Rules

- Friendly fire is disabled between members of the same declared team. Players cannot damage or destroy their own or their allies' units or buildings.
- Players on different teams can damage one another, even if they have an undeclared understanding or temporary cooperation.
- Autonomous armies engage nearby enemies according to their targeting rules, so undeclared cooperating players may accidentally attack each other and must actively manage positioning to avoid it.
- Damage between undeclared cooperating players counts in the anti-kingmaker participation calculation exactly like any other damage.

## 5. Production and Army Progression

### 5.1 Automatic Production

- There is no summon ability and no player command that starts, stops, or reorders production. Soldier and worker production are auto-cast abilities on the building.
- A building's production is fed by its own mana pool, not by unit charges. The pool, its regeneration and the ability's cooldown are authored on the building.
- Every soldier-granting building carries two production abilities: one raises its soldier type and one raises its worker crew. Both draw from the same pool, so a building raising workers has less mana for soldiers. That contention is a deliberate property of the building, not a defect.
- An ability casts when it is auto-casting, its cooldown is ready, its mana cost is covered, and the type it raises has room. A building spends no mana on a unit it cannot field.
- A cast pays the ability's mana, reserves the type's slot and starts the ability's cooldown. The unit is raised at the building and joins the army under the normal pathing and collision rules; it never spawns on the hero.
- A raised soldier walks from its building to the hero. The walk is readable to both players and keeps the building's location meaningful after construction.
- When no slot is open, mana accumulates to the pool's maximum and waits. Capacity caps are still authored per building and remain a map-side gate where the generic mana pool cannot express them.
- A newly completed building begins with one production worth of mana, so its first cast does not wait on regeneration. Because both abilities share the pool, whichever casts first claims it and the other waits; that contention is intended.
- A destroyed building stops regenerating mana and stops casting. Its stored mana and queue are retained, and both resume when the building is repaired.
- Auto-cast is on when the building is completed. The player may right-click either production card to switch that ability off or back on. Every card whose ability supports auto-cast carries the dashed border, whether the ability is on or off, and the border marches only while that ability is auto-casting; a still border therefore reads as an ability that could cast itself but is currently switched off. Toggling is an order that reaches the authority, not a local UI state.

### 5.2 Building Production Values

- Each building owns the production values for the soldier type it grants: its mana pool, mana regeneration, auto-cast cooldown, capacity contribution, and production time.
- A soldier type's total capacity is the sum of the capacity contributions of the player's owned buildings that grant it.
- Soldier types have hard caps that cannot be surpassed.
- When a soldier type has no open slot, its buildings retain their mana rather than spending it.
- Each copy of a building tracks its mana, cooldown, and queue independently. A second copy contributes its full values rather than sharing the first copy's pool, so two copies double total mana capacity, regeneration throughput, and production throughput.
- Relics, hero actives, and hero innates can modify a building's production values, including its mana regeneration and cooldown reduction. They apply to each building separately, so duplicate copies are affected independently.
- A building's production values are fixed by its authored profile and changed only by those buff sources.
- A building's production values do not improve a soldier's combat statistics. The hero's level is the only global source of stat growth for every soldier.
- Soldier types are not tiered. Every soldier is set to the hero's level, so a package drafted early remains valid late; balance is applied to the building package as a whole.
- Army progression combines veterancy, which raises every soldier's stats, with capacity, mana throughput, cooldown, and production time, which decide how many soldiers are fielded and how quickly.

### 5.3 Worker Production

- A building raises workers through the same mana-fed auto-cast model as soldiers, from its own worker ability: worker capacity, worker auto-cast cooldown, and worker production time. Both abilities draw on the building's single mana pool.
- A worker is raised when the worker ability casts and a worker slot is open. It spawns at its building and walks to its assigned resource. The mana it spends is no longer available to the soldier ability, so the two lines compete for the same resource.
- A newly completed building auto-casts its crew from the first moment it is functional, from the same opening mana that its soldier ability can claim.
- A worker profile is part of the building package, so a package may be strong in soldiers and weak in workers, or the reverse.
- Worker capacity and production values are authored per building and are affected by the same lobby multipliers as soldier capacity.
- Worker production values do not state an income rate. Income emerges from what the workers do (section 9.3).

## 6. Drafting

### 6.1 No Predefined Factions

- Players do not select a predefined faction or race. The lobby's Race / Faction dropdown selects a hero, and nothing else.
- No hero choice grants a distinct roster of soldiers or buildings.
- Each player's nexus is identical regardless of hero (section 3.1).
- The rest of the player's military and economic identity is assembled during the match through drafting.

### 6.2 The Shared Building Deck

- Draftable content consists of building packages. A package is a building, the soldier type it grants, that building's mana pool, mana regeneration, auto-cast cooldown, capacity contribution, production time, economy, and construction time.
- There is one shared deck of buildings. There are no mutually exclusive unit pools; every package competes with every other package.
- Every constructed building package in the content set is an option in the deck.
- Each construction draws up to three options from the buildings still remaining in the deck that the player does not already own. In a lobby that allows duplicate copies, an owned building stays eligible up to the configured limit. The draw is reserved for that player until the choice is locked.
- A player selects one option. That building leaves the deck and is unavailable to other players until the deck is replenished.
- The options not selected return to the deck.
- The next construction draws a fresh set of up to three options. No option carries over between purchases.
- When the deck runs out, it is restored and reshuffled. This permits duplicate buildings across players when the player count is large relative to the deck, but the draw still excludes buildings the player already owns at its duplicate limit.
- If fewer than three distinct options remain, the draw offers only the options that exist. A short draw is acceptable and never delays construction.
- Every constructed building costs 100 wood and occupies one point of building supply.
- Players never browse an exhaustive, ever-growing catalog of content. They choose among a small draw.

### 6.3 Drafting Through Construction

The draft is expressed through building construction, so that the decision and its commitment are the same act.

1. **Accumulate the cost.** The player gathers 100 wood.
2. **Begin construction.** The player presses Begin Construction and places the site with the standard construction placement: the blueprint's own silhouette follows the cursor, turning red over ground the match would refuse, instead of a targeting cross-hair. The 100 wood is charged and the blueprint marks the site; no building exists yet, and the hero's own grid stops offering Begin Construction while a draw is open.
3. **Draw up to three options.** Up to three buildings still in the shared deck and not already owned by the player are drawn and reserved for this player, and the blueprint's command grid shows exactly those packages; if the lobby allows duplicates, owned buildings are eligible up to the configured limit. Fewer than three is correct when fewer exist.
4. **Choose one.** The drawn options are visible immediately and the player picks one from the blueprint's command grid. That building leaves the deck, the blueprint gives way to the site as an unidentified silhouette, and the others return to the shared deck.
5. **Raise the site.** The site is invulnerable and not operational. It rises only while the hero is ordered to work at it, equivalent to repairing the building from 0 to 100% health at the package's construction time, and an order for somewhere else stops the work where it stands. Construction time is the decision window that began with the draw; no separate draft timer exists.
6. **Establish the building.** On completion the site becomes the player's building at that location, becomes attackable, and begins its production.
7. **No undrafted completion.** A site exists only once an option is locked, so construction can never finish without a chosen package. A draw left unanswered leaves its blueprint and its reserved wood until the player answers it or abandons it.

Consequences of this structure:

- The chosen option is committed before construction completes, so a player cannot wait to see how the battlefield develops before finalizing.
- Drawing reserves options rather than consuming them; only the selected building leaves the deck.
- The next construction draws a fresh set. A previous draw is never reused.
- The decision window is the open draw itself: the choice is made before any construction time is spent, and the construction that follows measures the commitment the player already made.
- If construction is canceled before a choice is locked, its reserved options return to the deck and its 100 wood is refunded. No building is created and no draft is spent.
- Once a choice is locked the construction is committed: the wood is not refunded, the package does not return to the deck, and the site waits invulnerable until a hero raises it. Abandoning is offered only while the draw is open.
- If no option exists to offer, the construction is canceled before a draw and its 100 wood is refunded.

### 6.4 Draft Competition

- Wood income determines how quickly a player can begin the next construction.
- All players draw from one shared deck, so a building taken by one player is unavailable to the others until the deck is replenished.
- Players therefore compete to take particular building packages first.
- Economic efficiency can fund more drafts sooner, but it does not pre-reserve an option or a building; reservation begins only when a construction starts.

### 6.5 Package Balance

- Every building package is balanced against every other building package in the shared deck.
- The balance unit is the complete package: soldier, capacity, mana pool, mana regeneration, auto-cast cooldown, production time, economy, and construction time together. A weaker soldier can carry a stronger economy, and a stronger soldier can carry a weaker economy.
- Soldier types are not tiered. Every soldier scales with the hero's level, so a package drafted early remains relevant late.
- Cross-package relationships are expressed through stated characteristics and buff sources, never through specific building or unit-name combinations.
- Drafting one particular package must not make another specific package the automatically correct follow-up choice.
- An option is never guaranteed to appear. Theorycrafting is therefore partial by design: a player plans around stats, formations, and the draw, then adjusts to the situation. A lobby that keeps drafted buildings in the deck (section 14) instead lets a player plan a complete combination.

## 7. Relics and Buff Sources

### 7.1 Relics

- Player-wide upgrades are equipped as relics in the hero's six inventory slots.
- A relic carries the impact of a keystone rune or a soul: it changes how the player's army, buildings, or economy performs, rather than adding a new unit or a new verb.
- Relics are purchased at the player's Nexus for 100 gold each. The player chooses from the relic pool; a relic is never drawn randomly and never appears in a building draft.
- A relic purchase does not require a hero level. A hero can hold at most six relics, one per inventory slot; reaching that cap ends relic progression.
- Relic scope is player-wide by construction. Spiked Barricades, for example, causes all owned buildings to return damage to their attackers, while Sigil of the Wall raises the max health of Front-formation soldiers.
- Relics use the item and equipment system, and equipped relics are permanently inspectable on the hero by any player.

### 7.2 Buff Sources

- The hero kit, relics, and a building's authored worker buffs modify an entity's static metadata during a match. The hero kit is its three active abilities plus its innate; the worker buffs are applied when a building raises a worker; there is no further modifier layer.
- Buffs typically apply to the entire army and hero simultaneously rather than to a selected unit.
- The same buff can resolve differently on each unit, because each unit has its own stats. Tenacity, spell immunity, armor, and health are read per unit.
- A buff is normally accompanied by a visual effect on every affected unit. During development some buffs may lack their visual; the unit stat panel is authoritative and always lists active buffs, so a missing effect never hides information.

### 7.3 Content Independence

Content is self-contained. No unit, hero, building, relic, or ability may refer to a specifically named other piece of content to function or to gain value, except for the other abilities in the same hero's kit.

The reasons:

- Draft safety. A random draft cannot guarantee that a player owns any particular other option, so an option must never depend on, or target, an entity the player may not own.
- Balance isolation. Named cross-references create a dependency web: changing or adding one option forces unrelated options to be re-evaluated, and a balance change can ripple outward without a natural boundary.
- Legibility. A player must be able to reason about an option from its own tooltip and from the match in front of them.

Cross-content effects use stated characteristics, shared enums, and buff sources rather than named pairings. Theorycrafting must therefore remain readable from tooltips; content whose value only appears after simulation or a third-party guide is badly authored, not deep.

A hero's own three actives and innate are one piece of content, so they may interact with each other directly. Cross-ability combos inside a kit are allowed and encouraged; they create no dependency web because no other hero, unit, building, or relic is required.

## 8. Buildings

### 8.1 Buildings as Drafted Commitments

- Buildings are drafted and unique to each player. A player drafts and constructs each building; by default a player never holds two copies of the same building.
- Every constructed building costs 100 wood and takes its authored construction time. There is no other building cost.
- A building is the unlock: it grants exactly one soldier type and that building's production profile for the type, plus its own income. There is no separate upgrade layer, research tree, research menu, or post-construction upgrade path. Selecting a constructed building only inspects it; repair remains a hero action.
- Because every constructed building costs the same 100 wood, the package is the balance unit: a weaker soldier can be paired with a stronger economy, and a stronger soldier with a weaker economy.
- Buildings serve both economic and military purposes, and they differ in that tradeoff: a building may provide a stronger economy and weaker military value, or the reverse.
- Because buildings are drafted and tied to military construction, observing an opponent's base reveals that opponent's available army capabilities.
- Buildings may be placed anywhere the map terrain is marked buildable, not only at fixed sites. A player may build beside a resource concentration, inside a chokepoint, or next to an opponent's building.
- Placement is therefore a strategic act: a building can protect a resource, close a chokepoint, or contest ground.
- Enemies may build adjacent to one another. Proximity is a decision, never a rule violation.
- Building locations are permanent once established. A building can be deactivated (destroyed) but is never relocated or removed through ordinary play.
- A building may gather more than one resource type.

### 8.2 Building Supply

- Every constructed building costs one point of building supply. The Nexus is free and does not count.
- The standard ruleset caps a player at eight building locations. A placement that would exceed the cap is refused before its wood is charged.
- A player cannot exceed that supply through expansion.
- Resource locations are not owned. Multiple players may build next to the same concentrated resource.
- An opponent can deny an expansion's income by destroying its gathering building or by exhausting nearby nodes so its workers must travel farther. The denied player can repair or rebuild, and the attacker can destroy it again.
- A player cannot hold more expansions than its own supply allows.

### 8.3 Building Lifecycle

| State | Health | Vulnerable to attack | Operational |
| :--- | :--- | :--- | :--- |
| Under construction | Below 100% | No | No |
| Functional | 100% | Yes | Yes |
| Damaged | Above 0%, below 100% | Yes | Yes |
| Destroyed | 0% | No | No |

Rules:

- Building damage is not permanent and is always repairable, even from zero health.
- Repair costs no resources. Its cost is the repairing hero's time and commitment.
- Any allied hero can repair an allied building.
- A building at zero health enters the destroyed state and stays there until restored to 100% health. The destroyed status does not reset at partial repair.
- A destroyed building is invulnerable while dormant and cannot be attacked again until fully repaired.
- A destroyed building generates no income, regenerates no mana, and produces no soldiers or workers. Its stored mana and queue are retained until it is repaired, and its capacity contribution remains part of the type's total cap so destroying it never pushes a surviving army over capacity.
- New construction uses the same silhouette presentation as a destroyed building, without the destruction visual effects, and is invulnerable until complete.
- Repair is done as percent of max health per second, so the hero can repair two buildings from 0 to 100% in exactly the same amount of time. The repair rate can be different per hero or augmented by buff sources.
- The nexus is an explicit exception: once destroyed it cannot be repaired.

### 8.4 Lobby-Configurable Building Multiplicity

- The default lobby configuration prevents a player from obtaining a second copy of a building.
- A second copy has its own production profile, mana pool, regeneration, cooldown, and queue. It contributes its full income and production values rather than sharing the first copy's state, so two copies double income, total mana capacity, capacity contribution, regeneration throughput, and production throughput. Each copy keeps its authored per-building values; only the totals across copies double.
- The default configuration is a setting of the formula, not a separate ruleset.

## 9. Economy

### 9.1 Resource Types

| Resource | Acquired from | Role |
| :--- | :--- | :--- |
| Veterancy | Experience from bounties paid by enemy units, enemy heroes, enemy buildings, and neutral monsters | Advances hero level, which grants raw stats to the hero and sets every army unit's level. It is progression, never a currency. |
| Gold | Finite gold mines, attacked by the hero or by building workers, plus the gold portion of bounties | Spent at the Nexus on relics, at 100 gold per relic. |
| Wood | Finite tree lines, attacked by the hero or by building workers | Spent on building construction, at 100 wood per building. Bounties never pay wood. |

Rules:

- A universal single-resource economy is not used.
- Gold funds relics, wood funds buildings, and veterancy funds hero levels. Each resource answers a different question.
- Gold has two sources: finite nodes and bounties. Bounty gold is created by combat rather than drawn from a node. Wood has no bounty source.
- Neutral bounties scale with the monster's level. Hero and building bounties are larger than soldier bounties.
- Creeps are not a gatherable resource. They are concentrated neutral monsters in key locations whose bounty pays experience and gold to whoever kills them (section 10.2).
- A gathered piece is an inventory item. Wood and gold are item templates with a per-slot stack limit, so what a worker carries is visible in its inventory and is spent at the drop-off as items rather than as an abstract number.
- Item templates state their stack limit: wood holds one piece per slot and gold holds six. Additional pieces of a stack-limited item open the next inventory slot instead of overflowing the first.
- Every unit may fill six inventory slots. Equipment and gathered pieces share those slots, so a hero carrying relics has that many fewer slots left to haul with, and a full inventory is six wood or thirty-six gold. The slot count is a map rule (`InventorySlots`), not a property of an item or a unit.
- Gathering is never instantaneous and never takes a whole stack in one swing: a unit fills its inventory over several hits, and a unit whose attack is stronger or whose penetration is higher fills it in fewer of them.

### 9.2 Active Economy

- Heroes gather gold and wood directly from resource nodes, consuming the node's remaining capacity.
- A hero gathers only when the player gives the order, either through the gather ability or by right-clicking a node with an order that applies to it. The hero never seeks a node on its own when idle; that automatic behaviour belongs to workers, which carry it through a tag. This keeps one gathering implementation for both unit kinds while leaving the hero's attention as a real cost.
- Active gathering is faster than passive gathering but requires the player's attention and mechanical input.
- The economic advantage of active gathering comes from choosing when to invest hero time in it, not from requiring uninterrupted repetitive clicking.
- Hero gathering's opportunity cost is the hero's time away from fighting, contesting, and constructing.

### 9.3 Building Workers

- Buildings raise workers through their worker production profile (section 5.3) instead of gathering through an abstract income rate. A worker is a unit on the board with health, position, an attack, and an inventory.
- Every building raises the same worker template. The template has per-level attribute scaling, so a worker's level is what separates a stronger worker from a weaker one.
- A worker's level is the construction order of the building that raised it: the first building raises level 1 workers, the second raises level 2 workers, and so on. A later building therefore raises stronger workers without any separate upgrade.
- A building applies its authored permanent buffs to every worker it raises. The buffs use the ordinary stat pipeline, are listed on the worker's stat panel, and are the only per-building difference between workers raised from the same template. Buffs typically raise the worker's damage (gathering rate), durability, or movement speed.
- Workers are attackable units, not invulnerable ones. They cannot be given player orders. When a worker is killed, its building raises a replacement through its worker ability when its mana and cooldown allow.
- A building assigns each worker the resource it gathers when it raises it, as a tag on the worker (`worker_assignment_gold` or `worker_assignment_wood`). The worker AI reads that tag, so one worker template serves every building and a package decides only which node type its crew works.
- A worker plays its labor animation while it attacks a node or works at a construction site. The animation is presentation of the same gather or build order the simulation is carrying out, not a separate state.
- A worker attacks the nearest node of the resource its building assigns to it, using its own attack. The node's own yield (`YieldPerDamage`) turns the damage that gets through its resistances into pieces: a tree or a gold mine gives up one piece per four points, so a worker's swing is worth about one piece, the pieces are drawn off the node's health one point each, and the pieces the worker cannot carry stay on the node instead of being taken and discarded.
- When the worker cannot hold another piece, it walks to the nearest building that accepts a drop-off, consumes its carried items there, adds their number to the player's stockpile, and is sent back out to its node. An idle worker with room in its inventory is sent to the nearest node of its assigned resource.
- When its node is exhausted, the worker moves to the next nearest reachable node of the same resource. If none is reachable, it stands idle rather than producing from nothing.
- A node has no worker cap. Any number of buildings may gather from it, and all of them draw from the same capacity.
- Owned workers body-block one another, but ghost through allied and enemy workers.
- A building's income is therefore what its workers physically take, carry, and deliver over time. It is decided by their damage, attack speed, level, buffs and travel distance, not by a stated rate. Income falls with travel distance because the walk is part of the trip.
- Workers are a third way to deny an expansion, alongside destroying its building and exhausting its node: killing workers stops the deliveries until their building replaces them.

### 9.4 Resource Nodes

- Gatherable gold and wood are finite. Each gold mine and tree line is a resource node with a finite capacity, and that capacity is its health.
- Every gatherable node of one kind is an instance of one template slug: all trees share a tree template and all gold mines share a gold template. Levels are expressed through that template's per-level scaling of capacity and resistances instead of through separate templates per level.
- A node's template states its armor and magic resistance as well as its capacity, so a higher-level node can be both larger and harder to take from.
- Gathering is combat: the worker's damage type, its armor and magic penetration, its critical strikes, and its attack speed all apply, and the node's resistances reduce what lands. The damage that lands is converted into pieces of the node's item by the node's yield, and each piece removes one point of health, so resistance and yield together decide how many swings a node costs. The ruleset's level 1 trees and gold mines resist enough of a worker's swing that it is worth one piece.
- A node at zero health is destroyed, leaves the gathering pool, and yields nothing further. The client removes the placed prop the node was drawn as. It only returns to service if its map data gives it a growth rate that restores capacity.
- A resource node is selectable by clicking it or by dragging a selection box that catches it. Its panel shows the resource it yields as one reading — its remaining resources, which are its health, against the capacity it was placed with — together with its armor and magic resistance, so a player can judge what a node is worth and how hard it will be to work before committing workers to it. The reading falls while the node is being worked.
- Gold also enters the economy through kill bounties. Bounty gold is created by combat rather than drawn from a node, so node depletion limits gathering but not bounty income. Wood has no bounty source.
- Gathering is not blocked by other players. Any number of players may attack the same node, and their hits draw from the same finite health.
- Exhaustion is a second form of denial. Destroying a gathering building stops its deliveries; exhausting the node it relies on forces its workers onto a farther node, which can reduce or effectively stop its income, and can deny an opponent's expansion or active gathering.

### 9.5 Economic Expansion

- Players expand economic reach by holding permanent building locations at resource concentrations.
- Resources are concentrated in particular parts of the map rather than spread evenly, and some concentrations sit next to terrain worth defending, such as chokepoints. Holding ground and earning income are therefore the same decision.
- Multiple players may build next to the same concentrated resource. A resource location is not owned, and no player can claim exclusive access to it.
- Buildings cannot be moved. A player's building supply, not proximity to a resource, determines how many expansions that player can hold.
- An expansion can always be temporarily denied by destroying its gathering building. The owner can repair or rebuild, and the attacker can destroy it again if it is rebuilt.
- Multiple buildings gathering from the same node deplete its capacity faster than a single building would.
- Building income falls with worker travel distance to the nearest matching node. When a concentrated node is exhausted, pathfinding seeks the next nearest matching node; if that node is far, the walk can reduce income to practical uselessness.
- Denial by depletion is permanent for that node unless it regrows, though the player who depletes it spends time and exposure gathering it. In both destruction and depletion, the denying player does not capture the location or the building.
- Expansion competes with military investment for the same resources, so economic development delays some military development.

### 9.6 Worker Levels and Buffs

- A worker's level is fixed by the order of the building that raised it and never changes for that worker. A building's workers all share its construction order, so replacing a killed worker raises another at the same level.
- A building's worker buffs are permanent for the worker's lifetime, applied when it is raised, and use the same stat pipeline as every other modifier. They are listed on the worker's stat panel like any other active buff.
- Because the buffs and the level are visible on the worker, a player reasons about a building's gathering rate from the worker's own stats and buffs rather than from an advertised number. The building's draft tooltip states the buffs its workers receive, not an estimated rate.
- Worker buffs are authored content, not a separate upgrade layer. A building cannot change its worker buffs after construction, and no buff is applied by a research step.
- Since income is physical, there is no income tax. The cadence of major purchases is bounded by worker capacity, the building's mana and worker cooldown, the walk, and the node's finite health rather than by a tax curve.

### 9.7 Resources and Allies

- Resources cannot be sent, traded, or shared between allied players.
- Each player maintains an independent economy.

## 10. Map, Roaming, and Information

### 10.1 Map-Driven Strategy

- The game does not use predefined lanes or scripted minion waves to establish the match's strategic structure.
- Terrain, resource allocation, and choke points shape strategic movement and engagement opportunities.
- Buildable terrain determines where buildings may be placed, so map design also dictates which ground can be fortified.
- The map itself creates areas of economic and military importance, so map design supplies the macro rhythm that lanes and wave management provide in a MOBA.

### 10.2 Roaming and Creep Clearing

- Neutral objectives are creeps and bosses scattered across the map.
- They are fully contestable: their bounty goes to a single team, so claiming one denies it to every other team.
- A neutral bounty pays experience and gold, scaled by the monster's level, and never wood.
- Some neutral objectives respawn periodically, so the same location is worth contesting more than once.
- Every player is effectively a jungler. There is no dedicated jungler role, and no player is assigned to neutral objectives.
- Players roam the map to find opportunities to catch unprepared opponents, contest neutral objectives, and deny expansions.
- Clearing creeps awards their experience and gold bounty. The experience raises hero level, which raises hero and army stats.
- PvE activity is part of the competitive economy rather than a separate sideline.

### 10.3 Information

- Competitive balance assumes full vision and full information. Shroud is an optional lobby mode (section 14); the standard rules and the balance model assume it is off. Shroud is the tactical hiding of knowledge, not the environmental fog that a weather preset draws, and the rules only ever refer to the former.
- Scouting is optional flavor rather than a strategically mandatory activity.
- Strategic advantage comes from making better decisions with visible information, not from hidden army compositions.
- Building draft reservation is visible. Before a choice is locked, the three reserved options are stated on the blueprint's command grid, so the reservation is read the same way any other unit's cards are read and an opponent can act on it. Once the choice is locked, the grid empties, the unchosen options return to the deck and the site reveals only the locked option, even while construction continues.
- A player may act on that information, for example by delaying their own construction when a desired option is reserved by an opponent and may return to the deck.
- Counter information is exhaustive: clicking any unit, allied or enemy, shows its stats, targeting rule, formation, and active buffs. Selecting a unit the player does not own hides or disables the command card.
- The readings the ruleset keeps itself reach every player through the unit's own panel rather than through a hidden script state: a soldier-granting building states the soldier it unlocks and both auto-cast cooldowns on its production cards, its soldiers and workers maintained against the capacity it contributes on its stat sheet, and the buffs its worker crew carries on the workers themselves.
- Clicking a completed building, allied or enemy, shows its current health and its mana, read from the health and mana bars under its portrait. A non-Nexus building also shows the soldier type it unlocks, the soldiers and workers it maintains against its capacity contributions on its stat sheet, both auto-cast cooldowns, and the worker buffs it provides. A building fields its units by casting, not by queueing, so it has no production box and no production queue to show; the production box belongs to content that trains on a timer. Completed buildings have no command card: your own Nexus opens the relic vendor, and every other completed building is inspection-only.
- Clicking a resource node, allied or enemy, shows its remaining capacity, health, and resistances; it never opens a command card.
- The player's stat sheet groups readings into General, Attributes, Offense and Defense, in that order. General holds progression and economy readings (level, experience, life steal, cooldown reduction, movement speed, damage type), Attributes holds the six core attributes, Offense holds attack damage, attack range, attack speed, critical hit, armor and magic penetration and spell power, and Defense holds armor, magic resistance, evasion and tenacity.

### 10.4 Instructions Menu

- The map publishes its player-facing instructions through `IGameAPI.AddInstructionsSection`. Each section has a title and a body written from the same numbers the script plays by, and the in-game instructions menu lists them in the order they were added.
- Instructions are part of the map package, not engine UI copy, so a ruleset can explain its own production, supply, gathering and victory rules without the engine hard-coding any of them.
- The menu is read-only and available for the whole match.

## 11. Victory and Defeat

### 11.1 The Nexus

- Each player has a starting building associated with that player's hero. Every nexus is identical in structure and statistics regardless of hero.
- The nexus is present from the beginning of the match and is never constructed by the player.
- The nexus is the hero's revival location.
- The nexus cannot be reconstructed or repaired once destroyed.
- The nexus is the equivalent of a MOBA nexus: a single, simple, permanent elimination target.

The victory condition is deliberately singular so that balance adjustments can be made precisely, without cascading side effects across several competing win conditions.

### 11.2 Victory Condition

- Destroying an opposing team's nexus is the sole elimination condition.
- A team remains active as long as at least one allied nexus survives.
- A player whose own nexus is destroyed remains active while an allied nexus survives. That player still revives at the destroyed nexus and can repair their other buildings to restore economy and army.
- A player becomes a spectator when their nexus is destroyed and no allied nexus remains.
- Individual elimination is therefore distinct from team elimination.

### 11.3 Ranking

- Teams are ranked in reverse order of their nexus destruction. The last surviving team places first.
- In a two-team match, first place is labeled Winner and second place Loser.
- Near-simultaneous destruction is resolved by whichever destruction occurs first.
- If two opposing teams' nexuses are destroyed on the same simulation tick, their relative ranking is intentionally undefined and non-deterministic. No tie-break is implemented, because the choice is irrelevant.

## 12. Bulwark

Bulwark is a resistance mechanic that reduces both physical and magic damage. It makes attacking a defended target inefficient for small forces without making that target unbreakable.

### 12.1 Building Bulwark

- Bulwark scales with the size of the attacking force. A small force is inefficient against a building rather than blocked by it, and breaking a building requires a substantial late-game army rather than a small early force.
- Bulwark also scales with building count, so each successive building reinforces the other buildings of the team.
- Destroying other buildings first therefore makes the nexus easier to break, but is not strictly required.
- All buildings have Bulwark, scaled per building, so it is more or less effective depending on the building type.
- Bulwark groups by team when counting: positively (resistance) for all allied buildings and negatively (penetration) for all nearby enemy attackers on the same team. Allied structures and forces therefore contribute to collective defense. Two separate enemy teams attacking one player each have their own Bulwark penetration calculations, which do not benefit each other unless allied.

### 12.2 Hero Bulwark

- Every hero has Bulwark, a resistance layer like the Nexus. It grants physical and magic resistance that scales with the number of that player's own soldiers near the hero.
- Allied soldiers do not count. Only soldiers owned by the hero's player contribute.
- Resistance rises as more owned soldiers are nearby and falls as they die or leave the radius. The exact radius and curve are tuning values.
- Attacking the hero through its army is inefficient, so the efficient target is the soldiers. Falling resistance is the visible signal to retreat.
- Because heroes pass through soldiers, the army providing this resistance never blocks the hero's retreat.

## 13. Multiplayer

### 13.1 Alliances

- Alliances are established in the lobby and cannot be changed during a match.
- Declared allies are treated as one allied group for all team-level rules.

### 13.2 Bounty Distribution

- Bounties for enemy heroes and buildings are distributed globally among allies.
- In free-for-all, only the last-hitting team receives the bounty.
- Large neutral objectives follow the same distribution rules.

### 13.3 Free-for-All Anti-Kingmaker

The purpose is to prevent an undeclared third party from converting an existing engagement into a stronger 2v1, either by joining a fight or by arriving to finish a weakened participant.

- The game tracks a damage-affinity value per team against each opposing team.
- Affinity is derived from cumulative pre-mitigated damage over a sliding one-minute window, and is recalculated periodically.
- Pre-mitigated damage is what matters, so a player cannot manipulate the system with trivial attacks that do not move the needle.
- A team that has not recently participated in a conflict begins with little or no damage effectiveness against the teams already fighting.
- As that team contributes meaningful damage, its effectiveness rises gradually while the existing participants' relative effectiveness falls.
- Established participants in a 1v1 each enjoy full effectiveness against each other.
- If an engagement becomes a 2v1, the participating teams normalize toward roughly half effectiveness each.
- If an engagement becomes a 1v1v1 among three independent teams, the participating teams normalize toward roughly 77% effectiveness each.
- The percentages describe the intended effectiveness outcome of those engagements. They are not a requirement that all teams split a fixed total.
- Participation history decays gradually rather than disappearing abruptly. Thirty seconds after disengaging, prior participation is approximately half of its former influence.
- This decay prevents a retreating, low-health enemy from being immediately sniped at full effectiveness, and gives a defender time to repair after a failed raid before a fresh attacker deals full damage.
- Declared lobby allies are one allied group and do not suffer the free-for-all penalty.
- The shield is shown as a visual effect with an obvious gradient so the approximate current reduction is legible to the player.
- The formula is standardized and documented so that identical participation history always produces identical adjustment, and so that bug reports can be evaluated against a stated rule.

The mechanic intentionally permits a nuisance third party to make an existing fight less efficient, which may cause one of the original participants to disengage and farm elsewhere. It is not intended to make collusion free: two cooperating players who fight each other pay in time, in damage to repair, in production mana and cooldown, and in exposure to third parties. Any benefit from manipulating participation must be weighed against those actual costs.

### 13.4 Anti-Kingmaker Invariants

- Identical participation history always produces identical damage adjustment.
- Participation is measured in pre-mitigated damage.
- Insignificant attacks cannot meaningfully manipulate participation.
- Meaningful participation gradually changes the relative effectiveness of all involved teams.
- Historical participation expires gradually rather than abruptly.
- Declared alliances are treated consistently as allied participation.
- The mechanic must not reward splitting attacks across multiple opponents over concentrating them.
- The mechanic must not make increasing meaningful damage reduce the contributing team's own overall effectiveness.
- The mechanic never provides a cost-free way to manipulate damage affinity. Damage exchanged between opposing teams counts normally regardless of informal cooperation.
- The game does not attempt to infer player intent. It evaluates participation consistently.

## 14. Lobby Configuration

The host configures three base parameters:

- Maximum player count.
- Maximum building supply.
- Maximum unique instances of a building.

The host may also enable any combination of the following modes. The standard lobby has all of them off and uses the default values above.

| Mode | Effect |
| :--- | :--- |
| Shroud enabled | Vision is limited to sight ranges instead of the standard full information. This is tactical shroud, not environmental fog. |
| Mirrored auto-draft | Every player receives the same heroes and the same building options, removing draft asymmetry from the match. |
| Adjusted multipliers | Maximum capacity or production mana and cooldowns are multiplied for every player, at a value the host sets. |
| Open pool drafting | Drafted buildings remain in the deck instead of leaving it when selected, so duplicate buildings can be drafted. |

These parameters and modes modify the game's underlying formulas rather than selecting different hard-coded rulesets. The default lobby configuration establishes the intended standard experience.

## 15. Balance Methodology

The game is not balanced mathematically. Win rates are measured, not derived.

- Balance is evaluated through simulated AI gameplay trained with machine learning, then augmented by analyzing the result metrics of past human matches.
- The target is a 50% win rate for any decision made completely at random. A decision chosen strategically should exceed 50%.
- Each option should see a comparable pick rate across the full range of scenarios, including different maps and lobby rulesets. No option is universally good or bad; every option is situational.
- A strategy nobody predicted that wins more often is a discovery, not a defect. Players are expected to find and exploit inefficiencies in the system.
- Balance patches and additional content keep the target moving. Balance is a continuous process, not a state that is reached.

## 16. Deferred Decisions

The following are intentionally outside this document or are not yet specified sufficiently. They belong in child documents or content specifications unless they require a change to a foundational rule above.

- Exact anti-kingmaker formula, including the precise decay curve and the exact normalization function.
- Exact Bulwark resistance curves and radii.
- Exact relic pool, relic availability, and any relic pricing beyond 100 gold.
- Exact targeting debounce, formation spacing, leash distance, and formation change debounce.
- Specific soldier characteristics and production parameters.
- Exact building pool size, draw size, and deck replenishment details.
- Specific buildings and the soldier, economy, and production values each one provides.
- Creep types, camps, respawn behavior, and exact neutral bounty values.
- Map layouts and exact resource-node placement.
- Exact resource-node capacities, resistances, worker travel speed, worker capacity and cooldown values, and regrowth values.
- Detailed nexus attack and defense rules.
- Match length, pacing, and multi-team victory resolution beyond the ranking rule.

## 17. Design Boundary

This document defines the rules that all content must operate within. It does not prescribe that every hero, soldier, building, or other option must be simple, mechanically demanding, or strategically complex.

Content Design Guidelines establishes how individual content options are designed to provide a varied range of experiences while respecting these rules.
