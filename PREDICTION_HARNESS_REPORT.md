# Client prediction harness report

Two-instance harness: `Realm.Client/Testing/PredictionHarnessRunner.tscn` (host + client, scripted scenario, per-tick prediction records, machine-readable verdict and exit code).

Run: `dotnet build Realm.Client`, then
`Godot_v4.7-stable_mono_win64.exe --headless --path Realm.Client res://Testing/PredictionHarnessRunner.tscn -- --profile=<clean|latency|latency-jitter|latency-loss> --out=<dir> --duration=40 --port=<port>`

## Defects found and fixed

1. **Replicated units never received the host's pathing flags.** `NetworkService.ApplySnapshotToEntity` created client units without `PathingFlags`, so they fell back to `Ground` (8). ChaosArena units are `Ground | ShallowWater` (9), so the client could not path anywhere: the scenario reported "No locally controlled unit has a pathable destination" and prediction produced zero movement and zero path comparisons.
2. **Attack-move was never sent to the host.** The one-argument `GameHost.IssueAttackMoveCommand` only mutated local ECS, and the two-argument overload queued `move_queued`. The host dispatcher had no `attack_move` case. The client predicted an order the host never executed, leaving permanent predicted order state.
3. **Prediction wrote replicated gameplay components.** Predicted orders were stored in `MoveTo`/`AttackMove`, so authoritative removals were indistinguishable from predicted state, the client's prediction was cancelled whenever the host cleared an order, and the sync comparator correctly saw client-only order state.
4. **Client input wrote gameplay order components in multiplayer.** Local `InputService` order calls mutated replicated components before the host confirmed anything.
5. **Baselines never removed components the host no longer had.** Units kept stale `AttackTarget`/`LastAttacker`; loot entities whose node disappeared were never destroyed. Both broke the zero-mismatch gate.
6. **False parity failures.** Path parity compared the plan-time start waypoint (necessarily different) and in-flight targets; presentation parity compared intentionally-leading predicted render transforms; settled error included the first ticks of the post-order convergence transient and reported the per-unit maximum as "final".

## Changes

- `Realm.Ecs/Components/Movement/PredictedMoveTo.cs`: non-replicated client-only predicted order component.
- `MovementPredictionService`: predicted orders in `PredictedMoveTo`; bounded correction with deadband, snap-threshold telemetry, command registration/ack/rollback, no per-tick allocation.
- `MovementAndPathfindingService`: steps predicted orders through the same navmesh pathfinder and movement pipeline, skipping the replicated order while a prediction is active; arrival clears only prediction state.
- `InputService`: multiplayer clients with prediction enabled no longer write authoritative order components locally.
- `NetworkService`: client units get host-equivalent `PathingFlags`; host handles `attack_move`/`attack_move_queued`; baselines reconcile absent loot and absent replicated components; rejection reasons/entities flow to the client.
- `GameHost.Input`: attack-move routes through the real command path (`attack_move`, `attack_move_queued`).
- `GameHost.Network`: predicted command registration only for locally owned units; rejection acknowledgement carries rejected entity ids and reason.
- Diagnostics/harness: prediction binary records, per-unit/per-stage error summaries, rollback timings, path/corner parity on confirmed orders, predicted render transforms excluded from authoritative placement parity.

## Measured results (40 s, 150 ms one-way for the latency profiles)

## Scope decision

Prediction is a movement/interpolation concern. It does not run map script WASM on clients, does not become authoritative for anything, and does not attempt to anticipate discrete script decisions. Non-predictable behaviour is covered by presentation buffering on the client (host-authoritative result plus a reveal animation) and is measured as an envelope (p95 ≤ round trip + 2 ticks) rather than assumed to be instant.

The harness gates are to be pre-registered per network profile, include a prediction-off negative control that must fail the latency assertion, assert that no non-local unit is ever recorded as predicted, and measure per-tick allocation instead of relying on inspection.

| Profile | Input response | p95 error | Max error | Settled error | Path parity | Snaps | Rollback | Sync mismatches |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Clean | 1 tick | 0.043 m | 1.471 m | 0.000 m | 283 compared, 0 mismatched | 0 | 1 tick | 0 |
| Latency | 1 tick | 1.560 m | 2.286 m | 0.334 m | 288 compared, 0 mismatched | 0 | n/a | 0 |
| Latency + jitter | 1 tick | 1.650 m | 2.000 m | 0.467 m | 255 compared, 0 mismatched | 0 | 2 ticks | 0 |
| Latency + loss | 1 tick | 1.792 m | 2.000 m | 0.005 m | 334 compared, 0 mismatched | 0 | 2 ticks | 0 |

Before the fixes (pre-change run, 30 s, clean): prediction recorded 3927 samples but 0 movement samples, 0 path comparisons, and the scenario never issued an order because no locally controlled unit had a pathable destination. Input latency and drift were therefore not measurable at all; the measured input response after the fix is 1 tick under 150 ms one-way delay.

## Still failing / unverified

- **Latency + loss transport gate.** Prediction and parity pass, and compared-state mismatches stay at zero, but the existing sync report fails its transport budget: 182 host sequences never reached the client (allowed 99) and the final convergence lag is 171 sequences (allowed 120). The budget is not profile-aware even though the injected 3% per-datagram loss compounds across multi-fragment snapshots.
- **Client-side map script WASM execution is out of scope by decision, not by omission.** `ShouldLoadAndRunMapScript` still excludes multiplayer clients and that is now the intended behaviour. Running map scripts on clients would require a prediction-only permission mode over every `IGameAPI` mutation, plus cross-instance determinism, and it would predict coarse presentation decisions (draft rolls, spawns, phase changes) whose failures are far more jarring than a fractions-of-a-second reveal delay. Prediction therefore covers the fluid, tick-tracked motion path only: move, queued move, attack-move, gather approach and rejected-order rollback. The gap this leaves is explicitly measured rather than hidden: script-issued orders for locally owned units are not anticipated and arrive one round trip late. See "Scope decision" below.
- **The host's local player does not use the client path.** Every order site in `GameHost.Input.cs` is guarded by `_multiplayerActive && !Multiplayer.IsServer()`, and `MovementPredictionService.Tick`, `SimulationService.ShouldSimulateEntity` and `InputService.ShouldApplyOrdersToLocalEcs` all bail out unless `IsClientSimulationOnly`. A host player therefore mutates authority directly: there is no prediction, no reconciliation and no in-process command round trip to exercise, so single-player and the host half of multiplayer cannot catch client-path regressions.
- **Never-received commands** (as opposed to explicitly rejected ones) have no timeout-based rollback; only host rejection acknowledgement rolls back.
- The wire-level allocation discipline of the prediction tick was checked by inspection (no lambdas, strings, or collections per tick) but not measured with the project's allocation tracker; the run duration is 40 s, not multi-minute.
