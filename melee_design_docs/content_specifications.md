# Content Specifications

Status: Rough Draft

Purpose: Specify the first playable content set for the proof of concept: three heroes, the shared building pool, the soldier and economy packages those buildings grant, and the relic pool.

This document is the child of [Core Game Rules](core_game_rules.md), [Content Design Guidelines](content_design_guidelines.md), and [Co-op Campaign Design](coop_campaign_design.md). It is a draft of a small set, not a balanced set. Every number here is a starting value for playtesting, not a tuned value.

## 1. Engine Baseline

The specified content is authored against the existing simulation, not against an imagined one. This section records what the engine already provides, because anything outside it is either scripted or missing.

### 1.1 Authoring Surfaces

Content is authored as templates inside a map's `metadata.json`. This set uses the Units, Buildings, Resources, Props, Abilities, Weapons, Items, Attachments, and Vfx collections.

| Surface | Fields this draft uses |
| :--- | :--- |
| Unit | MaxHp, Damage, Range, Armor, Speed, AttackCooldown, ScanRadius, MovementType, CollisionCircle, SightRange, AcquisitionRange, GoldBounty, XpBounty, DamageType, Splash*, Crit*, HpRegen, MaxMana, ManaRegen, MagicResistance, attributes (Strength, Agility, Vitality, Intelligence, Wisdom, Fortune), LevelGrowth, IsHero, Abilities, Weapons, StartingItems, BuildOptions, ConstructionRange, ModelPath |
| Building | The unit fields plus PathingType, and a construction time through the build flow |
| Ability | AbilityType, ManaCost, Cooldown, TargetRange, AreaOfEffectRadius, Effects, AutoCast, ProjectileId, GridX/GridY, Hotkey |
| Ability effect | Kind (Damage, Heal, Shield, Buff, Debuff, Teleport, Knockback), Delivery (Single, Radius, Self, Cone, Line, Aura), Targets (Self, Allies, Enemies, All), Amount, Duration, DamageType, MaxTargets, Scaling (attribute, shape, coefficient, divisor, cap) |
| Resource node | MaxCapacity, HarvestRate, GrowthRate |
| Item | Equipment and inventory fields, used here for relics |

The stat vocabulary is closed: `StatKeys` accepts only MaxHp, HpRegen, MaxMana, ManaRegen, Armor, MagicResistance, Attack, MovementSpeed, AttackSpeed, AttackDelay, SpellPower, Tenacity, CooldownReduction, LifeSteal, Evasion, ProcRateMultiplier, BountyMultiplier, CritChance, CritMultiplier, the two armor penetrations, the two magic penetrations, CastPoint, HealingMultiplier, KnockbackResistance, StatusEffectBuffer, and the six attributes. An authored key outside that set is reported and ignored, so this draft uses no other names.

Damage has exactly three types: physical, magic, and pure. Armor mitigates physical damage, magic resistance mitigates magic damage, and nothing mitigates pure damage. Bulwark resists both physical and magic damage. Fire, frost, and similar flavours are presentation only.

Map packaging. A melee map uses the existing Asset Import system in a split form: the rules, meaning the C# script code and `metadata.json`, live in one `.rmap`, while `terrain.json` lives in a separate `.rmap`. The rules file links `melee_{{version}}_{{blake3Hash}}.rmap` instead of importing it. Both files must exist in the content-addressed store and are loaded together on map start.

### 1.2 Systems the Content Relies On

- Heroes: `IsHero`, `LevelGrowth`, `ExperiencePerLevel`, `XpBounty`, and `LevelService` already apply per-level growth from a template.
- Production queues: buildings already support queued unit production. This content drives each queue automatically from its building's charges instead of exposing a player training command.
- Economy: Gold, Wood, and Stone exist as player resources; gathering is `Gatherer` plus `ResourceNode`; passive income exists behind `EnablePassiveResourceIncome`. This content uses gold for relics and wood for buildings.
- Construction: `ConstructionState`, the `UnderConstruction` tag, `Invulnerable`, `BuildTask`, and `ConstructionRange` already exist.
- Combat: weapons carry windup, backswing, burst count, projectile speed, splash bands, penetration, and friendly-fire flags; `AutoCastMetadata` supports the Mender.
- Victory: `TriggerPlayerDefeat`, `TriggerPlayerVictory`, and per-player defeat reasons exist, as do timers and zones for mission scripting.

### 1.3 Engine Gaps This Set Depends On

| Needed by the rules | Engine status | Where it must live |
| :--- | :--- | :--- |
| Per-building charges with individual recharge | Not present. Production queues are player-driven and have no charge accounting | New simulation state, or authoritative map script |
| Per-building capacity contributions and summed per-type caps | Not present. No per-type cap accounting exists | Same as above |
| Automatic charge-to-queue production | Not present. Production queues exist but are not fed by automatic charge consumption | New simulation state |
| Floating charge counts above buildings and in unit statistics | No charge state or indicator | Simulation state and UI |
| Production-value modifiers from hero actives, hero innates, and relics | No API exposes charge count, capacity, recharge, or production time as buffable building values | Simulation state and buff-source effect API |
| Hero innate as an always-on player-wide effect | Aura delivery and buffs exist; no innate slot or permanent player-wide hero effect | Hero definition and buff-source effect API |
| Passive income drawn from finite resource nodes | Passive income is not tied to node capacity, exhaustion, or worker travel distance | Economy service |
| Shared building deck, draw of three, lock-in during construction | Not present | Editor authoring plus new runtime state |
| Building supply bounded by draft count | Not present | New runtime state |
| Building lifecycle: destroyed dormant at 0 HP, invulnerable, repairable to 100% | Construction and invulnerability exist; the destroyed/repair loop does not, and no repair system exists | New system |
| Hero death removes the army and clears queues; revival refills charges | No revival system exists | New system |
| Nexus elimination with team survival | Per-player victory/defeat triggers exist; the team rule does not | Mission/engine rule layer |
| Bulwark resistance for buildings and heroes | Not present. No physical-and-magic mitigation layer, and no proximity scaling for heroes | Damage resolver hook plus an owned-soldier proximity count |
| Income tax | Not present | Economy service |
| Free-for-all damage affinity | Not present | Damage resolver |
| Construction progress manipulation by an ability | Construction state exists; no API exposes it | Script or simulation API |
| Player-wide relic purchases at the Nexus and six equipment slots | Items and equipment exist; no purchase flow | New runtime state |
| Per-soldier targeting and formation rulesets | Acquisition and scan radii exist; no shared ruleset enums | Movement and target selection |
| Army-wide buffs resolved per unit | Abilities support radius and aura delivery; no army-wide buff bookkeeping | Ability effects and the unit stat panel |
| Ghost movement through soldiers and selected buildings | Pathing flags and collision exist; selective tag-based ghosting does not | Movement/pathing |
| Worker collision: same-player workers body-block, allied and enemy workers ghost | No owner-aware worker collision | Movement/pathing |

This draft does not pretend those gaps are small. It specifies the content so the gaps have a concrete target, and it avoids specifying content that would require capability the engine cannot plausibly gain.

## 2. Conventions

- Template IDs use the existing prefixes: `hero/vale`, `unit/shieldbearer`, `building/bulwark_hall`, `ability/shield_bash`, `item/sigil_of_the_wall`.
- Each soldier type declares exactly one targeting rule and one formation from the shared enums in Core Game Rules. Both are authored content, never a player choice.
- Soldiers have zero resource cost and an authored production time. They are never trained by a player; a building's production queue is fed automatically by its charges, and no building exposes a training command.
- Every constructed building costs 100 wood and has an authored construction time.
- A building's production values are the charge count, capacity contribution, recharge rate, and production time authored on the building. Only hero actives, hero innates, and relics modify them; there is no separate unlock layer.
- No soldier has per-unit progression, promotion, or equipment.
- Health, damage, and armor values are engine points, not percentages. Range and speed are in the engine's authored spatial units; they are internally consistent with each other, not with real-world metres.
- Placeholder art reuses assets already in `MapTemplate`: `orc_warrior_7.rmesh` and `fantasy_warrior_unit_1.rmesh` for units, `clock_tower_building.rmesh` and `timber_framed_house.rmesh` for buildings.

## 3. Proof of Concept Match Parameters

| Parameter | Value |
| :--- | :--- |
| Players | 2 default, up to 4 supported by the same rules |
| Hero selection | One Race / Faction dropdown per player, Random allowed; every nexus is identical |
| Building drafts | 5 per player, therefore building supply 5 |
| Starting resources | 200 gold, 100 wood |
| Starting army | None. Every soldier comes from automatic production |
| Hero level cap | 6; levels grant raw stats only. Relics cost 100 gold at the Nexus |
| Nexus health | 6000, armor 15, no attack, Bulwark 1.0 |
| Hero revival timer | 12 s, plus 3 s per prior death, capped at 45 s |
| Match target | 20–30 minutes |
| Starting charges | A completed soldier-granting building begins with one charge |
| Production | Automatic; charges fill each building's queue with no player command |

The first building draft is reachable at match start with the starting 100 wood. That is deliberate: the first meaningful decision should happen in the first minute, not after a build-up.

## 4. Heroes

### 4.1 Baselines

| Hero | Fantasy | Complexity | MaxHp | Armor | Damage | Interval | Range | Speed | Mana | Mana regen | Primary attribute |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Vale, the Lineholder | Durable frontline commander | Low | 900 | 8 | 30 | 1.25 | 1.6 | 5.2 | 240 | 2.2 | Strength 18 |
| Sable, the Ashcaller | Ranged area-damage spellcaster | Medium | 700 | 3 | 20 | 1.5 | 8.5 | 5.0 | 360 | 4.0 | Intelligence 26 |
| Corvus, the Quartermaster | Economy and tempo strategist | High | 800 | 5 | 24 | 1.3 | 9.0 | 5.0 | 300 | 3.2 | Intelligence 20, Fortune 14 |

Every hero active and innate is unlocked at match start. Levels neither unlock nor rank abilities; they raise the hero's raw stats, and active ability effectiveness scales from those stats. An innate may be flat like a relic or scale from stats. Relics are purchased from the Nexus for 100 gold each, independent of level, up to six (section 9).

Each hero has three active combat abilities and one innate passive. The Q, W, and E slots are input positions, not power tiers. There is no ultimate ability: E is simply the third active slot, and all three actives plus the innate are available from the first second of the match. An innate is a permanent player-wide effect in the same design space as a relic; it is never cast, never occupies an ability slot, and is shown as a permanent indicator rather than a button. Because the three actives belong to one hero, they may interact directly; a kit can contain a combo without creating a cross-content dependency.

Every hero has Bulwark, like the Nexus. It grants physical and magic resistance that scales with the number of that player's own soldiers nearby; allied soldiers do not count. The exact radius and curve are deferred (section 11).

### 4.2 Vale, the Lineholder

Vale is the entry point into the game. Her kit has one decision that matters: when to hold ground and when to give it up. Nothing in it requires precise aiming or fast sequencing.

| Ability | Slot | Cast | Mana | Cooldown | Range | Effect |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Grounding Shout | Q | Point | 60 | 14 s | 7 | 60 magic damage in radius 4, and a 25% movement slow for 2 s |
| Hold Fast | W | Self radius | 50 | 18 s | 6 | Allies in radius 6 gain +4 armor for 3 s |
| Shield Bash | E | Unit | 60 | 16 s | 4 | 40 physical damage and a 1.25 s stun |

| Innate | Effect |
| :--- | :--- |
| Unbroken | Vale and all owned units gain +6 armor and +10% Tenacity. Always active; cannot be cast. |

Unbroken is a flat player-wide effect, like a relic; it does not scale with level.

Ability growth. The ability scaling fields drive these values from the hero's Intelligence attribute, so they rise automatically with level. The table states intended outcomes early, mid, and late in the six-level curve; it is not a formula to reverse-engineer.

| Hero level | Grounding Shout damage | Hold Fast duration | Shield Bash stun | Notes |
| :--- | :--- | :--- | :--- | :--- |
| 1 | 60 | 3 s | 1.25 s | Baseline |
| 3 | 90 | 3.5 s | 1.4 s | Mid match |
| 6 | 120 | 4 s | 1.55 s | Level cap |

Co-op level: **The Last Gate**. Teaches army positioning, holding a damaged building line, and the repair loop. Lore hook: the last gatekeeper of a wall that no longer has an army behind it.

### 4.3 Sable, the Ashcaller

Sable trades Vale's durability for reach and area damage. Her decisions are about placement and timing, which is why she sits in the middle of the complexity spectrum: forgiving to pick up, punishing to use at the edge of her range.

| Ability | Slot | Cast | Mana | Cooldown | Range | Effect |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Ember Lance | Q | Point, line | 80 | 10 s | 12 | 90 magic damage along a line of length 12 and 35° width, up to 5 targets |
| Cinderfield | W | Point, radius | 100 | 20 s | 11 | Ground area of radius 5 for 4 s, dealing 25 magic damage per second |
| Backdraft | E | Self radius | 80 | 22 s | 4 | 70 magic damage and 3 units of knockback in radius 4 |

| Innate | Effect |
| :--- | :--- |
| Cinder Mark | Sable's abilities and all owned units' basic attacks apply a 2 s burn for 6 magic damage per second. The burn does not stack; refreshing it resets the duration. Burn damage scales from Sable's SpellPower. |

Cinder Mark gives the three actives a shared result without adding a fourth button: every hit Sable lands spreads the same burn, and the army applies it at the same time.

Ability growth. The ability scaling fields drive these values from the hero's Intelligence attribute.

| Hero level | Ember Lance damage | Cinderfield duration | Backdraft damage |
| :--- | :--- | :--- | :--- |
| 1 | 90 | 4 s | 70 |
| 3 | 130 | 4.5 s | 95 |
| 6 | 170 | 5 s | 120 |

Co-op level: **Ashfall**. Teaches area denial, kiting, and why a hero without an army nearby is not a threat. Lore hook: a fire that was lit to end a siege and never went out.

### 4.4 Corvus, the Quartermaster

Corvus is the complex hero. He is weaker in a straight fight and strongest when the player is converting economy into tempo: finishing a building sooner, moving an army sooner, fielding soldiers sooner. His difficulty is entirely in reading the match, not in execution.

| Ability | Slot | Cast | Mana | Cooldown | Range | Effect |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Requisition | Q | Self | 70 | 30 s | — | Completes 30% of the active construction's remaining time. No effect if nothing is under construction |
| Forced March | W | Self radius | 60 | 22 s | 8 | Allies in radius 8 gain +20% movement speed for 5 s |
| Emergency Levy | E | No target | 90 | 50 s | Self | Every owned soldier-granting building gains 1 charge, up to its maximum. Production remains automatic |

| Innate | Effect |
| :--- | :--- |
| Standing Ledger | Every owned soldier-granting building has +1 maximum charge. Always active; cannot be cast. |

Ability growth. The ability scaling fields drive `Requisition` from the hero's Fortune attribute.

| Hero level | Requisition completion | Notes |
| :--- | :--- | :--- |
| 1 | 30% | Baseline |
| 3 | 40% | Mid match |
| 6 | 50% | Level cap |

Standing Ledger and Emergency Levy are flat effects; their value comes from capacity and timing rather than level scaling. The three actives chain: Requisition finishes a building, Emergency Levy fills its charges, and Forced March moves the new soldiers to the fight.

Co-op level: **Ledger of Salt**. Teaches the economy-to-draft race, action commitment, and the value of finishing a building first. Lore hook: a quartermaster who has outfitted both sides of the same war.

## 5. The Shared Building Deck

Every building is a complete package: one soldier type, that building's charge count, capacity contribution, recharge rate, production time, an income rate, and a construction time. There are no mutually exclusive unit pools; every package competes with every other package. All nine buildings sit in one deck; the Nexus is present from the start and is not part of it.

Each construction costs 100 wood and takes the building's authored construction time. It draws up to three buildings still in the deck that the player does not already own; in a lobby that allows duplicate copies, an owned building stays eligible up to the configured limit. The player chooses one; it leaves the deck, and the unchosen options return. The next construction draws a fresh set of up to three. When the deck empties, it is restored and reshuffled.

Because every constructed building costs the same 100 wood, the package is the balance unit. A weaker soldier can carry a stronger economy, and a stronger soldier can carry a weaker economy. Gold income buys relics; wood income buys more buildings. A building is the unlock, so a player's base is a readable statement of that player's army.

## 6. Buildings

Every constructed building costs 100 wood. Construction time varies by package.

| Building | MaxHp | Armor | Bulwark | Income per minute | Soldier | Cap | Charges | Recharge | Production | Construction |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Bulwark Hall | 2000 | 10 | 0.9 | 30 gold | Shieldbearer | 6 | 3 | 22 s | 5 s | 30 s |
| Warband Yard | 1500 | 7 | 0.7 | 16 gold | Axeman | 6 | 3 | 20 s | 4 s | 30 s |
| Spear Lodge | 1600 | 8 | 0.8 | 10 gold | Spear Militia | 5 | 3 | 24 s | 5 s | 30 s |
| Archery Range | 1400 | 6 | 0.6 | 22 wood | Bowman | 8 | 4 | 18 s | 4 s | 30 s |
| Bolt Foundry | 1800 | 9 | 0.85 | 8 wood | Bolt Thrower | 2 | 1 | 30 s | 8 s | 30 s |
| Sling Camp | 1200 | 5 | 0.5 | 16 wood | Slinger | 8 | 4 | 16 s | 3 s | 30 s |
| Mender Shrine | 1500 | 7 | 0.7 | 14 gold | Mender | 4 | 2 | 26 s | 5 s | 35 s |
| Signal Post | 1300 | 6 | 0.6 | 6 gold | Standard Bearer | 3 | 2 | 28 s | 5 s | 35 s |
| Vigil Bastion | 1600 | 8 | 0.75 | 10 gold | Warder | 3 | 2 | 26 s | 6 s | 35 s |
| Nexus | 6000 | 15 | 1.0 | 0 | None. Hero revival location and relic vendor | — | — | — | — | Cannot be repaired |

Building notes:

- Income is the base value before the income tax curve, which is not specified yet. Actual income is reduced by the travel distance between the building and the nearest matching resource node with remaining capacity. If no matching node is reachable, the building produces no income of that type. The distance falloff curve is not specified yet.
- Passive income is produced by invulnerable workers traveling to the nearest matching node. A node has no worker cap. Workers from different players ghost through one another; workers owned by the same player body-block.
- Cap is the building's capacity contribution to its soldier type. A type's total cap is the sum of the contributions of the player's owned buildings that grant it.
- Charges is the building's maximum charge count. A newly completed building begins with one charge.
- Recharge is the time to regain one charge. Production is the time to produce one queued soldier.
- Bulwark is the physical-and-magic resistance layer, not armor. It scales with the attacking force and building count.
- The three melee packages are ordered by tradeoff, not by power. The Bulwark Hall has the strongest income (30 gold), the toughest building, and the slowest, least damaging soldier. The Warband Yard has mid income (16 gold) and the hardest-hitting melee soldier. The Spear Lodge has the weakest income (10 gold) and the longest-reaching melee soldier. That is the tradeoff being tested.
- The Bolt Foundry is the extreme case: almost no economy, the strongest soldier in the set. It should be a real choice and a real risk.
- The Signal Post has the weakest economy in the set. Its soldier is the only army aura in the proof of concept, so it must be visible or it will be removed.
- The Mender Shrine, Signal Post, and Vigil Bastion answer three different questions. The Mender Shrine restores health already lost, the Vigil Bastion prevents damage from being taken, and the Signal Post spends its economy on moving the army faster. The Vigil Bastion is the sturdiest of the three.
- No building is a higher tier than another. Every soldier scales with the hero's level, so a package drafted early remains valid late. The tradeoff is economy against unit and capability, not power over time.
- Gold income funds relics; wood income funds further buildings. A package's income type is part of its tradeoff.
- A constructed building has no command card. Selection shows only its health, the soldier type it unlocked, its capacity contribution, current charge count and recharge, active production queue, and current income. Repair remains a hero action. Your own Nexus is the exception: it is the relic vendor, not a drafted building.
- Every soldier-granting building shows its current charge count as a floating number above it. The number is visible to every player, includes zero, and requires no selection. The Nexus has no charge indicator because it grants no soldier type.
- Building locations are permanent. Deactivation is the destroyed state, not demolition.
- Buildings may be placed anywhere with buildable pathing, so a building can sit beside a resource, inside a chokepoint, or next to an opponent's building. Location is part of a draft's value.
- In a lobby that allows a second copy of a building, the copy owns its own production profile. It contributes its full income, charge capacity, capacity contribution, recharge throughput, and production throughput, so two copies double those values while tracking charges and production separately.

## 7. Soldiers

All soldiers are produced automatically from building charges; no player trains them. Their resource cost is zero, and each type has an authored production time. The per-type cap is the only limit on how many of that type can exist. A charge is consumed into the building's queue whenever the type has an unreserved slot, and queued soldiers reserve capacity.

| Soldier | Granted by | Formation | Targeting | MaxHp | Armor | Damage | Interval | Range | Speed | Damage type | Bounty |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Shieldbearer | Bulwark Hall | Front | Standard | 420 | 7 | 14 | 1.4 | 1.4 | 4.2 | physical | 8 gold, 6 XP |
| Axeman | Warband Yard | Flank | Standard | 300 | 3 | 26 | 1.6 | 1.6 | 4.6 | physical | 10 gold, 8 XP |
| Spear Militia | Spear Lodge | Front | Tank-Shredder | 330 | 4 | 20 | 1.5 | 2.4 | 4.3 | physical | 9 gold, 7 XP |
| Bowman | Archery Range | Back | Assassin | 190 | 1 | 17 | 1.3 | 9.0 | 4.8 | physical | 7 gold, 5 XP |
| Slinger | Sling Camp | Flank | Standard | 160 | 0 | 12 | 1.1 | 7.0 | 5.2 | physical | 5 gold, 4 XP |
| Bolt Thrower | Bolt Foundry | Back | Tank-Shredder | 260 | 2 | 46 | 3.2 | 12.0 | 2.8 | physical | 16 gold, 12 XP |
| Mender | Mender Shrine | Back | Standard | 200 | 1 | 8 | 1.8 | 8.0 | 4.8 | magic | 6 gold, 5 XP |
| Standard Bearer | Signal Post | Flank | Standard | 240 | 3 | 10 | 1.8 | 1.5 | 4.9 | physical | 6 gold, 5 XP |
| Warder | Vigil Bastion | Front | Standard | 380 | 5 | 12 | 1.7 | 1.6 | 4.4 | physical | 8 gold, 6 XP |

Soldier notes:

- Every soldier bounty pays gold and experience, never wood. Hero and building bounties are larger than soldier bounties; neutral bounties scale with monster level.
- Formation and targeting are static authored data, so the buildings a player drafts decide the army's shape. A set of packages that grants only Front soldiers is durable but blind to flankers; a set that grants only Back soldiers has no line to stand behind.
- Soldier types are not tiered. Every soldier is set to the hero's level, so an early unit remains relevant late; balance is applied to the package.
- Targeting is distributed deliberately. Standard belongs to the soldiers that follow the hero's pressure, Tank-Shredder to the two types that answer high-health targets, and Assassin to the Bowman, which finishes what the front line has wounded.
- The nine types split three Front, three Flank, and three Back. The Slinger is the fast Flank skirmisher: its 7 range lets it pressure from the side, while its 160 health and 0 armor make it fragile if caught.
- No soldier has splash friendly fire, and no soldier has any crit.
- The Mender's heal is an auto-cast ability: 22 healing, 2.2 s cooldown, range 8, no mana. Its auto-cast engages below 90% ally health.
- The Standard Bearer's aura is +10% movement speed in radius 7. It is the only non-damage aura in the set, and it deliberately does not touch combat stats.
- The Warder's Warding Aegis is an auto-cast shield: 60 points of absorption to the lowest-health allied soldier within range 8, on a 12 s cooldown, no mana. The shield is a fixed number that belongs to the Warder, so it is identical every time the unit is produced and never scales with another unit, a relic, a hero active, or a hero innate. Shielding the lowest-health soldier makes the behaviour predictable enough to watch without inspecting the unit.
- The Spear Militia's 2.4 range is its identity: it can strike past the Shieldbearer line without a longer weapon changing its damage. Whether that range clears a Shieldbearer body is a playtesting question, not a specification question.
- Every soldier type is identical every time it is produced at the same hero level. Its stats can be modified only while a buff source is active, and its targeting rule and formation never change.
- Charge count, capacity contribution, recharge rate, and production time belong to the building's production profile, not to the soldier's combat statistics. A stronger soldier may take longer to field, just as it may carry a weaker economy.

## 8. Building Production Profiles

Each building owns the production profile for its soldier type: charge count, capacity contribution, recharge rate, and production time. These values are part of the building's authored package, not a separate unlock.

- A duplicate building has its own profile and state. Two copies add both income and production contributions, so income, total charge capacity, capacity contribution, recharge throughput, and production throughput double. Each copy keeps its authored per-building values; only the totals across copies double.
- Each copy consumes its own charges, recharges on its own timer, and runs its own production queue.
- Relics, hero actives, and hero innates can modify these values. A modifier applies to each affected building independently, including duplicate copies.
- No building grants an upgrade or exposes a research or post-construction modification surface.

## 9. Relics

Relics are player-wide effects and the second buff source. They are purchased at the player's Nexus for 100 gold each, independent of hero level. The player chooses from the relic pool; relics are never drawn randomly and never appear in a building draft. A hero holds at most six, one per inventory slot. They use the item and equipment system, and equipped relics are inspectable by any opponent who selects the hero.

Relics and the hero kit are the only sources that modify building production values. The kit is its three actives plus its innate. A production modifier applies to every owned building it affects, and duplicate copies are affected independently.

| Relic | Effect | Legibility requirement |
| :--- | :--- | :--- |
| Sigil of the Wall | Front-formation soldiers gain +10% max HP | Shown on the hero panel, in the unit stat panel, and as a persistent aura cue |
| Courier's Knot | All recharge is 10% faster | Shown on the hero panel |
| Spiked Barricades | All owned buildings return damage to their attackers | Shown on the hero panel and on the reflecting building |
| Deep Granaries | Passive income +10% | Shown on the hero panel |
| Warden's Writ | All owned units have 10% shorter debuff durations | Shown on the hero panel and in the unit stat panel |
| Muster Bell | Charge recharge is 10% faster | Shown on the hero panel |

The proof of concept pool is exactly six, so every relic can be evaluated against the six slots. A player can eventually buy the whole pool; the decision is purchase order and timing. A shipping pool should exceed the slots so purchases remain exclusive.

Relics never add conditional damage, on-hit effects, or per-unit behaviour. Each changes one player-wide number or rule and is summarizable in a single line.

## 10. Sanity Checks

These are arithmetic checks, not balance claims.

- **First decision.** The starting 100 wood reaches a building draft immediately, and the starting 200 gold can buy up to two relics. A player who gathers before building delays the first building but can chain the next one sooner. Both paths are viable by construction.
- **Raw time to kill, ignoring armor.** An Axeman kills a Bowman in 8 hits, about 12.8 s. A Bowman kills an Axeman in 18 hits, about 23.4 s. The ranged soldier loses a standing fight and wins by not standing in one.
- **Splash value.** A Bolt Thrower hit on three Slings deals 46 to the centre target and roughly 23 and 11 to the others through its bands. That is weaker than its cost suggests against a spread army, which is intentional.
- **Sustained throughput.** A Bowman's 18 s recharge is the throughput limit: about three soldiers per minute once the opening charge has been spent. Its cap of 8 fills only over several minutes if no Bowman dies, so recharge binds before capacity in a standing army.
- **Army ceiling.** The nine soldier types total 45 soldiers if every type were owned at cap. Building supply caps a player at five buildings, so at most five types are owned at once; the largest five-type cap total is 33 soldiers. Army size is bounded by the per-type caps and building supply.
- **Building cost.** Five buildings cost 500 wood. Because every constructed building costs the same 100 wood, the package is the choice, not the price.
- **Draft supply.** Five drafts from a shared deck of nine buildings. A player drafts five buildings, so there is no fixed coverage requirement; the fifth draft asks which package best complements the first four. Because the deck is shared, an opponent can take a package first.
- **Formation coverage.** The nine types split three Front, three Flank, and three Back, so every formation has multiple sources and a five-building player can cover all three.
- **Relic path.** Relics cost 100 gold each, the pool holds six, and the hero has six slots. A player can eventually buy the whole pool; the decision is purchase order and timing, not access.

## 11. Deliberately Not Specified Yet

- Creeps, creep camps, respawn timing, and exact neutral bounty values.
- The proof-of-concept map, its resource layout, and its choke points.
- Nexus defense details beyond health, armor, and Bulwark resistance.
- The income tax curve, and the Bulwark resistance formula and radii.
- Additional heroes beyond the three, and their co-op levels.
- Hero items other than relics.
- The shipping relic pool beyond the proof-of-concept six, and relic availability or pricing beyond 100 gold.
- Targeting debounce, formation spacing, leash distance, and formation change debounce values.
- Resource-node capacity, harvest rate, regrowth, worker travel speed, and the distance income falloff curve.
