# Co-op Campaign Design

Status: Working Draft

Purpose: Define the player-versus-environment co-op layer: one authored mission per hero, delivered as a lore introduction and a pseudo-tutorial for that hero, using the shared content pool.

This document augments [Core Game Rules](core_game_rules.md) with a scenario and teaching layer. It does not revise, replace, or special-case a parent rule. It is not a content specification: it defines what a co-op mission is, what it must accomplish, and the constraints it operates under, but not any individual mission, hero, or line of lore.

Co-op inherits [Content Specifications](content_specifications.md) whole. It maintains no roster, deck, or allowlist of its own, so every current and future soldier and building is part of co-op without an edit to this document. The only content restriction the layer imposes is that hero selection is disabled.

Difficulty scales with player count so 1-player is not easier or harder than 4-player, assuming equally skilled players. Missions must support 2-player minimum, but can be played solo if desired. Max players can be determined on a case by case basis depending on the mission.

## 1. Model

The co-op layer is derived from the commander-style co-op model: a small team of players, all playing the mission's predefined hero, plays a hand-authored PvE mission with a fixed narrative and a shared objective against scripted opposition.

Deviations from that reference model are deliberate and are listed in this section rather than left implicit.

| Reference element | This game's implementation |
| :--- | :--- |
| Large roster of commanders, any commander on any mission | One authored level per hero, and that level exists to introduce that hero |
| Commander selection before a mission | Hero selection is disabled. Every player plays the mission's predefined hero |
| Commander progression from level 1 to 15 with unlocks | One co-op level per hero. No per-hero leveling, no power unlocks |
| Two players, both playing full commanders | One or two players; every player uses the level's predefined hero, and the level is tuned to be completable solo |
| Mission objectives as the terminal win condition | Nexus destruction remains the terminal condition, consistent with the parent rules. Mission objectives shape the enemy and gate escalation rather than ending the match |
| Difficulty tiers | Difficulty tiers that modify opposition, not player statistics |
| Weekly mutators | Optional post-completion modifier layer; not required for the first release |

### 1.1 One Level Per Hero

Each hero has exactly one co-op level. The campaign is therefore exactly as large as the hero roster.

The level is the canonical introduction to that hero: it presents the hero's backstory, teaches the hero's kit through play, and demonstrates the doctrine the hero is designed to pursue. A player does not need to complete it before playing the hero elsewhere, because gating a hero behind its own introduction raises the barrier the game is trying to lower. The level is context and tutelage, not a key.

### 1.2 A Real Match, Not a Drill

The co-op level is a legitimate match of the game, played under the same rules as any other match, with an authored scenario and authored pressure. There is no separate tutorial mode, no disabled rules, and no sandbox phase.

This is what makes it a pseudo-tutorial: the skills it builds transfer directly to competitive play because nothing is faked. A player who finishes a co-op level has played the actual game badly and improved, not played a simplified variant of it.

The level must not work only because its content is unusually simple or because a rule is special-cased for it. If a lesson only holds under mission conditions, it is not teaching the game.

### 1.3 Content Inheritance

Co-op owns no content list. [Content Specifications](content_specifications.md) is the single source of truth for soldiers, buildings, the shared building deck, and construction costs, and co-op consumes it as written.

- Every unit and building defined there is available in every co-op mission. Content added later is available the day it lands, with no edit to this document and no edit to any mission.
- A mission may not curate its building deck, keep an allowlist, exclude an option, or introduce a co-op-only unit or building. Any such list would fall out of date the moment the shared content set changed.
- Mission authorship controls the scenario, not the pool: the map, resource layout, opposing composition drawn from the same pool, escalation, difficulty, and lore.
- The only content restriction in co-op is hero selection, which is disabled. Every player plays the mission's predefined hero; every unit and building remains available through the standard draft.
- When new content disrupts a mission, the mission is repaired or retired. The shared pool is never trimmed, and no option is removed from co-op, to protect an older mission.

This document must never enumerate a mission's available units or buildings. Such a list is a duplicate of the content set and is wrong as soon as the content set grows.

## 2. Rule Coverage

A co-op level uses the parent ruleset. "Authored" does not mean exempt.

### 2.1 Rules That Apply Unchanged

- Hero selection is disabled. Every player plays the mission's predefined hero.
- The hero is the only directly controlled unit.
- The army is autonomous. Its targeting rules and formations are properties of the soldiers the drafted buildings bring, not orders the player issues.
- Soldiers carry no per-unit customization, and army strength comes from the hero's level, capacity, mana throughput, and production cadence.
- Production is automatic. A building's two production abilities are auto-cast from its mana pool whenever the type they raise has room, and no player command starts production.
- A building is the unlock. Construction is the only unlock step.
- Buildings are unique, permanent, and carry both an economic and a military function.
- Active gathering is faster than passive gathering, and the hero can perform one major non-combat activity at a time.
- Gatherable gold and wood are finite. Depleting the nearest node can leave an expansion walking to a distant node, reducing its income to practical uselessness even if the building survives. Bounty gold is separate from node income; wood has no bounty source.
- The nexus is the elimination condition and the revival location.
- Hero death removes the army and clears production queues; revival replenishes every building's mana.
- Building damage is repairable by any allied hero, and the nexus is the exception.
- Bulwark makes small forces inefficient against buildings and against a hero surrounded by its own soldiers, without making either invulnerable.
- Relics are player-wide upgrades purchased at the Nexus for 100 gold each, never drafted.
- Friendly fire is disabled between teammates.

### 2.2 Rules That Are Inert

- Draft races against a human opponent. The AI does not draft, so the economy gates access to the next building draft but does not race a rival for it. In a two-player run, draft contention exists between the partners instead.
- Anti-kingmaker affinity. There is one enemy side, and teammates cannot damage one another.
- Alliance configuration and bounty sharing between declared allies.
- Ranking across teams.
- Full-information scouting pressure. The opposition is visible by design, so reading an enemy base is taught without punishment.
- Lobby levers beyond the mission's own difficulty selection, including player count, duplicate buildings, fog, mirrored drafts, adjusted multipliers, and open pool drafting.

### 2.3 Rules That Are Authored Per Level

- The predefined hero for the mission, and the fantasy the mission introduces.
- The mission's map, its resource layout, and the placement of the players' nexuses.
- The composition, scripted behavior, and escalation timing of the opposing force, drawn from the shared content pool.
- The difficulty tiers available.
- The lesson beats, including which mechanic is introduced at which point.
- The lore: briefing, in-mission lines, and the unlocked codex entry.

The draft pool is not on this list. It is inherited, not authored (section 3.1).

### 2.4 What the Co-op Layer May Not Change

- It may not add a second elimination condition, or make the nexus irrelevant.
- It may not grant heroes or soldiers power that exists only in co-op.
- It may not introduce per-unit upgrades, hidden combat modifiers, or name-based dependencies.
- It may not make a co-op reward, difficulty tier, or mission condition grant power the parent rules do not define.
- It may not allow hero selection, curate the building deck, exclude a unit or building, or add co-op-only content.
- It may not special-case a rule for one mission. A mission-specific exception is a rules bug with a narrative attached.

## 3. Anatomy of a Level

Every co-op level follows the same macro arc so that players learn the shape of the game, while the mission's hero changes what each beat emphasizes.

| Beat | Purpose | What the player does | What it teaches |
| :--- | :--- | :--- | :--- |
| 1. Establishment | Introduce the hero, the nexus, and the map | Move, look, hear the briefing | This is my home; this is what I am protecting |
| 2. Active economy | Fund the first commitment | Gather by hand, and feel the attention cost | Gathering is fast but costs hero time |
| 3. First draft | Make the first construction commitment | Begin a construction, watch the silhouette, pick one of the three offered buildings during construction | Buildings are the draft, and the building is the unlock |
| 4. First production | Field an army | Let the first building auto-cast its production ability | Soldiers appear from mana, cooldown, and capacity, without a command |
| 5. First contact | Learn the hero's combat identity | Fight a modest opposing force | The army's targeting rules and formations change the outcome, and the hero's kit has a shape |
| 6. Escalation | Pressure the economy and the army at once | Clear creeps, deny an expansion, repair | Passive income frees attention; bounties reward activity, and an expansion is denied by destruction, not captured |
| 7. Climax | Defend and break | Survive an assault, then destroy the opposing nexus | The nexus is the only win condition, and Bulwark makes force size matter |

Constraints on the arc:

- Beats may overlap, but each beat has one primary lesson. A beat that teaches three things teaches none of them.
- The mission teaches its hero's kit, not the entire game. Universal mechanics are reinforced, but the level's authored emphasis belongs to the mission's hero.
- Beats 1 through 5 are mandatory in every level. Beats 6 and 7 may be reshaped by the hero's fantasy, provided the nexus climax remains.

### 3.1 The Shared Building Deck

The draft is the standard draft, using the shared building deck from Content Specifications without curation. A co-op mission does not choose what its deck contains.

- Every building in the deck is part of every mission's pool, including options added after the mission was written.
- Random draws are preserved, because choosing among unfamiliar options is the skill being taught.
- No specific option can be assumed to appear. A mission must be completable with a reasonable but suboptimal draw, which proves it teaches judgement rather than a sequence.
- A mission must not be designed around one specific option; under a growing pool, that is a hidden quiz rather than a decision.
- The 100-wood building cost stays fixed, so the mission's resource geography determines draft pace without extra scripting.
- New content is a new possibility in every mission, not a per-mission patch. If a new unit or building breaks a mission's pacing, the mission's opposition or map is adjusted; the option is not withheld.
- The mission's hero should have a reachable signature doctrine, not a guaranteed one. Reachability comes from the hero's kit and from the standard pool, never from a hand-picked deck.

### 3.2 Authored Opposition

- The opposing force has its own nexus and buildings, so base-reading is a live lesson: the buildings visible on the map are the enemy's army capability.
- The opposition is drawn from the shared content pool. New soldiers and buildings are available to it without a rules change or a co-op-only variant.
- The opposition is composed to show the mission's hero being effective and to expose the hero's weakness. A hero that is never challenged in its own level is taught by propaganda.
- Opposition escalation is timed and stated. The mission signals that pressure is coming, so the player's first experience of an attack is preparation rather than ambush.
- The AI does not need to play the full game. It must be legible and deterministic enough that a player can learn its behavior, in the same way autonomous soldiers are learnable.

### 3.3 Target Duration

A first completion targets roughly fifteen to twenty minutes. Long enough to reach the passive-economy transition and a real engagement, short enough to restart after a failure without resenting it.

## 4. Teaching Design

### 4.1 Coaching, Not Modals

- Teaching happens through authored pressure and short contextual prompts, never through a modal that pauses the match or a mandatory tooltip before an action.
- A prompt appears when the player's situation makes the mechanic relevant, and it states one thing.
- Hints are recoverable. A player who dismisses or ignores a prompt must be able to discover the same lesson by playing, and the mission should not soft-lock on a missed instruction.
- The first use of every mechanic is low-stakes. The player meets army commitment against a small force before meeting it against a decisive one.

### 4.2 The Lesson Ladder

Each level teaches in this order, because each rung depends on the one below:

1. What the hero does with its own hands (movement, gathering, abilities).
2. How the hero converts economy into a building, and a building into capability.
3. How the hero's buildings produce and replenish an army.
4. How drafted formations and targeting rules change an engagement the player can see coming.
5. How the hero survives a mistake: death, army removal, revival, repair.

The ladder is identical across the roster; the mission's hero changes the emphasis inside each rung. One level may stress the economy transition, another may stress army commitment, another may stress holding a damaged building line while its own army rebuilds.

### 4.3 Failure Is the Lesson

- The most instructive moments are recoverable ones: a hero death that removes the army, a building reduced to zero and repaired, a defence lost and retaken.
- Every level should script at least one scripted-but-fair reversal so the player experiences recovery as a legitimate state rather than a loss of the match.
- The mission should never be winnable by ignoring composition, formations, and repair, and never lose because the player had no way to know a mechanic existed.
- A mandatory hero death teaches the revival loop better than any tooltip. Place it where the consequence is expensive but survivable.

## 5. Lore

Lore is delivered in three beats, all short, all about the character rather than the setting.

| Beat | Delivery | Must establish |
| :--- | :--- | :--- |
| Briefing | Pre-mission transmission, before the first action | Who this hero is, what they want, why they are here |
| In-mission lines | Two or three barks tied to arc beats, never interrupting combat | What this hero reveals under pressure, and how they relate to their army |
| Epilogue and codex | Post-completion screen, plus a permanent codex entry | What changed for this hero, and what the mission revealed about them |

Rules:

- The backstory is the hero's, not the world's. The level is an introduction to a character, and a player who skips the epilogue should still have learned who they were playing from their play.
- Lore never introduces mechanics. A codex entry may explain a mechanic already taught in play; it may not be the only place a rule is stated.
- Lore must not contradict the parent rules. Do not describe a hero as a member of a faction, a rank in an army, or the owner of a domain in terms that imply predefined factions, unit upgrades, or player-issued production exists.
- The art, naming, and tone stay legible to a player with no prior exposure. Avoid invented vocabulary in the briefing that the mission does not then demonstrate.
- The epilogue is the campaign's only progression hook. It rewards understanding, not power.

## 6. Difficulty

Difficulty changes the opposition and the mission's pacing, never the player's statistics and never the rules.

| Tier | Intended for | What changes |
| :--- | :--- | :--- |
| Relaxed | First contact with the hero or the genre | Smaller and later opposition, few early contests, forgiving escalation |
| Standard | The default first completion | The authored mission as designed |
| Hard | Repeat players who know the hero | Earlier and larger opposition, more pressure on resource locations, fewer scripted lulls |
| Extreme | Mastery | Maximum authored pressure; no additional rules |

Rules:

- Difficulty must not change the player's damage, income, or unit caps. A co-op player who learns a hero on Relaxed must not learn a different hero than the one played on Extreme. Economic pressure is expressed through what the opposition does to the player's resource locations, never through altering the player's own numbers.
- Rewards differ in recognition only: completion badges and cosmetics, never power or content gated by tier.
- Two-player runs use one difficulty setting for the party, and the mission is tuned so that both players can contribute meaningfully while playing the same predefined hero.

## 7. Players and the Partner Slot

- A level supports solo play and two-player co-op.
- Hero selection is disabled in co-op. Every player plays the mission's predefined hero; there is no featured slot and no partner hero choice.
- The partner exists so a player can share an introduction rather than to double the tutorial burden. Teaching prompts are written for the mission's hero, so they apply to both players.
- The partner is a full participant under the parent rules: their own economy, their own buildings, and no resource sharing.
- Draft contention between partners is real, because the building deck is shared. The standard deck makes that contention instructive rather than punishing, and the level must remain completable if both players choose poorly.
- The mission's narrative framing must work with either one or two players present. The briefing addresses the hero, not a fixed party size.

## 8. Failure, Recovery, and Restart

- Defeat is the destruction of every friendly nexus, exactly as in competitive play. Nothing else ends the mission, and a mission does not end while an allied nexus stands.
- If one player's nexus is destroyed while an allied nexus stands, that player stays in the match under the parent rule: they revive at their destroyed nexus and can repair their other buildings to restore economy and army. This is itself a lesson about team survival.
- The parent rule that a destroyed nexus can never be repaired is unchanged, so a player who has lost their nexus plays the remainder of the mission with no revival building left to defend.
- Restart returns to the beginning of the mission. There are no mid-mission checkpoints, because checkpoints would teach that consequences can be deferred.
- On restart, the player may change difficulty or return to mission selection. Neither choice is locked by a failed attempt.
- A mission that is failed repeatedly should suggest a lower difficulty rather than silently lowering it. The player keeps authorship of the decision.

## 9. Rewards and Progression

- Completing a level unlocks that hero's codex entry, its lore epilogue, and a recognition cosmetic.
- There are no stat unlocks, no persistent hero progression, and no co-op-only abilities. A player who has completed the campaign must be indistinguishable in power from a player who has not.
- Heroes are not gated behind their own levels, and the roster is not gated behind campaign completion.
- Post-completion modifiers are an optional replay layer that changes mission conditions, not rewards. Modifiers grant recognition only, and must not be required to experience a hero's identity.

## 10. Campaign Composition

The campaign is a curriculum, not a content list. Each level owns one lesson emphasis so that the roster collectively teaches the game.

- Across the roster, every core system should receive at least one level where it is the focus: the active-to-passive economy transition, army commitment, formation coverage, production cadence and caps, building repair and defensibility, expansion denial, neutral-objective contest, veterancy, and base-reading.
- No two levels should share a primary lesson emphasis. Duplicated emphasis is duplicated production cost for no additional teaching.
- Complexity ordering is not required. A player may start with any mission, because each level teaches its own predefined hero rather than assuming campaign order.
- Hero levels should span the complexity spectrum from Content Design Guidelines, so the campaign itself demonstrates that simple and demanding heroes are both first-class.
- Every level must also be enjoyable to a player who already knows the game. A tutorial that only functions as a tutorial is dead content after one play; the mission's authored opposition, map, and pacing must stand as a match in their own right.

## 11. Authoring Checklist

Apply these to any proposed co-op level.

1. Does the level exist to introduce its predefined hero, and does its authored emphasis match that hero's fantasy?
2. Does it disable hero selection and use exactly that hero for every player?
3. Does it use the shared content pool without curating, excluding, or adding options?
4. Is every beat present, with one primary lesson per beat?
5. Does the level teach the hero's kit without contradicting the parent rules?
6. Is the level winnable with a reasonable but suboptimal draft under the current shared pool?
7. Does the opposition both demonstrate the hero's strength and expose the hero's weakness?
8. Are escalation and attacks signalled, not sprung?
9. Does the player meet each mechanic at low stakes before a decisive one?
10. Is there at least one recoverable reversal, including a hero death that teaches the revival loop?
11. Is the lore about the character, short, and free of invented mechanical claims?
12. Does the mission remain a good match for a player who already understands it?
13. Can it be completed solo, and does it remain coherent with a partner?
14. Does the level avoid adding any rule that only exists inside it?

## 12. Open Questions

- Whether the first release ships a level for every hero or only for the proof-of-concept roster.
- Whether the partner slot is required for content production, and whether every mission must support two players.
- Whether the AI opposition can ever be an ally to the player, and how that would be authored without implying alliances are freely mutable.
- The campaign's setting, naming vocabulary, and whether a lore bible is required before the first level is written.
- Whether completion is tracked per hero, per difficulty, or per party.
- Whether post-completion modifiers are in scope for the first release.
- How the mission communicates the enemy's economy without implying the enemy drafts, when drafted buildings are the intended read on enemy capability.
- How a mission's pacing is repaired when a newly added unit or building invalidates it, and who owns that repair.
