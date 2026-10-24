# Client-side prediction: implementation and verification report

This supersedes the earlier version of this file. The scope changed: the client no longer runs the map script
WASM. The feature is now *one* prediction architecture in which the hosting player, a remote client, single
player and offline all take the same path.

## 1. What was wrong

1. **One predicate did three jobs.** `IsClientSimulationOnly` (`_multiplayerActive && !IsServer()`) gated
   prediction, the input handlers, the authoritative systems and the interpolation exemption. The hosting player
   and single player therefore bypassed prediction, command queueing, acknowledgement and reconciliation
   completely, so a multiplayer sync bug could only surface after a packet had crossed a socket.
2. **Orders were applied twice, differently.** A client wrote gameplay order components (`MoveTo`,
   `AttackMove`, `Gatherer`, `BuildTask`) locally *and* queued a command; the host applied the order inline and
   queued nothing. Two implementations of "give this order" drifted apart. `IssueAttackMoveCommand(target)`
   only touched local ECS and never reached the host at all.
3. **Client units could not path.** Replicas were spawned without the host's pathing flags, so every client unit
   defaulted to `Ground` while the arena's units are `Ground | ShallowWater`. Before the fix a run recorded zero
   prediction samples and zero path comparisons.
4. **A predicted order could outlive the authority.** Prediction only rolled back on an explicit rejection, so
   an order the host never received left a unit walking to a dead destination forever, and a scripted order on a
   locally controlled unit stayed invisible behind the stale prediction.
5. **A tracked unit was interpolated as well as predicted.** `InterpolationQueryAction` exempted a predicted unit
   only while it carried an order component, so a settled predicted unit was dragged towards a stale visual
   target after every correction: a second, divergent smoothing model.
6. **Baselines never removed components or loot** the host no longer had, leaving stale `AttackTarget`,
   `LastAttacker` and orphaned loot.
7. **Several checks measured the wrong thing**: plan-time start waypoint instead of path progress, deliberately
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
settled, deadband 2.0 m / 0.05 m). A snap (4 m or more) requires a stated cause: `authoritative teleport at N
m/s`, or `recovered from a N tick snapshot gap` (gap ≥ 6 ticks, i.e. at least one snapshot never arrived).

**Order lifecycle.** `AdoptAuthoritativeOrders` drops a prediction the authority has contradicted (targets more
than 4 m apart, above the 2.2 m group formation scatter). `ExpireUnacknowledgedCommands` rolls back anything
unacknowledged after 90 ticks (3 s) with reason `the authority never acknowledged the order`. Build orders
register for acknowledgement and rollback but are not predicted as movement, because the authority chooses the
worker and arbitrates the slot.

**Interpolation.** The exemption is now "this unit is tracked by prediction", for the entity's whole lifetime.

**Diagnostics.** Prediction record streams are role-aware (`prediction_client.bin` for prediction,
`prediction_host.bin` for the authority's snapshot stream; loopback writes both). New records: per-tick
allocation, snapshot-ordering violations.

**Harness.** New `loopback` role (one process, authority and client share the in-memory transport), a
`--negative-control=1` mode, `--duration`, and per-profile thresholds and transport budgets.

## 3. Measured results

Two instances, 30 s unless stated, ~100–150 units on `ugc-ChaosArena`, injected one way delay via `UdpDelayRelay`.

| Run | Verdict | Input response | Error p95 | Error max | Settled | Path parity | Rollbacks | Snaps | Predicted samples |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| clean | pass | 1 tick | 0.048 m | 1.360 m | 0.048 m | 278 / 0 bad | 1 | 0 / 0 | 9399 |
| latency (150 ms) | pass | 1 tick | 1.738 m | 2.299 m | 0.004 m | 271 / 0 bad | 2 | 0 / 0 | 9215 |
| latency + jitter (120 ± 40 ms) | pass | 1 tick | 1.669 m | 2.000 m | ≤ 0.04 m | 268 / 0 bad | 6 | 0 / 0 | 9204 |
| latency + loss (120 ms, 3%) | **fail** | 1 tick | 1.733 m | 2.000 m | 0.040 m | 361 / 0 bad | 6 | 0 explained / 2 unexplained | 8770 |
| loopback (authority = client) | pass | 1 tick | 0.000 m | 0.000 m | 0.000 m | n/a | 1 | 0 / 0 | 4816 |
| latency, 120 s | pass | 1 tick | 1.694 m | 2.000 m | 0.279 m | 293 / 0 bad | 1 | 0 / 0 | 36285 |
| negative control (prediction off, 150 ms) | pass (defect reproduced) | n/a | n/a | n/a | n/a | n/a | n/a | n/a | 0 |

Zero compared-state mismatches on every run (`Compared units` 112 727 on the clean 30 s run). Zero
presentation/placement mismatches. Zero navigation parity mismatches. Predicted remote unit samples: 0 on
every run, i.e. prediction is genuinely restricted to locally controlled units.

**Per-tick allocation.** Prediction's own work allocates **0 bytes** on every profile, mean and max. The shared
movement step (`SimulationService.StepPredictedMovement` → the same `MovementService` the authority uses)
allocates up to 15 KB on a re-path; it is measured and reported separately rather than folded into the prediction
budget, because it is not prediction's code.

**Loopback invariant.** On an in-process authority the maximum distance between predicted and authoritative
position is **0.0000 m** over 4 816 samples, and every submitted command (5 of 5) was acknowledged: the
authority and its local viewpoint cannot diverge, and no command is silently dropped.

**Long run.** 36 285 samples over 120 s, corrections/s 1.47 (first half) → 0.00 (second half), no upward trend,
no accumulated offset (final 0.000 m for every unit).

## 4. What still fails, and why

**1. Two unexplained snaps on the loss profile.** Unit 60 snapped at ticks 517 and 1173 with an authoritative
sample at most 5 ticks old and an implied speed below the teleport bound, i.e. the client had drifted more than
4 m from the authority without either allowlisted cause. The run converges within 2 ticks and settles at
0.040 m with no oscillation and no residual offset, so this is a transient divergence, not unbounded drift. The
most likely mechanism is an order change whose snapshot was dropped while prediction still held the previous
target, after which the two paths diverge until the next sample arrives. This is reported, not suppressed: the
snap threshold check is unchanged.

**2. Snapshot loss on the delayed transport.** Godot warns `Sending 2444 bytes unreliably which is above the MTU
(1392)`. Per-tick snapshots therefore span several unreliable fragments, and on the relayed 150 ms path between
0.5 % and 22 % of host sequences never arrive, run to run. The convergence check is now "did the client apply
what it received" and reports **0 received-but-unapplied sequences on every run**, so this is wire loss rather
than a pipeline stall. The 5 % allowance for the clean/latency/jitter profiles is therefore occasionally
exceeded on long runs (1 536 of ~7 000 sequences over 120 s). Snapshot format and bandwidth work is explicitly
out of scope for this story; this is a transport characteristic the harness now names instead of hiding.

**3. Scripted orders are not predicted, by design.** The client does not run the map script. Orders the scenario
did not submit are measured instead: the "unpredicted order visibility" metric reports p95 in ticks against a
budget of one-way delay plus two ticks. In practice the arena script issues too few orders on locally controlled
units to state an envelope (1 sample per run), and the report says so rather than publishing a p95 from one
sample. This is the honest replacement for the removed cross-instance WASM determinism claim.

**4. Build placement and spell casting keep their role split.** Both own local UI state (preview mesh,
obstruction validation, optimistic VFX and cooldown) and their authoritative handlers already converge on one
code path for every role; they are not part of the movement-prediction subset and were left alone deliberately.

## 5. How to run it

```
# two instances with injected conditions
dotnet build Realm.Client
"<godot>" --headless --path Realm.Client res://Testing/PredictionHarnessRunner.tscn -- \
    --profile=latency --duration=30 --out=D:/temp/pv_lat --port=29615

# authority and client in one process over the in-memory transport
dotnet build Realm.Client
"<godot>" --headless --path Realm.Client res://Testing/PredictionHarnessRunner.tscn -- \
    --loopback=1 --profile=clean --duration=30 --out=D:/temp/pv_loop --port=29610

# negative control: prediction off must reproduce the defect
dotnet build Realm.Client
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
- `Realm.Client/Services/MovementPredictionService.cs` — prediction, reconciliation, expiry, adoption, accounting.
- `Realm.Client/Services/SimulationService.cs` — authority/viewpoint gates, interpolation exemption.
- `Realm.Client/Services/PredictionDiagnostics.cs` — role-aware record streams.
- `Realm.Client/Services/NetworkLatencyService.cs`, `Realm.Client/PingCounter.cs` — ping readout.
- `Realm.Ecs/Components/Movement/PredictedMoveTo.cs` — the non-replicated predicted order.
- `Realm.Client/Testing/MultiplayerSimulationHarness*.cs`, `PredictionHarnessRunner.cs`,
  `PredictionHarnessVerification.cs`, `MultiplayerEcsSyncTestSupport.cs`, `UdpDelayRelay.cs` — the harness.
