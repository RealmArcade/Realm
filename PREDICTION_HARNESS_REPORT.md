# Client-side prediction: implementation and verification report

This supersedes the earlier version of this file. The scope changed: the client no longer runs the map script
WASM. The feature is now *one* prediction architecture in which the hosting player, a remote client, single
player and offline all take the same path.

## 1. What was wrong

1. **One predicate did three jobs.** `IsClientSimulationOnly` (`_multiplayerActive && !IsServer()`) gated
   prediction, the input handlers, the authoritative systems and the interpolation exemption. The hosting player
   and single player therefore bypassed prediction, command queueing, acknowledgement and reconciliation
   completely, so a multiplayer sync bug could only surface after a packet had crossed a socket.
2. **The client record could desynchronise its own reader.** A tracked unit that died between the prune and the
   record was skipped without adjusting the declared count, so every value after it belonged to another unit. The
   report is the only evidence a red run has, so this is listed with the product defects.
3. **Orders were applied twice, differently.** A client wrote gameplay order components (`MoveTo`,
   `AttackMove`, `Gatherer`, `BuildTask`) locally *and* queued a command; the host applied the order inline and
   queued nothing. Two implementations of "give this order" drifted apart. `IssueAttackMoveCommand(target)`
   only touched local ECS and never reached the host at all.
4. **Client units could not path.** Replicas were spawned without the host's pathing flags, so every client unit
   defaulted to `Ground` while the arena's units are `Ground | ShallowWater`. Before the fix a run recorded zero
   prediction samples and zero path comparisons.
5. **A predicted order could outlive the authority.** Prediction only rolled back on an explicit rejection, so
   an order the host never received left a unit walking to a dead destination, and a scripted order on a
   locally controlled unit stayed invisible behind the stale prediction.
6. **A tracked unit was interpolated as well as predicted.** `InterpolationQueryAction` exempted a predicted unit
   only while it carried an order component, so a settled predicted unit was dragged towards a stale visual
   target after every correction: a second, divergent smoothing model.
7. **Baselines never removed components or loot** the host no longer had, leaving stale `AttackTarget`,
   `LastAttacker` and orphaned loot.
8. **Prediction kept an entity's history across entity-id recycling**, so a new unit could be reconciled against
   the dead unit's sample and snapped several metres on its first snapshot.
9. **Prediction kept an unreceived order alive for three seconds**, which carried a unit far enough that the
   eventual correction exceeded the snap threshold; the resulting snap had no stated cause.
10. **Several checks measured the wrong thing**: plan-time start waypoint instead of path progress, deliberately
   leading predicted transforms as placement mismatches, a post-order transient as the settled error, and
   `Max()` reported as the final error.

## 2. What changed

**Role predicates.** `GameHost.HasAuthority`, `HasRemoteAuthority`, `ObservingPlayerIndex`,
`IsLocallyControlledViewpoint(int)`. Prediction gates on the *local viewpoint*, not on being a client; the
authoritative systems gate on `HasAuthority`.

**One command path.** Every order (move, queued move, attack, attack move, follow, patrol, hold, stop, gather,
train, cancel-train) is built as a `NetworkCommand` and submitted:
- remote client: `RpcId(1, SubmitCommand, …)` as before;
- hosting player, single player, offline: `LocalCommandTransport.Submit(...)`, an in-process queue drained at the
  start of the authority's next tick.

The `SubmitCommand` RPC body became `ApplyAuthoritativeCommand(senderId, cmd)`, called by both the RPC and the
queue drain, and `DeliverLocalCommandAck` serves both the ENet acknowledgement RPC and the local acknowledgement.
Order handlers no longer write gameplay order components themselves.

**Prediction on every role.** `MovementPredictionService.Tick` runs for the local viewpoint of any role. On an
instance that owns the authority it records the authoritative sample and does *not* integrate movement a second
time, so `predicted == authoritative` is an invariant. `ApplyLocalPrediction`/`ApplyPredictedOrder` only ever
write the non-replicated `PredictedMoveTo`.

**Bounded reconciliation.** Corrections lerp towards the authoritative sample (14/s while moving, 30/s when
settled, deadband 2.0 m / 0.05 m). A snap (4 m or more) requires a stated cause and carries its distance in the
reason: `authoritative teleport at N m/s`, `recovered from a N tick snapshot gap`, or `first authoritative sample
for the unit` (spawn or the start of prediction).

**Order lifecycle, three bounds.**
- `ExpireUnacknowledgedCommands`: an order the authority never acknowledged is dropped after the full timeout
  (90 ticks) *or* as soon as it has waited out the acknowledgement round trip and the unit has drifted 2 m from
  the authoritative sample, so a lost command is rolled back in about a third of a second instead of three.
- `ClampPredictionLead`: no predicted order may ever carry a unit more than 3 m from the latest authoritative
  sample. This bound does not depend on command bookkeeping, so an order whose record was lost still cannot walk
  the unit away.
- `RevertImplausibleSteps`: a predicted step larger than 1 m in one tick is reverted and its order dropped. The
  shared movement step repairs an off-mesh waypoint by moving the unit onto the navmesh; the client must not
  adopt that repair inside prediction, because the authority's next sample would snap it straight back.
- `ForgetEntity`: a dead or newly unowned entity loses its sample, sequence and command id, so a recycled entity
  id starts with no prediction history.

**Interpolation.** The exemption is now "this unit is tracked by prediction", for the entity's whole lifetime.

**Diagnostics.** Prediction record streams are role-aware (`prediction_client.bin` for prediction,
`prediction_host.bin` for the authority's snapshot stream; loopback writes both). New records: per-tick
allocation split between prediction and the shared movement step, snapshot-ordering violations, and a snap reason
that states the distance measured at the snap.

**Harness.** New `loopback` role (one process, authority and client share the in-memory transport), a
`--negative-control=1` mode, `--duration`, and per-profile thresholds and transport budgets. The report now
attributes a failure to a unit, tick, snapshot sequence, order and path state, and prints the error series for
the nine ticks before an unexplained snap.

## 3. Measured results (this build)

Two instances, 30 s, ~100 units on `ugc-ChaosArena`, injected one way delay via `UdpDelayRelay`. "Stream stalled"
samples are settled samples excluded because the client had not applied any snapshot for more than 8 ticks, i.e.
the gap being measured belongs to the transport.

| Run | Verdict | Input response | Error p95 | Error max | Settled transient | Sustained settled error | Slowest convergence | Path parity | Snaps (explained/bad) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| clean | pass | 1 tick | 0.048 m | 1.600 m | 0.048 m | 0.000 m | 1 tick | 273 / 0 bad | 0 / 0 |
| latency (150 ms) | pass | 1 tick | 1.537 m | 2.251 m | 0.050 m | 0.000 m | 12 ticks | 269 / 0 bad | 0 / 0 |
| latency + jitter (120 ± 40 ms) | pass | 1 tick | 1.763 m | 2.000 m | 0.095 m | 0.000 m | 3 ticks | 245 / 0 bad | 0 / 0 |
| loopback (authority = client) | pass | 1 tick | 0.000 m | 0.000 m | 0.000 m | 0.000 m | 1 tick | n/a | 0 / 0 |
| negative control (prediction off, 150 ms) | pass, defect reproduced | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a |
| latency + loss (120 ms, 3 %) | 2 of 4 runs failed (see 4.1) | 1 tick | 1.733 m | 2.000 m | 0.483 m | 0.000 m | 1–2 ticks | 62–314 / 0 bad | 0 / 1 |

Every green run reports zero compared-state mismatches (110 000+ unit states), zero presentation/placement
mismatches, zero navigation parity mismatches, zero oscillation violations, zero snapshot ordering violations and
zero unexplained snaps. Predicted remote unit samples: **0** on every run, so prediction is genuinely restricted
to locally controlled units. Corrections per second fall from the first half to the second half (3.27 → 0.00 on
clean, 6.37 → 0.00 on 150 ms), i.e. no upward trend.

**Per-tick allocation.** Prediction's own work allocates **0 bytes** on every profile, mean and max. The shared
movement step (`SimulationService.StepPredictedMovement` → the same `MovementService` the authority uses)
allocates 0–128 bytes, measured and reported separately because it is not prediction's code.

**Record integrity.** The client record wrote its declared unit count from the tracked list but skipped units that
died between the prune and the write, so a dead tracked unit desynchronised every reader for the rest of the
stream and produced evidence that contradicted the service's own measurements. The count now describes exactly the
units written, and the report is a faithful measurement again. This is worth stating because it is the reason the
loss-profile snap below could not be attributed for several iterations.

**Loopback invariant.** On an in-process authority the maximum distance between predicted and authoritative
position is **0.0000 m**, and every submitted command (5 of 5) was answered. The authority and its local viewpoint
cannot diverge, and no command is silently dropped.

**Settled error definition.** `Settled error` in the report is the largest error that persists for at least 10
consecutive settled samples (a third of a second). A single snapshot may legitimately move the authority by up to
the snap threshold, and the client is then that far away for the three or four ticks the bounded correction needs;
the raw maximum is still printed alongside as the transient so nothing is hidden. Convergence is additionally
asserted per unit: every unit must return within 0.05–0.6 m (per profile) inside 90 ticks of its last order,
measured against the newest sample the client actually received.

## 4. What still fails, and why

### 4.1 One unexplained snap on the loss profile, in roughly one run in two

On `latency-loss`, unit 60 occasionally snaps at 4.0–4.1 m with no allowlisted cause, and the report prints the
evidence:

```
Error series (tick:meters, o = has order): 578:0.14o, 579:0.14o, … 586:0.14o
prediction error reached 4.00 m with a fresh sample and no stated cause
The snap moved the unit 0.14 m onto the authoritative position
```

A 4 m difference is measured inside `ReconcileAuthoritativePosition`, while the applied position one tick before
and immediately after the snap is within 0.03–0.14 m of the authority, and the recorded sample gap is one sequence.
The difference therefore cannot be accumulated prediction drift: prediction's own position is where the authority
says it is, both before and after. Three mechanisms were ruled out by measurement: a predicted step of that size
(the 1 m revert bound would have logged a rollback), an entity-id recycle (a dead entity's sample is now forgotten)
and two local replicas claiming one server entity (the harness now fails such a run explicitly and none appeared).
What remains is how the value handed to reconciliation and the value applied to the entity can differ by four
metres inside one snapshot burst, which is in the delta-apply path and is the next thing to investigate.

The snap threshold, the cause list and the assertion are unchanged, so these runs stay red. The same run is green
in about half of attempts and every other profile is green in five attempts each, which is consistent with the
trigger being a lost or reordered delta.

A related defect in the attribution itself was fixed: the teleport bound was 25 m/s while a snap that is not already
attributed to an outage can only happen inside 5 ticks, where 4 m already implies 24 m/s. The bound was therefore
unreachable and no authoritative jump of four to five metres could ever be explained. It is now 12 m/s, well above
any unit's locomotion and below what a snap implies, so a genuine authoritative jump is attributed as one and a
prediction that drifts away from a stationary authority is still reported.

### 4.2 Transport loss on the delayed path

Godot warns `Sending 2444 bytes unreliably which is above the MTU (1392)`. Per-tick snapshots therefore span
several unreliable fragments, and on the relayed 150 ms path 7–22 % of host sequences never arrive, run to run,
against a 5 % allowance. The convergence check itself is now "did the client apply what it received" and reports
**0 received-but-unapplied sequences on every run**, so this is wire loss, not a pipeline stall. Snapshot format
and bandwidth work is explicitly out of scope for this story; the harness names the number instead of hiding it,
and the per-profile allowance is derived from the profile rather than a single global constant.

### 4.3 One startup placement mismatch on the loss profile

`1 rendered placement mismatches were detected in the first 2.0s` (e.g. entity 88, host `(22.15, 0.10, 158.28)`
against client `(20.00, 0.00, 160.56)`), on the same loss profile. The client's ECS agrees with the host — the
compared-state check is clean on those runs — so the rendered node was still sitting on a frozen off-frustum
transform when the pair was sampled, and the next update corrects it. Not caused by prediction; predicted
transforms are excluded from that check (16 exclusions per run).

### 4.4 Scripted orders are not predicted, by design

The client does not run the map script. Orders the scenario did not submit are measured instead: the "unpredicted
order visibility" metric reports p95 in ticks against a budget of one-way delay plus two ticks. In practice the
arena script issues too few orders on locally controlled units to state an envelope (0–1 sample per run), and the
report says so rather than publishing a p95 from one sample.

### 4.5 Build placement and spell casting keep their role split

Both own local UI state (preview mesh, obstruction validation, optimistic VFX and cooldown) and their
authoritative handlers already converge on one code path for every role; they are not part of the movement
prediction subset and were left alone deliberately.

## 5. How to run it

```
# two instances with injected conditions
dotnet build Realm.Client
"<godot>" --headless --path Realm.Client res://Testing/PredictionHarnessRunner.tscn -- \
    --profile=latency --duration=30 --out=D:/temp/pv_lat --port=29615

# authority and client in one process over the in-memory transport
"<godot>" --headless --path Realm.Client res://Testing/PredictionHarnessRunner.tscn -- \
    --loopback=1 --profile=clean --duration=25 --out=D:/temp/pv_loop --port=29610

# negative control: prediction off must reproduce the defect
"<godot>" --headless --path Realm.Client res://Testing/PredictionHarnessRunner.tscn -- \
    --profile=latency --duration=20 --negative-control=1 --out=D:/temp/pv_neg --port=29625
```

Profiles: `clean`, `latency` (150 ms one way), `latency-jitter` (120 ± 40 ms), `latency-loss` (120 ± 25 ms, 3 %).

Each run writes `prediction_report.txt`, `prediction_report.json`, `multiplayer_ecs_sync_report.txt`,
`negative_control_report.txt` (negative control only), `host.log` and `client.log`. The exit code is 0 only when
every failure counter is zero.

## 6. Files

- `Realm.Client/Core/GameHost.cs` — role predicates.
- `Realm.Client/Core/GameHost.Network.cs` — one command path, `ApplyAuthoritativeCommand`, local ack, latency probe.
- `Realm.Client/Core/GameHost.Input.cs` — order handlers submit commands instead of writing order state.
- `Realm.Client/Services/LocalCommandTransport.cs` — in-process transport.
- `Realm.Client/Services/MovementPredictionService.cs` — prediction, three bounds, expiry, adoption, accounting.
- `Realm.Client/Services/SimulationService.cs` — authority/viewpoint gates, interpolation exemption.
- `Realm.Client/Services/PredictionDiagnostics.cs` — role-aware record streams.
- `Realm.Client/Services/NetworkLatencyService.cs`, `Realm.Client/PingCounter.cs` — ping readout.
- `Realm.Ecs/Components/Movement/PredictedMoveTo.cs` — the non-replicated predicted order.
- `Realm.Client/Testing/MultiplayerSimulationHarness*.cs`, `PredictionHarnessRunner.cs`,
  `PredictionHarnessVerification.cs`, `MultiplayerEcsSyncTestSupport.cs`, `UdpDelayRelay.cs` — the harness.
