# Content Design Guidelines

Status: Working Draft

Purpose: Define what constitutes well-designed content within the Core Game Rules. This document governs heroes, soldiers, buildings, and relics. It does not specify individual content.

The Core Game Rules are the parent specification. Where a guideline here conflicts with a rule there, the rule wins.

## 1. Design Goal

Content should let a player find the version of the game they want to play, rather than force every player through one intended complexity level. A hero with a single obvious decision and a hero with a dozen interacting conditions are both valid content, provided each is coherent within its own complexity.

The macro ruleset is deliberately uniform. All variety, personality, and mechanical demand live in content.

## 2. Hard Constraints Imposed by the Rules

These are not preferences. Content that violates them is rejected or redesigned.

- Individual soldiers are immutable. No unit upgrades, no per-unit modifications, no veteran promotions, no equipment on soldiers. A soldier type is always the same soldier at a given hero level; the only global change is matching the hero's level.
- Soldiers' combat power is never conditional on other content. Army-wide effects come from buff sources (hero actives, hero innates, and relic passives), are inspectable, and use standardized stat or effect types rather than hidden modifiers.
- No content requires a specifically named other piece of content to function or to gain value. Cross-content effects use standard characteristics, shared enums, and buff sources.
- A drafted option must not target or depend on a named entity the player may not own, because a random draft cannot guarantee it.
- Each soldier type declares exactly one targeting rule and one formation from the shared enums. A soldier never carries a bespoke behaviour.
- Buildings are drafted, unique, and permanent once established. They always carry both an economic and a military function, expressed as a tradeoff.
- Bulwark is a physical and magic resistance mechanic, not an armor stat. Buildings scale it with the attacking force and building count; heroes scale it with nearby soldiers owned by that hero's player, never with allied soldiers.
- A building is the unlock. Constructing it grants its soldier type, its production profile, and its economy; there is no research submenu and no post-construction upgrade path.
- Production is automatic. A building's two auto-cast production abilities draw on its mana pool whenever the type they raise has room, and no building exposes a training or manual production command.
- Heroes are the only directly controlled units and the only content with an ability bar.
- Every building package must be balanceable against every other building package in the shared deck.
- The nexus is the only elimination condition, so no content may create an alternative win condition.

## 3. Complexity Spectrum

Every content type supports a range of complexity, intentionally and by design.

- Complexity is an authored characteristic, not an accident of unclear mechanics. If a hero is hard, it is hard on purpose and for a stated reason.
- At the simple end, a hero should be understandable from its ability descriptions alone, with a clear default plan and forgiving execution. A player can be effective with it while still learning the game.
- At the demanding end, a hero may require timing, positioning, resource management, and matchup understanding, and may reward dedicated practice.
- Simple content must never be strictly weaker. It should trade ceiling for reliability, clarity, and consistency.
- Complex content must never be strictly stronger. Its advantage is expressiveness, adaptability, and reward for execution, not raw numbers.
- Both ends of the spectrum must be viable within the draft and match rules. A player who drafts simple content is not disadvantaged by an opponent who drafts complex content.

A useful practical test: two players of equal skill, one on the simplest option and one on the most demanding building package at the same 100-wood cost, should each have a plausible path to winning.

## 4. Fantasy Spectrum

Different players want to fulfill different fantasies, and no single playstyle is the intended experience.

Content should cover, at minimum, distinct fantasies such as:

- The durable frontline commander who holds ground and absorbs pressure.
- The spellcaster whose power comes from timing and area control.
- The tactician whose strength is army shape, positioning, and tempo.
- The commander whose identity is breadth and replenishment.
- The opportunist who punishes exposure and unfinished fights.
- The economic strategist whose advantage compounds into draft priority and capacity.

Guidelines:

- Each hero should have a distinct fantasy expressed through both its abilities and its preferred doctrine.
- Fantasy is expressed through behavior and decision shape, not through cosmetic reskinning. Two heroes that differ only in numbers share a fantasy.
- A fantasy should be legible to a new player within one match: what this hero wants to do, and when it is strong.
- A hero must not express its fantasy by commandeering the army. The player controls the hero, not the soldiers.

## 5. Progressive Mastery

- Players should be able to begin with straightforward choices and expand toward complex ones as they gain experience.
- Advanced content offers different forms of skill expression rather than more power.
- The content library should always retain approachable entry points in every content type, including the hero pool. A growing card pool must not gradually raise the minimum complexity of play.
- Complexity should be discoverable in layers: a simple hero should still have depth available to a player who keeps playing it, and a complex hero should still function for a player who has not mastered it.
- Abilities are unlocked from the first moment of the match. Mastery comes from using them well and from reading the draft, never from spending points to acquire them.

## 6. Independent Content Design

Each content type is evaluated primarily on its own terms. This is the strongest defense against cascading balance changes.

### 6.1 Heroes

- Evaluated on identity, fantasy, and complexity.
- A hero is balanced against the hero pool, not against the army or building pool.
- Hero strength should come from how it commands, contests, and supports its army, not from bypassing the army system.
- A hero's actives scale from its raw stats, so level growth improves its kit without a separate ability-build decision. An innate may be flat like a relic or scale from stats, but it never requires a separate build decision.
- There is no ultimate category. Every hero has three active combat abilities and one innate passive, and all three actives plus the innate are available from match start; the Q, W, and E slots are input positions, not power tiers.
- An innate is a permanent player-wide effect in the same design space as a relic: bounties, soldier combat, economy, production queues, capacity, or another fundamental rule. It is not a fourth active and is never cast.
- The three actives may interact with one another directly. Cross-ability combos inside one hero's kit are allowed and encouraged because the whole kit is one piece of content and cannot create a cross-content dependency.
- A hero is balanced as one kit—three actives plus an innate—against other heroes' equivalent kits. If one active is stronger, the same hero pays for it through weaker companion actives, a longer cooldown, a resource cost, a health cost, a narrower condition, or another explicit sacrifice. Being an active slot, or being named as a signature move, is never payment by itself.
- Because every active and innate is unlocked from the first minute, a strong effect cannot be gated behind levels or skill points. Its cost must exist inside the kit from the start of the match.

### 6.2 Soldiers

- Evaluated as part of the building package that grants them, against every other package in the shared deck.
- A soldier's value should be understandable from its own characteristics, which include its targeting rule and its formation.
- A soldier must not need a specific partner to be viable; adding it must simply add what it does.
- Coverage is compositional. A Back soldier is not weak for being in Back; the army is exposed when nothing holds Front, Flank, or Back. Vulnerability comes from the draft's shape, not from a bad unit.
- A soldier's targeting rule must be readable from its behaviour. A ruleset that requires an explanation overlay is too complex for a soldier.
- Auto-cast abilities use shared deterministic rules. A soldier may not bring a new player-facing button or a new selection pattern.
- Adding a new soldier must not require redesigning other building packages, and it must reuse the existing targeting and formation enums rather than add a rule.
- Soldier types are not tiered. Every soldier scales with the hero's level, so an early package remains valid late; balance the package as a whole rather than the soldier alone.

### 6.3 Buildings

- Evaluated on the economic-to-military tradeoff it offers.
- Every constructed building costs 100 wood, so the package is the balance unit. A weaker soldier may carry a stronger economy, and a stronger soldier may carry a weaker economy.
- Both halves of the tradeoff must be real: a building that is strong in both is not a choice, and a building that is weak in both is not content.
- A building's military half must be readable from its base presence, because observing an opponent's buildings is how army capability is communicated.
- A building's mana pool, mana regeneration, cooldowns, and capacity contribution are fixed authored values. Hero actives, hero innates, and relics may modify them; a building never grants an upgrade that does.
- A building's income must be drawn from a finite resource node. A building cannot create gold or wood from nothing: its workers attack the nearest matching node with their own attack, carry the pieces their damage earns, and deliver them at a drop-off, so income falls with worker travel distance and stops when no matching node is reachable.
- A building's worker profile is stated in its tooltip: how many workers it raises, how quickly, and which permanent buffs it gives them. A building never advertises a computed income rate, because its income is what its workers physically take and carry.
- A building's placement is part of its value. Content must remain meaningful both when placed beside a resource and when placed inside a chokepoint.

### 6.4 Relics

- Relics are player-wide upgrades, never per-unit equipment for soldiers.
- Relics are purchased at the Nexus for 100 gold each, independent of hero level. A hero holds at most six. Relics are not part of the building deck or a random draw.
- Relic effects use standardized stat or effect types and are fully inspectable on the hero by any opponent.
- Relic effects are limited in number and must be summarizable in one line. Keystone-rune impact is the target; a small novel subsystem is not.
- A relic may change how an army, building, or economy performs, but must never make an individual soldier's combat identity ambiguous.
- Relics and the hero kit are the sanctioned match-time buff sources; a building's authored worker buffs are the third, applied only to the workers it raises. A relic must read as a rule of the player's whole side, not as a hidden exception on one unit.

### 6.5 Ambition

- Options are independent answers to the match, not parts of a named combination. No option becomes correct because a specific other option was taken, except among the actives of the same hero's kit.
- Cross-content effects use stated characteristics, shared enums, and buff sources rather than naming another unit, building, hero, or relic. This keeps a balance change from cascading through unrelated options and keeps a random draft from depending on an entity the player does not own. A hero's own actives may interact directly because they are one piece of content.
- Theorycrafting must remain readable from tooltips. If an option only makes sense in combination with one specific other option outside its own hero kit, the option is not designed yet.

## 7. Legibility

Players make decisions in real time with full information. Content is only as good as it is readable.

- Counters should be communicated through recognizable, stated attributes rather than through memorized matchup charts. Targeting rules and formations are part of those stated attributes. If a player must consult an external source to know that a choice is bad, the content is not communicating itself.
- Army composition is intended to be parseable at a glance. Anything that would obscure it, such as per-unit upgrades or conditional combat buffs, is disallowed by the rules and should not be reintroduced through content design.
- Determinism is preferred over expressed intent. The army's targeting and formation must be predictable enough that a player learns it through play. If a system needs a visual overlay to explain why the autonomous army is doing something, the underlying behavior is too complex and should be simplified.
- Draft options should make their consequences apparent before selection: what the option does, which formation and targeting rule its soldier brings, and how it fills the army. Evaluating an unfamiliar option should not require prior knowledge or an external guide. Each option on offer carries the icon of the building being drafted rather than the icon of the soldier it raises, so the packages are told apart at a glance before any of them is read.
- Draft reservation is intentionally visible. Before a choice is locked, an opponent inspecting the building site can see the options reserved for that player. After the choice is locked, the other options return to the deck and the site shows only the locked choice, even while construction continues.
- Screen real estate and interaction count are budgeted. Construction, repair, relic purchase, and the three hero actives are the only verbs a player should need. Targeting and formation are authored rules, not commands, so they must be learned from the draft and the stat panel rather than from a shortcut.
- Completed building selection is inspection only, with one exception: your own Nexus opens the relic vendor. Every other completed building panel states what the building unlocked and its current state; it never offers a research, training, or post-construction modification command.
- Counter information is exhaustive. Clicking any unit, allied or enemy, shows its statistics, targeting rule, formation, and active buffs. Selecting a unit the player does not own hides or disables the command card, so an inspection never implies control.
- Production readiness is visible without selection. Every soldier-granting building displays its available production count, derived from current mana, as a floating number above it, including zero, so an observer can read how soon that building can field another soldier.
- Numeric systems should be explainable to the player in one sentence, and their formulas should preserve the monotonicity principle: more investment never yields less total value.

## 8. Economy and Army as One Decision

Content should reinforce that economic and military development are the same choice, not parallel tracks.

- A building's economic strength and military strength trade off against each other.
- Gold income funds relics and wood income funds further buildings, so a package's income type is part of its tradeoff.
- Content should not provide a way to escape the tradeoff, such as a building that is efficient at both, or a soldier that generates its own economy.
- Gatherable gold and wood are finite. Depleting a node, choosing which nodes to protect, and denying an opponent's gathering by destroying buildings, killing workers, or exhausting shared nodes are economic decisions alongside drafting, not separate upkeep systems. Bounty gold is additional to node income; wood has no bounty source.
- Army capacity, mana throughput, cooldowns, and the breadth of formations a draft can cover are the correct places for economic investment to pay off, because they preserve the immutability of individual soldiers at a given hero level.

## 9. Content That Should Not Be Created

- A unit that changes the value of three unrelated units, which in turn changes the value of a building, which in turn changes the value of a doctrine.
- A hero whose power comes from an unstated interaction rather than its stated abilities.
- A draft option that is only correct in combination with one specific other option.
- A player-level unlock that changes the value of other content.
- A soldier whose behaviour needs a bespoke rule outside the shared targeting and formation enums.
- A building whose identity is a research menu.
- A relic that adds conditional combat damage hidden from the opposing player.
- A second win condition, or any content that makes the nexus irrelevant.
- Content whose balance depends on a special case in the macro rules rather than on its own parameters.

## 10. Proof of Concept Scope

The first content release is a small but complete expression of the game's identity, not a miniature version of the eventual library.

It should demonstrate that the systems work together:

- Hero control and combat.
- Autonomous armies shaped by the targeting rules and formations of the soldiers the drafted buildings bring.
- Doctrine drafting and army composition.
- Building-based economy and progression.
- Map-based resource competition and roaming.
- Creep clearing, experience and gold bounties, and hero veterancy.
- Relics purchased with gold across the match.
- Attacking and defending the nexus.

Guidelines:

- The proof of concept targets a two-player, 1v1 match. Larger player counts remain supported by the same rules but are not the balancer's first target.
- The proof of concept is the only committed scope. The hero count and building pool size of a first release are not fixed by this document.
- Content count should be small enough to be understood by a new player in a few matches, and broad enough that no system is represented by a single option with no alternative.
- The shared building pool must contain more options than a single draw of three can fill, with genuinely competing alternatives, or the draft is not being tested.
- The proof of concept must not work only because its content is unusually simple or because the rules are special-cased for it. If a rule only holds for the starting content, the rule is wrong.
- Content added later should not require changing the macro rules. If it does, the rules were underspecified, and that is a finding worth acting on rather than a one-off exception.

## 11. Review Questions

Apply these to any proposed hero, soldier, building, or relic.

1. What fantasy does this fulfill, and is that fantasy distinct from existing content?
2. Where does it sit on the complexity spectrum, and is that placement intentional?
3. Is it strictly stronger or weaker than a comparable option? If so, why?
4. Does it create a dependency on any specifically named content, or on a combination with another option?
5. Does it change any soldier's combat power conditionally? It must not, except through a stated buff source.
6. Can it be balanced as a complete building package against every other package in the shared deck?
7. If it is a soldier, which targeting rule and formation does it bring, and can the army still cover the formations it gave up?
8. If it is a relic, is its effect player-wide, one line long, and inspectable on the hero?
9. Is its effect readable by an opponent without inspecting hidden state?
10. Does it preserve the economic and military tradeoff, including the 100-wood building cost, the 100-gold relic cost, and the per-type army caps?
11. Would adding a similar option later require reworking this one?
12. Does it introduce a new verb, a new resource, a new targeting rule, a new formation, or a new win condition? Each requires a rules-level justification, not a content-level one.
13. If one hero active is stronger than its companions, what explicit cost does the rest of the kit pay? An active slot is not a cost, and no ability is an ultimate.
14. If it is a hero innate, does it change a fundamental player-wide rule, stay always-on and uncast, and avoid becoming a fourth active?
