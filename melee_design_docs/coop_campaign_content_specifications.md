# Co-op Campaign Content Specifications

Status: Rough Draft

Purpose: Specify the first playable co-op mission for the proof of concept: one scenario for Vale, the Lineholder, using the shared content pool from [Content Specifications](content_specifications.md).

This document is the child of [Co-op Campaign Design](coop_campaign_design.md) and [Content Specifications](content_specifications.md). It authors a scenario around the shared content set; it does not add a unit, building, ability, relic, or rule. Every number here is a starting value for playtesting.

## 1. Scenario Identity

| Parameter | Value |
| :--- | :--- |
| Mission | The Last Gate |
| Predefined hero | Vale, the Lineholder |
| Players | 1 or 2, all playing Vale |
| Map | The Last Gate, a mountain pass with a walled gate |
| Target duration | 15 to 20 minutes |
| Enemy | One scripted warband with no hero |
| Terminal condition | Destroy the warband's nexus |
| Draft pool | The full shared pool, with no curation |
| Difficulty tiers | Relaxed, Standard, Hard, Extreme |

The mission is the canonical introduction to Vale. Its pressure is holding ground: a narrow gate that rewards a front line, two flank routes that punish ignoring the sides, and a breach that makes the player give ground and take it back. The wall behind Vale has been empty for years, and she has held its gate anyway.

In a two-player run, both players command Vale. The mission is a mirror in hero identity, not in draft: each player has an independent economy and draws from the shared building deck.

## 2. Content Inheritance

The mission owns no content. Every soldier, building, and relic is the shared one.

- The enemy's five buildings and every unit it spawns are shared content. There are no co-op variants, no renamed soldiers, and no mission-only structures.
- The gate and the wall are terrain, not buildings. They cannot be destroyed or repaired, and they add no rule.
- The south creep camp uses the shared creep set when it is authored. This document does not define creep statistics.
- Mission authorship is limited to the map, the starting state, the wave composition, the timings, the difficulty tiers, and the lore. The enemy building list in section 5 composes the opposition; it is not a player allowlist and does not curate the draft.

## 3. Map

The map is a pass running west to east. The player yard is west of a wall, the warband's yard is east of it, and the wall has one gate plus two narrow breaks at its ends.

| Region | Contents | Purpose |
| :--- | :--- | :--- |
| West yard | One nexus per player, one gold mine, two wood lines, buildable ground | Opening economy and first construction |
| The gate | A narrow buildable pass through the wall. The wall itself is impassable terrain | The main chokepoint and the formation test |
| North shelf | A rich gold mine and open buildable ground, reached by the north break | An exposed expansion |
| South camp | A neutral creep camp beside the south break | Optional experience and gold bounty |
| East yard | The warband's nexus and its five buildings | The target and the readable statement of enemy capability |

Rules:

- Both flank routes bypass the gate, so walling the gate diverts a wave rather than stopping it. A full gate fortification is a legitimate tactic, not a stall.
- Buildable ground covers the yards, the shelf, and the gatehouse floor. The wall and the pass floor outside the gatehouse are not buildable.
- Resource nodes are finite and shared, as in the parent rules. The player and any partner draw from the same stock; when a node is exhausted, the building using it seeks the next nearest matching node, and distance can make that income negligible. The shelf is contested by position and depletion, not by ownership.
- The two player nexuses are placed in separate pockets of the west yard, so neither player can build across the other's opening.

## 4. Starting State

| Element | Value |
| :--- | :--- |
| Hero level | 1 for every player |
| Nexus | One per player, 6000 health, armor 15, Bulwark 1.0 |
| Resources | 200 gold, 100 wood |
| Army | None |
| Starting production | None; production begins when the first soldier-granting building completes |
| Enemy buildings | Bulwark Hall, Archery Range, Warband Yard, Mender Shrine, Sling Camp |
| Enemy nexus | 6000 health, armor 15, Bulwark 1.0 |
| Neutral camp | One camp at the south break |
| First enemy wave | 4:00 |

The opening offers a building draft immediately: the starting 100 wood covers the 100-wood construction, and the starting 200 gold can buy up to two relics. The first meaningful decision happens in the first minute: which of the three offered buildings to lock, and whether to spend gold on a relic now or save it.

## 5. Authored Opposition

The warband is a scripted force, not a full player. It has no hero, does not gather, and does not draft. Its five buildings are placed at mission start, so an opponent who reads the east yard knows exactly what every wave can contain.

| Building | Grants | Role in the mission |
| :--- | :--- | :--- |
| Bulwark Hall | Shieldbearer, cap 6 | The front line of every wave |
| Archery Range | Bowman, cap 8 | The back line of every wave |
| Warband Yard | Axeman, cap 6 | The flank detachment from wave 2 onward |
| Mender Shrine | Mender, cap 4 | Sustain in waves 2 and 3 |
| Sling Camp | Slinger, cap 8 | The fast flanking screen in wave 3 |

Waves:

| Wave | Time | Composition | Route | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| Probe | 4:00 | 2 Shieldbearers, 2 Bowmen | Gate | First contact and first automatic production |
| Push | 8:30 | 6 Shieldbearers, 4 Bowmen, 3 Axemen, 1 Mender | Gate, with the Axemen on the north break | Teaches flank coverage and targeting |
| Breach | 13:00 | 6 Shieldbearers, 6 Axemen, 6 Bowmen, 2 Menders, 4 Slings | Gate and both breaks | The authored reversal and the climax defence |

Rules:

- Each wave spawns from the buildings that are functional when it begins. Destroying the Bulwark Hall before the breach removes every Shieldbearer from wave 3.
- The warband has no hero, so it cannot repair a destroyed building. A building destroyed before a wave stays destroyed, which makes a forward push a real way to weaken the next wave.
- The full wave-3 composition is 24 units, within the warband's per-type caps.
- The east yard is reachable from the start. An early push is limited by Bulwark and by the wave timers, not by an invisible wall.
- The enemy never receives a unit or building that is not in the shared content set.

## 6. Timeline

| Time | Beat | Event | What the player should learn |
| :--- | :--- | :--- | :--- |
| 0:00–1:00 | Establishment | Briefing. The gate, the wall, and the east yard are visible. No enemy movement | This is the ground being defended |
| 1:00–4:00 | Active economy and first draft | Gather, start a construction, lock a choice during construction | Buildings are the draft, and the building is the unlock |
| 4:00–7:00 | First contact | Wave 1 probes the gate. The first building has auto-cast its production ability | Mana feeds production automatically, and the gate is a shape |
| 7:00–11:00 | Escalation | Wave 2 flanks through the north break. The shelf and the south camp are live choices | Formations cover ground; the hero's target decides the army's focus |
| 11:00–15:00 | Breach | Wave 3 uses both breaks. A hero caught forward is expected to die once | Army removal, revival, and giving ground without losing the match |
| 15:00–20:00 | Counterattack | The warband has no further waves. The east yard is open | The nexus is the only win condition, and Bulwark makes force size matter |

The first draft is expected by 1:30. The first soldiers are expected before 4:00. The mission never waits for a specific draft; the waves are timed so a player who is slow to build meets the probe with the hero and whatever the first production casts.

## 7. Objectives and Pressure

The only terminal condition is the warband's nexus. The other objectives change the shape of the next wave or the player's economy; none can end the mission.

| Objective | Effect | Why it exists |
| :--- | :--- | :--- |
| Destroy the warband's nexus | Ends the mission | The sole elimination condition |
| Clear the south camp | An experience and gold bounty; the experience raises the hero's level, which raises every army unit to that level | Teaches that PvE activity is part of the economy |
| Take the north shelf | A second, finite gold mine, exposed to the north break | Teaches that expansion is a position and a finite resource, not a purchase |
| Destroy the Bulwark Hall | Removes Shieldbearers from later waves | Teaches that enemy buildings are the enemy's army capability |

Rules:

- Escalation is timed, never objective-gated. A player cannot stall the mission by refusing to push, and cannot skip the climax by rushing an objective.
- The gate is terrain. "Holding the gate" is a positioning decision, not a scripted counter or a destructible objective.
- The mission has no escort, extraction, or survival timer. Those would be a second win condition.

## 8. Difficulty

Difficulty changes the opposition and the pacing, never the player's numbers and never the rules. The mission is tuned for solo play; a two-player run is intentionally easier, and difficulty is the lever that restores pressure.

| Tier | Waves | Timing | Flanks |
| :--- | :--- | :--- | :--- |
| Relaxed | 25% fewer units, rounded up | 60 seconds later | No flank detachment in wave 2 |
| Standard | As authored | As authored | North break from wave 2, both breaks in wave 3 |
| Hard | 20% more units, rounded up | 30 seconds earlier | North break in every wave |
| Extreme | Every type at its cap | 45 seconds earlier | Both breaks in every wave |

Extreme's wave 1 is 6 Shieldbearers, 6 Axemen, 8 Bowmen, 4 Menders, and 8 Slings: 32 units, exactly the warband's per-type caps. It is the strongest force the warband's buildings can field without a second copy, so Extreme is the ceiling of the authored opposition rather than a separate ruleset.

## 9. Draft and Progression Expectations

The mission uses the shared building deck. No soldier or building is guaranteed, and none is required.

| Expectation | Target |
| :--- | :--- |
| First draft | 1:30 |
| Second draft | 6:00 |
| Third draft | 10:00 |
| Fourth draft | 14:00 |
| Fifth draft | 17:00 |
| Hero level after wave 1 | 2 |
| Hero level at the breach | 4 |
| Hero level during the counterattack | 6 |

- Any two drafts should hold the gate through wave 2. The third draft lands during the escalation and should answer the flank. Wave 3 asks for either a second front-line type or a sustain answer; a mono-formation army can survive it behind the gate, but not comfortably.
- A Back-heavy draft uses the hero as the front line. A Front-heavy draft has no ranged damage and must body-block. Neither is the correct draft; both are survivable at Standard.
- Relic purchases are the player's, 100 gold each and independent of level. The mission scripts none and assumes none.
- Because every player building costs 100 wood, every opening package is available at the same price. The first draw, not the resource cost, decides which package is available; no opening bypasses the mission's pressure.

## 10. Hero Kit Pressure

The mission is authored around Vale's kit without adding to it. Unbroken is always active and is part of the mission's baseline; the three actives are the choices.

| Ability | Moment the mission creates for it |
| :--- | :--- |
| Grounding Shout | The gate is narrow enough that one cast slows the whole approach |
| Hold Fast | The moment each wave reaches the line |
| Shield Bash | The first attacker through a break, stopping it short of the line |
| Unbroken (innate) | The breach, where the permanent armor and Tenacity let the army give ground without collapsing |

The mission does not require a specific ability order. A player who never casts still sees the army's targeting and formation rules work; the actives decide how cleanly the line holds, while the innate keeps the baseline survivable.

## 11. Lore

**Briefing.** The wall behind Vale has been empty for years. She has held its gate anyway, because a gate that is not held is only an opening. This morning the pass is full of smoke, and the warband on the other side has finally noticed.

**In-mission lines.**

- On first contact: "Through the gate, then. It is narrower on this side."
- On losing a building: "Stone can be replaced. The line cannot."
- On the counterattack: "The gate held. Open it."

**Epilogue.** Vale does not repair the wall. She leaves the gate open behind her and walks into the pass. What is left of the warband does not follow.

**Codex entry: The Last Gate.** Vale held a gate in a wall with no army behind it. When the pass was quiet, she opened the gate and went through.

## 12. Rewards

- The codex entry for The Last Gate.
- The lore epilogue.
- One recognition cosmetic: a gatewarden's standard for the hero panel.
- No power, no persistent progression, and no relic or ability unlocked by completion.

## 13. Sanity Checks

These are arithmetic and design checks, not balance claims.

- **Opening choice.** Every constructed building costs 100 wood, so the starting 100 wood reaches a construction immediately. Starting 200 gold can buy up to two relics; the opening choice is whether to spend it now or save.
- **First production versus probe.** The probe is 4 units. The first completed building opens with one production worth of mana and auto-casts immediately, and the hero fights alongside the first soldier. The first contact is winnable without a second building.
- **Wave 3 capacity.** The breach is 24 units, within the warband's per-type caps. No wave exceeds the buildings that produce it.
- **Extreme ceiling.** The Extreme wave is every type at cap, 32 units in total. The mission cannot field more without a duplicate building, which the default lobby does not allow.
- **Enemy capability read.** The five enemy buildings cover front-line, ranged, and sustain capabilities and are visible from the first minute. Every unit in a wave can be predicted from the east yard.
- **No alternate win condition.** The gate is terrain, the shelf and camp are economic choices, and the Bulwark Hall only weakens a wave. Destroying the warband's nexus is still the only ending.
- **Two-player parity.** The mission is tuned solo. Two players make it easier by design; difficulty tiers, not extra rules, restore the pressure.
- **No mission-only content.** Every enemy unit and building is in Content Specifications. The wall, gate, and shelf are terrain.

## 14. Deliberately Not Specified

- Creep statistics, camp composition, and respawn timing, which belong to the shared creep set.
- The AI's exact target selection, pathing, and ability usage.
- Voice lines, portraits, animation, and the cosmetic's final art.
- The exact damage race against the warband's nexus once the counterattack begins.
- Any tertiary objective beyond the south camp, the north shelf, and the Bulwark Hall.

## 15. Open Questions

- Whether the warband should receive a hero for repair and defence, or remain a pure scripted force without one.
- Whether the breach should force a hero death, or only be tuned so that a forward player is expected to die once.
- Whether the mission ships with creep camps before the shared creep set is authored.
- Whether two-player runs should remain intentionally easier or receive an authored wave increase once the mission is tuned.
