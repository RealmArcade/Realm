# Multiplayer replication gaps

Analysis of why "most gameplay state is not replicated", what the client actually receives today, and the work needed to close the gap. Written after a headless host + client run (`SimulationTests.MultiplayerECS`, 120 ms injected ping, 30 s, 1900 host ticks): positions, rotation, health, ownership, building flags and death flags converge exactly, so the transport is sound; everything below is a coverage problem, not a transport problem.

## 1. What a client receives today

Every tick the host builds one snapshot per peer and sends it as an RPC. The client never simulates: `GameHost.UpdateClientTick` only advances interpolation, movement prediction and presentation, so any state that is not in a snapshot is simply absent on the client (or, worse, silently stays at its spawn-time default).

| Channel | Contents | Transport | Cadence |
| --- | --- | --- | --- |
| `WorldSnapshot.Units` | position, velocity, rotation, current/max health, owner slot, unit id, dead/building/detailed flags | unreliable, visibility filtered | every tick for changed units |
| `WorldSnapshot.ColdPayloads` | `(scope, key, kind) -> content id` payloads for handle-backed state: interned zone lists and production-queue text lists, MemoryPack blobs for leaderboards and the summary table | reliable, with baselines | baseline resend plus a tombstone when a key disappears |
| `WorldSnapshot.ComponentDeltas` | 19 registered ECS codecs (below) | unreliable | every tick for changed values (all of them in a baseline) |
| `WorldSnapshot.AbilityStates` | ability `Disabled` / `Hidden` booleans only | unreliable | every tick on change |
| `WorldSnapshot.StaticTexts` | map script `Label3D` text, position, colour, visibility | unreliable | every tick on change |
| `WorldSnapshot.Catalog` | registry metadata (`placement_radius`, `dropoff`, `worker`, `primary`, resource node types) + string table entries | unreliable | baseline + when the string table grows |
| `ReceiveBaselineSnapshot` | full state, same shape as above | reliable | every 30 ticks (0.5 s) |
| `SyncWeatherRpc` / `SyncEnvironmentPresetRpc` | weather, environment preset | reliable | on change |
| `ClientSpawnLootItemRpc` / `ClientDespawnLootItemRpc` | ground loot id, item type, position, charges | reliable | on change |
| Component removals | an empty-byte delta for a `(scope, key, component type)` that the client already holds | unreliable | once, when the host drops the component |
| Inventory, rally point, scoreboards, script player table | **no channel exists** | – | – |
| `PlaySpellEffect`, `ClientSpawnArrowProjectile`, `ClientCreateFloatingText`, camera/HUD/selection RPCs | presentation only | reliable/unreliable | event |

Registered ECS codecs (`Realm.Ecs/Replication/ReplicatedComponents.cs`): `WorldState`, `PlayerPopulation`, `PlayerResources`, `ScriptZonesState`, `ProductionQueue`, `PlayerTeam`, `PlayerUpgrades`, `Cooldowns`, `SpellCooldowns`, `Buffs`, `BuffState`, `ModifierState`, `ConstructionState`, `BuildTask`, `BuildQueue`, `Mana`, `MovementStats`, `ModelScale`, `Level`, `Experience`, `Attack` (numbers), `Armor` (numbers), and tags `Invulnerable`, `CommandsFrozen`, `AnimationsFrozen`, `OverheadBarsHidden`, `SpellImmune`, `UnderConstruction`.

`ReplicationContractTests` classifies every ECS component into one of five channels: **29 delta-replicated, 2 RPC-replicated, 2 cold-payload, 100 host-only and 11 storage helpers across 144 component types** (144 names). Most of those 103 are static definitions, editor state, local prediction or presentation data and should stay local — but roughly a third is gameplay state the client UI and prediction already read.

## 2. Gameplay state the client reads but never receives

| State | Component(s) | What the client gets now | Player-visible symptom |
| --- | --- | --- | --- |
| Ability cooldowns | `Cooldowns`, `SpellCooldowns` | compact codec: up to eight live slots of `(status id, remaining, duration)` | command card shows the cooldown sweep and countdown, and `IsAbilityDisabled` sees real timers |
| Ability list | `AbilityState` | codec plus the interned ability-list payload | abilities granted or removed at runtime follow the host. There is no charge counter in ECS: item charges live in `Inventory` (1.6), so nothing else was missing here |
| Buffs / debuffs | `ModifierState`, `Buffs`, `BuffState`, `TimedStatusSlots` | compact codecs: up to eight live `(status id, remaining, duration)` triples for `Buffs`/`BuffState`, up to eight raw `StatModifier`s for `ModifierState` | buff icons and durations match, the client sees stun state, and slowed units predict at the slowed speed because the host's recomputed `MovementStats` is replicated |
| Player tech | `PlayerUpgrades` (weapons, shields, harvesting) | player-scope codec | upgrade buttons show level/maxed and `GetPlayerTechLevel` matches the host |
| Custom resources | `PlayerResources` (`Ore` + 8 custom slots) | gold/wood/stone via RPC, and the whole struct via the player-scope delta codec | map-defined resources never update |
| Inventory | `Inventory` | empty (units are spawned with `new Inventory()`) | inventory panel is always empty, item charges unknown |
| Construction | `ConstructionState`, `BuildQueue`, `BuildTask` | unit-scope codecs; the client no longer simulates construction, so progress, the worker task and the queue are the host's values | works: the fade-in follows `ConstructionState.Progress`, the unit panel shows the real build task, queue ghosts render from the replicated `BuildQueue`, and the host's removal tombstone clears `ConstructionState`/`UnderConstruction`/`BuildTask`/`BuildQueue` when the work finishes |
| Production queue | `ProductionQueue` | codec copy plus the interned text-list payload | works; the queue struct rides the delta channel and the unit ids arrive on the cold payload channel |
| Rally point | `RallyPoint` | none | rally flag never drawn when a client selects its own building |
| Script player table | `ScriptPlayersState` / `ScriptPlayer` buffer | host only | scoreboard names, kills, teams and map-HUD wood/stone are missing or zero (`ResourceEconomyService.GetPlayerWood` reads `ScriptPlayersState`) |
| Leaderboards | `LeaderboardState`, `SummaryTableState` | MemoryPack blobs on the cold payload channel | map scoreboard and summary table render on clients |
| Map zones | `ScriptZonesState` | codec copy plus the interned zone-list payload | objective/zone indicators and zone ownership follow the host |
| Resource depletion | `ResourceNode.Amount` | full amount forever | depleted trees/ore still report capacity; gathering UI misleads |
| Gatherers | `Gatherer` | none | carried amount/state unknown, gather animations desync |
| Orders | `MoveTo`, `AttackMove`, `Follow`, `HoldPosition`, `Patrol`, `WaypointQueue` | none | order line in the unit info panel always says idle for remote units; prediction cannot replay orders |
| Combat targeting | `AttackTarget`, `LastAttacker`, `CombatTargeting`, `AutoCombatState`, `CombatAlertState` | none | no attack indicator, no alert state, bounty attribution invisible |
| Tower upgrades | `TowerUpgradeLevel` | 0 | tower upgrade pips missing |
| Unit stats | `Stats`, `UnitAttributes`, `UnitBaseStats`, `StatModifier`, `AttackSpeedMultiplier`, `AttackSpeedStat`, `HealthStat`, `MovementSpeed`, `DerivedCombatStats` | base metadata only | tooltips show unupgraded numbers; per-unit `Attack`/`Armor`/`MovementStats` deltas do arrive, but recomputed derived stats and buff-modified stats do not |
| Terrain mutation | `TerrainState`, `TerrainPathingFlags` | initial terrain + region pathing overrides | map scripts that raise/lower terrain or permanently block it desync visually and in prediction |
| Shroud | `ShroudState` | client-local | server already filters by visibility, so a client can show a unit the host considers shrouded (or the reverse) whenever the two shroud calculations disagree |

## 3. Structural problems that make each fix harder

### 3.1 Player rows still have no channel
`ScriptPlayerStateCodec` used to be registered but could never fire: `TryWrite`/`Apply` required `Has<ScriptPlayer>(entity)`, while player entities only ever receive `PlayerResources`, `PlayerPopulation`, `PlayerTeam`, `SpellCooldowns` and `PlayerUpgrades`. Player rows live in `ScriptPlayersState` on the world entity, so the codec wrote nothing and applied nothing while making the registry look like player kills and teams replicated. The codec is removed and `ScriptPlayer` is now classified host-only; replicating the player table is Phase 1.8.

### 3.2 The RPC bucket records a channel, not field coverage
`ReplicationContractTests` has an explicit RPC bucket: `ProductionQueue`, `TerrainState` and `WeatherState` reach clients over dedicated RPCs and are no longer mislabelled host-only. The bucket means "this component has a channel", not "all of its fields arrive":

- `ProductionQueue` re-sends the whole queue every tick as a reliable RPC.
- `TerrainState` syncs pathing regions only; height, splat and cliffs do not.
- `WeatherState` syncs the weather string; fog density and other properties do not.

`PlayerResources` used to be in this bucket. It is now a player-scope delta codec, so every field (`Gold`, `Wood`, `Stone`, `Ore`, eight custom slots) arrives and the `SyncPlayerResources` RPC is redundant until 1.1 deletes it.

Two components that are often assumed to be RPC-replicated are not: `GroundLoot` becomes `LootItem3D` nodes on the client, and the pause `CountdownState` lives in `NetworkService` fields, so their ECS state stays host-only. Loot and countdown still work for the player; they just do not populate ECS.

The guard used to be vacuous: `EnumerateComponentTypes` filtered on `GetCustomAttribute<StructLayoutAttribute>()`, but `StructLayout` is a metadata pseudo-attribute that reflection never returns, so it yielded zero types. `EveryEcsComponentIsClassified` passed trivially and `HostOnlyComponentListHasNoStaleEntries` failed on every run. It now enumerates every value type under `Realm.Ecs.Components`, skips compiler-generated fixed-buffer structs, and asserts a minimum of 120 types so it cannot silently match nothing. Repairing it surfaced eight unclassified components (`PlayerUpgrades`, `AttackTarget`, `HealingTarget`, `LastAttacker`, `ScanRadius`, `AbilityTargetFilter`, `CameraState`, `BuildingSpawnOffset`, all now host-only) and eleven storage helpers that are embedded in components rather than components themselves (`BuffSlot`, `BuffSlotArray`, `InventorySlot`, `InventorySlotArray`, `ModifierSlotArray`, `PlayerSlotArray`, `StatSlot`, `StatSlotArray`, `VFXSlotArray`, `OccupiedZoneMask`, `CameraLocationSlot`); the stale `TerrainPathingFlags` entry was removed because it is an enum. Classification is by simple name, which is unambiguous now that the combat multiplier and the stat marker are named apart (`AttackSpeedMultiplier` and `AttackSpeedStat` rather than two `AttackSpeed` types); overlapping buckets are rejected explicitly.

### 3.3 Verification covers the delta channel and weather
`ComputeHostStateFingerprint` hashes Player-scope codecs, ability flags, static texts, cold payload content ids and `WeatherState.Type`. Coverage is therefore exactly the components that ride the delta channel, the payload byte-identity of the cold channel, plus weather:

- `TerrainState` pathing is still outside: overrides are pushed on request (`ServerRequestDynamicPathingSync`) and are not ordered against baselines, so they join the hash in 2.5.
- Loot has no client ECS representation at all (`ClientSpawnLootItemRpc` builds nodes and a runtime dictionary), so it cannot be hashed until 1.6 puts it on a channel.

`ShadowClientVerifier` combines the same weather value from the host ECS, so a real delta-channel regression still fails its replay, and the client's own check still fails when resource deltas are dropped (verified by suppressing `PlayerResources` deltas: the baseline check reports `host=0dc0c0c2 client=e98886e8`).

### 3.4 Non-blittable state gets a cold payload channel
- `LeaderboardState` and `SummaryTableState` reference `ColdPayloadRegistry` handles, which are per-process indexes that mean nothing on the client.
- `ProductionQueue`, `Inventory`, `ScriptZonesState` reference `InternedValueStore` handles. Those ids are content hashes, so they are stable across processes, but the payload table itself (text lists, zone lists) has to travel; only `StringRegistry` text did.
- `ScriptPlayersState` carries `StringRegistry` ids. `PlayerTeam` does too, and its codec now carries `TeamNameStringId` as well: those ids are content hashes and the string table transfers the matching text, so they resolve on a client.

A raw `MemoryMarshal` codec is the wrong tool for the non-blittable cases. `WorldSnapshot.ColdPayloads` is the answer: `(scope, key, kind) -> content id` records, where interned payloads travel as their exact bytes (so the client's `InternedValueStore` regenerates the same content hash) and component-owned objects travel as MemoryPack blobs the client applies back into ECS. Payloads are sent with baselines, which keeps unreliable ticks the size they were, so a leaderboard, zone list or queue text list can take up to half a second to reach a client. A tombstone removes a key that no longer exists on the host. Only the content ids join the fingerprint, so a dropped payload is still detected without hashing kilobytes of blobs.

### 3.5 Bandwidth and allocation
- The two per-tick reliable RPCs for resources and production queues are gone: resources ride the player-scope codec and queues ride the unit-scope codec plus a cold payload.
- `BuildComponentDeltas` now writes each candidate through one reused scratch buffer, keeps one reusable delta list per peer, reuses the stored comparison buffers per `(peer, scope, key, type)`, and reuses the per-peer unit snapshot list. Steady-state tick allocation no longer scales with entity count; a baseline still rebuilds its comparison dictionary and the per-tick ability and static-text delta lists are still allocated.
- There is still no dirty tracking: the host walks every visible unit against every codec every tick. That walk is allocation-free but not free.

### 3.6 Component removal now replicates
The delta channel used to carry only present components, so a component the host removed (a cleared `UnderConstruction`, an expired `Invulnerable`, a finished `ConstructionState`, a dequeued `BuildQueue`) stayed on the client forever. `IReplicatedComponentCodec.Remove` plus a zero-length delta (`Bytes = Array.Empty<byte>()`) is now the removal tombstone: the host emits one when a codec stops writing for a key it had previously sent, the client calls `codec.Remove` instead of `codec.Apply`, and `ReplicatedStateMirror` forgets the key so the fingerprint matches again. `Realm.ReplicationVerifier` asserts that `Remove` stops a codec from writing for every registered codec (319 checks).

### 3.7 Visibility and ownership
Component deltas share the unit visibility filter, so off-screen enemies are invisible to the client (intended). But `ProcessBaselineSnapshotMismatches` despawns every entity missing from a baseline without asking whether it is owned by the local player, so a stealth/hidden *own* unit disappears from the client and comes back as a new entity — losing its node, selection and prediction history.

## 4. Task list

Tasks are ordered; each phase assumes the previous one. Sizes are rough: S ≈ half a day, M ≈ 1–2 days, L ≈ 3+ days.

### Phase 0 — foundation (do these before adding components)
- [x] **0.1 Field-level channel coverage (S).** The RPC bucket records a channel, not which fields it carries. Record the covered fields next to each RPC-replicated component so adding a resource slot, terrain layer or weather property without a channel fails review. Acceptance: the contract lists gold/wood/stone/Ore/custom slots for `PlayerResources`, pathing-only for `TerrainState`, and `CurrentWeather` for `WeatherState`.
- [x] **0.2 Fold RPC-replicated state into the fingerprint (M).** `PlayerResources` moved onto the delta channel as a player-scope codec, so it is fingerprinted automatically and every resource slot now arrives; `WeatherState.Type` is hashed directly. Production queues, terrain pathing and loot stay outside for the ordering reasons in 3.3 and join when 1.5, 2.5 and 1.6 give them a channel. Acceptance met: suppressing `PlayerResources` deltas makes the client report a baseline fingerprint mismatch.
- [x] **0.3 Cold payload channel (L).** `WorldSnapshot.ColdPayloads` carries `(scope, key, kind, content id)` records, applied before component deltas, resent on every baseline, with tombstones for keys that disappear. `LeaderboardState`, `SummaryTableState`, `ScriptZonesState` and `ProductionQueue` are on it; `Inventory` is a fully blittable struct whose only non-primitive fields are `StringRegistry` content hashes that the string table already transfers, so it becomes a plain unit-scope codec in 1.6 instead of a blob. Acceptance: content ids only change when the payload bytes change, and the client fingerprint matched during the 120 ms-ping harness run; `Realm.ReplicationVerifier` now asserts that a cold payload changes the fingerprint and that a tombstone removes the entry.
- [x] **0.4 Allocation pass (M).** `BuildComponentDeltas` writes through one reused scratch buffer, keeps a reusable delta list and snapshot list per peer, and overwrites the stored comparison buffer for a key instead of replacing it, so the only per-tick allocations left are the ability and static-text delta lists and the baseline comparison dictionary. `Realm.ReplicationVerifier` asserts that 500 codec writes through one reused buffer allocate zero bytes (measured with `GC.GetAllocatedBytesForCurrentThread`). Acceptance met for the codec path; a full per-tick budget number needs a harness counter, which 3.1 can carry.
- [x] **0.5 Remove duplicate world state (S).** `WorldState` now travels only through its world-scope codec. `SyncWorldState`, `SyncReconnectedWorldState`, `EnvironmentService.SyncWorldStateDomain` and `NetworkService.TryGetReconnectedWorldState` are deleted. The codec wins because it is part of the fingerprint, is resent with every baseline (which is also what a reconnecting peer receives), and the client already drives day/night visuals from the replicated `WorldState` component in `UpdateClientWorldStateDomain`. The bytes are identical to the old RPC arguments, so the clock loses nothing except the reliable-vs-unreliable distinction on a dropped tick, which the next baseline repairs.

### Phase 1 — player-visible gameplay state
- [x] **1.1 Full player economy (S).** The player-scope `PlayerResources` codec landed in 0.2 and the duplicate `SyncPlayerResources` / `SyncServerPlayerResources` / `SyncReconnectedPlayerResources` RPC path is deleted. Every client-side consumer already reads the component (HUD view model, purchase validation in `NetworkService`, `CheatService`, reconnect baseline). The `ScriptPlayersState` copy the RPC also wrote goes stale on clients until 1.8 replicates it, which only affects the `ResourceEconomyService.GetPlayerWood/GetPlayerStone` reads that map scripts perform, not the HUD. Acceptance met: no per-tick reliable resource RPC remains and the harness fingerprint, which hashes every resource slot, still matched with zero mismatches.
- [x] **1.2 Player tech (S).** `PlayerUpgrades` is a player-scope codec and `PlayerTeamCodec` now carries `TeamNameStringId` alongside `TeamIndex` (8 bytes instead of 4). Team names resolve on clients because the string table already transfers every `StringRegistry` entry. Acceptance met: both components are fingerprinted, so a tech or team divergence now fails a baseline check; the harness ran clean.
- [x] **1.3 Cooldowns and abilities (M).** `Cooldowns` (unit scope) and `SpellCooldowns` (player scope) use a compact `TimedStatusPayload` codec: one count byte plus up to eight `(status id, remaining, duration)` triples, 97 bytes instead of the 388-byte raw `BuffSlotArray`. The first attempt shipped the raw struct and immediately showed up as snapshot loss in the harness (19 unpaired sequences, above the allowed 14), which is why the compact encoding exists. Codecs that normalize their input implement `INormalizingComponentCodec` and the verifier asserts stable re-encoding instead of byte-exact round-trip. `AbilityState` rides the unit codec with its interned ability list on the cold payload channel. The disabled/hidden flags stay the cheap path. Acceptance met: the client command card reads real `Cooldowns`/`SpellCooldowns` components, and the harness reported zero fingerprint mismatches and zero failures.
- [x] **1.4 Buffs and modifiers (M).** `Buffs`, `BuffState` and `ModifierState` are unit-scope codecs built on the same compact payload as cooldowns (eight live slots; `ModifierState` carries the raw 28-byte `StatModifier`s so `StatId`, type, value, duration and source ids all arrive). Movement prediction already accounts for slows without extra work: `AttributeStatCalculator` folds modifiers into `MovementStats`, which is itself replicated, and `MovementPredictionService` predicts from that speed. Client timers are display-only until the next host delta overwrites them. Acceptance met: the harness ran with zero fingerprint mismatches and zero failures, and the verifier round-trips every new codec.
- [x] **1.5 Construction and queues (M).** The queue half landed in 0.3 (codec plus interned text list, no RPC). `ConstructionState`, `BuildTask` and `BuildQueue` are plain unit-scope codecs now: all three are blittable, `BuildQueue` only carries `StringRegistry` ids and `Entity` references that the string table and the entity key already resolve, so none of them needs a cold payload. `BuildQueue` is 164 bytes and only sends when a queue actually changes, `BuildTask` is 12 bytes and follows the progress tick. Construction became host-authoritative: `TickConstructionSystem` takes the same `isAuthoritative` flag as `TickEcs`, so a client no longer advances progress, completes buildings, drains queues or issues the queued build command a second time; it still runs `UpdateBuildQueueGhosts` so the queue is visible. Removing `ConstructionState`/`UnderConstruction`/`BuildTask`/`BuildQueue` on completion reaches the client through the removal tombstone in 3.6. Acceptance met: the harness ran with zero state mismatches and zero fingerprint failures.
- [ ] **1.6 Inventory and loot (M).** `Inventory` on the cold payload channel with `StringRegistry` id remap; loot stays id-keyed but moves into the same channel. Acceptance: picked-up items and charges appear on clients.
- [ ] **1.7 Rally point (S).** `RallyPoint` codec. Acceptance: rally flag visible to the owner on clients.
- [ ] **1.8 Script player table (M).** `ScriptPlayersState` codec (fixed struct, string ids via the string table) as a World-scope delta. Acceptance: kills, teams, names and map-HUD resource totals match on clients.
- [ ] **1.9 Leaderboards and summary table (M, needs 0.3).** The blobs and client application landed in 0.3; what remains is confirming the scoreboard and summary table panels repaint on a client and refreshing them without a full HUD rebuild. Acceptance: scoreboard and summary table render on clients.
- [ ] **1.10 Zones (M, needs 0.3).** The interned zone-list transfer and the `ScriptZonesState` codec landed in 0.3; what remains is confirming the zone indicator and ownership UI read it on a client. Acceptance: zone objectives and ownership UI match.
- [ ] **1.11 Resource node depletion (S).** Add `Amount`/`MaxCapacity` to the unit snapshot or a `ResourceNode` codec. Acceptance: harvesting drains the node on every client.

### Phase 2 — prediction and behaviour parity
- [ ] **2.1 Orders (M).** Replicate `MoveTo`/`AttackMove`/`Follow`/`HoldPosition`/`Patrol` targets so the info panel shows the real order and prediction can replay it on reconciliation.
- [ ] **2.2 Combat targeting (M).** Replicate `AttackTarget`/`CombatTargeting`/`AutoCombatState`/`CombatAlertState`/`LastAttacker` for indicators and kill attribution display.
- [ ] **2.3 Derived stats policy (M).** Decide per stat whether the host replicates the computed value or both sides recompute from replicated modifiers; write it down and make `AttributeStatCalculator` deterministic for the recompute case. Acceptance: tooltip damage/speed/armor matches the host for an upgraded, buffed unit.
- [ ] **2.4 Own-entity visibility policy (S).** Never despawn an entity owned by the local player on baseline absence; hide it or keep it with a stale flag instead. Acceptance: a hidden own unit keeps its entity, selection and prediction history.
- [ ] **2.5 Terrain mutation (L).** Replicate runtime terrain edits as region deltas (height, splat, pathing) instead of only dynamic pathing overrides.

### Phase 3 — verification
- [ ] **3.1 Extend the sync record (M).** Add the new components to `MultiplayerSyncDiagnostics.RecordClientSnapshot`/`MultiplayerSyncDiagnostics.RecordHostSnapshot` and compare them in `MultiplayerEcsSyncTestSupport.Verify`. Acceptance: a deliberately broken codec fails the harness.
- [ ] **3.2 Exercise each system in the harness (M).** The harness currently issues no client commands by default; add a scenario that casts an ability, takes damage, buys an upgrade, builds, gathers and picks up an item, then asserts the client observed each one. This replaces the `MultiplayerECSHardening` TODO stub.
- [ ] **3.3 Record authoritative ECS state instead of presentation (S).** The harness TODO is right: record host `Position`/`RotationY` components instead of `Unit3D.GlobalPosition` so host smoothing cannot masquerade as a sync bug.
- [ ] **3.4 Replace the shroud assumption with data (S).** Record the host's per-player visibility set so a missing entity is only a failure when the host considered it visible through the shroud.
