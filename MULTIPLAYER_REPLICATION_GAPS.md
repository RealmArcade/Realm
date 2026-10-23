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
| `SyncPlayerResources` | gold, wood, stone per player slot | reliable | **every tick, unconditionally** |
| `SyncProductionQueue` | queue unit ids, progress, build time per producing building | reliable | **every tick, unconditionally** |
| `SyncWorldState` / `SyncWeatherRpc` / `SyncEnvironmentPresetRpc` | clock, weather, environment preset | reliable | on change |
| `ClientSpawnLootItemRpc` / `ClientDespawnLootItemRpc` | ground loot id, item type, position, charges | reliable | on change |
| Inventory, player tech, cooldowns, buffs, construction, rally point, scoreboards, script player table, zones | **no channel exists** | – | – |
| `PlaySpellEffect`, `ClientSpawnArrowProjectile`, `ClientCreateFloatingText`, camera/HUD/selection RPCs | presentation only | reliable/unreliable | event |

Registered ECS codecs (`Realm.Ecs/Replication/ReplicatedComponents.cs`): `WorldState`, `PlayerPopulation`, `PlayerResources`, `ScriptZonesState`, `ProductionQueue`, `PlayerTeam`, `Mana`, `MovementStats`, `ModelScale`, `Level`, `Experience`, `Attack` (numbers), `Armor` (numbers), and tags `Invulnerable`, `CommandsFrozen`, `AnimationsFrozen`, `OverheadBarsHidden`, `SpellImmune`, `UnderConstruction`.

`ReplicationContractTests` classifies every ECS component into one of five channels: **19 delta-replicated, 2 RPC-replicated, 2 cold-payload, 110 host-only and 11 storage helpers across 144 component types** (143 names, because the two `AttackSpeed` types share a name). Most of those 110 are static definitions, editor state, local prediction or presentation data and should stay local — but roughly a third is gameplay state the client UI and prediction already read.

## 2. Gameplay state the client reads but never receives

| State | Component(s) | What the client gets now | Player-visible symptom |
| --- | --- | --- | --- |
| Ability cooldowns | `Cooldowns`, `SpellCooldowns` | default (0) timers | command card never shows a cooldown sweep or countdown, `IsAbilityDisabled` only reacts to replicated `Disabled`/`Hidden` flags; `GetPlayerSpellCooldown` reads zero |
| Ability charges | `AbilityState` | nothing | charge-based abilities always look fully charged |
| Buffs / debuffs | `ModifierState`, `Buffs`, `BuffState`, `TimedStatusSlots` | empty | no buff icons, no slow/stun display, client-side movement prediction ignores modifiers |
| Player tech | `PlayerUpgrades` (weapons, shields, harvesting) | all `false` | upgrade buttons never show level/maxed, `GetPlayerTechLevel` returns 0, harvest rate and HUD differ from host |
| Custom resources | `PlayerResources` (`Ore` + 8 custom slots) | gold/wood/stone via RPC, and the whole struct via the player-scope delta codec | map-defined resources never update |
| Inventory | `Inventory` | empty (units are spawned with `new Inventory()`) | inventory panel is always empty, item charges unknown |
| Construction | `ConstructionState`, `BuildQueue`, `BuildTask` | none | `UnderConstruction` tag arrives so scaffolding renders, but progress bar and queue never appear (`UpdateUnitProduction`) |
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
| Unit stats | `Stats`, `UnitAttributes`, `UnitBaseStats`, `StatModifier`, `AttackSpeed`, `HealthStat`, `MovementSpeed`, `DerivedCombatStats` | base metadata only | tooltips show unupgraded numbers; per-unit `Attack`/`Armor`/`MovementStats` deltas do arrive, but recomputed derived stats and buff-modified stats do not |
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

The guard used to be vacuous: `EnumerateComponentTypes` filtered on `GetCustomAttribute<StructLayoutAttribute>()`, but `StructLayout` is a metadata pseudo-attribute that reflection never returns, so it yielded zero types. `EveryEcsComponentIsClassified` passed trivially and `HostOnlyComponentListHasNoStaleEntries` failed on every run. It now enumerates every value type under `Realm.Ecs.Components`, skips compiler-generated fixed-buffer structs, and asserts a minimum of 120 types so it cannot silently match nothing. Repairing it surfaced eight unclassified components (`PlayerUpgrades`, `AttackTarget`, `HealingTarget`, `LastAttacker`, `ScanRadius`, `AbilityTargetFilter`, `CameraState`, `BuildingSpawnOffset`, all now host-only) and eleven storage helpers that are embedded in components rather than components themselves (`BuffSlot`, `BuffSlotArray`, `InventorySlot`, `InventorySlotArray`, `ModifierSlotArray`, `PlayerSlotArray`, `StatSlot`, `StatSlotArray`, `VFXSlotArray`, `OccupiedZoneMask`, `CameraLocationSlot`); the stale `TerrainPathingFlags` entry was removed because it is an enum. Classification is by simple name, so the two `AttackSpeed` types collapse into one entry; overlapping buckets are rejected explicitly.

### 3.3 Verification covers the delta channel and weather
`ComputeHostStateFingerprint` hashes Player-scope codecs, ability flags, static texts, cold payload content ids and `WeatherState.Type`. Coverage is therefore exactly the components that ride the delta channel, the payload byte-identity of the cold channel, plus weather:

- `TerrainState` pathing is still outside: overrides are pushed on request (`ServerRequestDynamicPathingSync`) and are not ordered against baselines, so they join the hash in 2.5.
- Loot has no client ECS representation at all (`ClientSpawnLootItemRpc` builds nodes and a runtime dictionary), so it cannot be hashed until 1.6 puts it on a channel.

`ShadowClientVerifier` combines the same weather value from the host ECS, so a real delta-channel regression still fails its replay, and the client's own check still fails when resource deltas are dropped (verified by suppressing `PlayerResources` deltas: the baseline check reports `host=0dc0c0c2 client=e98886e8`).

### 3.4 Non-blittable state gets a cold payload channel
- `LeaderboardState` and `SummaryTableState` reference `ColdPayloadRegistry` handles, which are per-process indexes that mean nothing on the client.
- `ProductionQueue`, `Inventory`, `ScriptZonesState` reference `InternedValueStore` handles. Those ids are content hashes, so they are stable across processes, but the payload table itself (text lists, zone lists) has to travel; only `StringRegistry` text did.
- `ScriptPlayersState` and `PlayerTeam` carry `StringRegistry` ids; `PlayerTeamCodec` already drops `TeamNameStringId` because of it.

A raw `MemoryMarshal` codec is the wrong tool for the non-blittable cases. `WorldSnapshot.ColdPayloads` is the answer: `(scope, key, kind) -> content id` records, where interned payloads travel as their exact bytes (so the client's `InternedValueStore` regenerates the same content hash) and component-owned objects travel as MemoryPack blobs the client applies back into ECS. Payloads are sent with baselines, which keeps unreliable ticks the size they were, so a leaderboard, zone list or queue text list can take up to half a second to reach a client. A tombstone removes a key that no longer exists on the host. Only the content ids join the fingerprint, so a dropped payload is still detected without hashing kilobytes of blobs.

### 3.5 Bandwidth and allocation
- The two per-tick reliable RPCs for resources and production queues are gone: resources ride the player-scope codec and queues ride the unit-scope codec plus a cold payload.
- `BuildComponentDeltas` now writes each candidate through one reused scratch buffer, keeps one reusable delta list per peer, reuses the stored comparison buffers per `(peer, scope, key, type)`, and reuses the per-peer unit snapshot list. Steady-state tick allocation no longer scales with entity count; a baseline still rebuilds its comparison dictionary and the per-tick ability and static-text delta lists are still allocated.
- There is still no dirty tracking: the host walks every visible unit against every codec every tick. That walk is allocation-free but not free.

### 3.6 Visibility and ownership
Component deltas share the unit visibility filter, so off-screen enemies are invisible to the client (intended). But `ProcessBaselineSnapshotMismatches` despawns every entity missing from a baseline without asking whether it is owned by the local player, so a stealth/hidden *own* unit disappears from the client and comes back as a new entity — losing its node, selection and prediction history.

## 4. Task list

Tasks are ordered; each phase assumes the previous one. Sizes are rough: S ≈ half a day, M ≈ 1–2 days, L ≈ 3+ days.

### Phase 0 — foundation (do these before adding components)
- [x] **0.1 Field-level channel coverage (S).** The RPC bucket records a channel, not which fields it carries. Record the covered fields next to each RPC-replicated component so adding a resource slot, terrain layer or weather property without a channel fails review. Acceptance: the contract lists gold/wood/stone/Ore/custom slots for `PlayerResources`, pathing-only for `TerrainState`, and `CurrentWeather` for `WeatherState`.
- [x] **0.2 Fold RPC-replicated state into the fingerprint (M).** `PlayerResources` moved onto the delta channel as a player-scope codec, so it is fingerprinted automatically and every resource slot now arrives; `WeatherState.Type` is hashed directly. Production queues, terrain pathing and loot stay outside for the ordering reasons in 3.3 and join when 1.5, 2.5 and 1.6 give them a channel. Acceptance met: suppressing `PlayerResources` deltas makes the client report a baseline fingerprint mismatch.
- [x] **0.3 Cold payload channel (L).** `WorldSnapshot.ColdPayloads` carries `(scope, key, kind, content id)` records, applied before component deltas, resent on every baseline, with tombstones for keys that disappear. `LeaderboardState`, `SummaryTableState`, `ScriptZonesState` and `ProductionQueue` are on it; `Inventory` is a fully blittable struct whose only non-primitive fields are `StringRegistry` content hashes that the string table already transfers, so it becomes a plain unit-scope codec in 1.6 instead of a blob. Acceptance: content ids only change when the payload bytes change, and the client fingerprint matched during the 120 ms-ping harness run; `Realm.ReplicationVerifier` now asserts that a cold payload changes the fingerprint and that a tombstone removes the entry.
- [x] **0.4 Allocation pass (M).** `BuildComponentDeltas` writes through one reused scratch buffer, keeps a reusable delta list and snapshot list per peer, and overwrites the stored comparison buffer for a key instead of replacing it, so the only per-tick allocations left are the ability and static-text delta lists and the baseline comparison dictionary. `Realm.ReplicationVerifier` asserts that 500 codec writes through one reused buffer allocate zero bytes (measured with `GC.GetAllocatedBytesForCurrentThread`). Acceptance met for the codec path; a full per-tick budget number needs a harness counter, which 3.1 can carry.
- [ ] **0.5 Remove duplicate world state (S).** Keep one path for `WorldState` (preferably the codec) and delete `SyncWorldState`, or the reverse; document why.

### Phase 1 — player-visible gameplay state
- [ ] **1.1 Full player economy (S).** The player-scope `PlayerResources` codec landed in 0.2; delete the now-duplicate `SyncPlayerResources` / `SyncServerPlayerResources` / `SyncReconnectedPlayerResources` RPC path and confirm every client-side consumer reads the ECS component instead of the `ScriptPlayersState` copy the RPC used to write. Acceptance: no per-tick reliable resource RPC remains, and the HUD, affordability checks and map API still match the host.
- [ ] **1.2 Player tech (S).** `PlayerUpgrades` codec; extend `PlayerTeamCodec` to carry `TeamNameStringId`. Acceptance: tech levels and team names match on clients.
- [ ] **1.3 Cooldowns and charges (M).** `Cooldowns` and `SpellCooldowns` codecs (fixed slots of remaining seconds, host-authoritative) plus `AbilityState` charges; keep the disabled/hidden flags as the cheap path. Acceptance: casting on one client shows the correct sweep/countdown on the other.
- [ ] **1.4 Buffs and modifiers (M).** `ModifierState` + `Buffs` + `TimedStatusSlots` codec with remaining durations; client advances timers for display only. Acceptance: stun/slow icons and durations match; movement prediction accounts for slows.
- [ ] **1.5 Construction and queues (M).** The queue half landed in 0.3 (codec plus interned text list, no RPC), so what remains is `ConstructionState` as a codec and `BuildQueue`/`BuildTask` on the cold payload channel. Acceptance: progress bars and queue lists match, including while a building is being constructed.
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
