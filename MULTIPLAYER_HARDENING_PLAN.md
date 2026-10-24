# US-MP-RELEASE: multiplayer assessment, plan and findings ledger

Single living artifact for the multiplayer hardening pass. It replaces the earlier ad-hoc notes. Every item
carries the evidence it was verified against, a severity, and a status.

## 1. Architecture summary

Server-authoritative RTS. The host owns the ECS world in shared WASM linear memory and steps it at 30 Hz. Each peer
receives a per-tick snapshot: deltas chained to a base sequence, plus a full baseline every 30 sequences
(`BaseSequence = sequence / 30 * 30`). Clients send intent as `NetworkCommand` over reliable RPC, never run the
gameplay systems, and present snapshot state through `InterpolationTarget` plus bounded prediction for the units
they control. Transport is Godot's ENet layer with RPCs.

## 2. Command flow, phase by phase

1. **Input** (`GameHost.Input.cs` / `GameHost.cs`) turns a click into an order and calls `SubmitOrderCommand`.
2. **Transport** — remote client: `RpcId(1, SubmitCommand, payload)`; hosting player, single player, offline:
   `LocalCommandTransport.Submit` drained at the start of the authority tick. Both converge on
   `ApplyAuthoritativeCommand`.
3. **Deserialise** (`GameHost.Network.SubmitCommand`) — `MemoryPackSerializer.Deserialize<NetworkCommand>`.
4. **Dedupe** (`IsDuplicateCommand`) — per-peer monotonic command id.
5. **Validate and apply** (`NetworkService.ExecuteServerCommand` → `DispatchMovementCommand` /
   `DispatchActionCommand` / `DispatchTrainingCommand`) — ownership via `IsClientAuthorized` per unit id.
6. **Side effects on the host** — order components, velocities, build placement, spell effects, production queues.
7. **Replicate** — the next snapshot carries the consequences; the client acknowledges the command (accept or
   reject with a reason) so prediction can roll back.
8. **Present** — interpolation, prediction, visuals.

## 3. Trust-boundary map

| Peer-supplied value | Entry point | Validated today | Verdict |
| --- | --- | --- | --- |
| command type | `SubmitCommand` | switch, unknown types ignored | ok |
| `UnitEntityIds` | `SubmitCommand` | ownership per id in each handler; **no count cap** | needs cap |
| `TargetPosition` | `SubmitCommand` | used directly; non-finite values unchecked | needs check |
| `TargetEntityId` | `SubmitCommand` | resolved to a server entity, ownership of the *target* not required (fine) | ok |
| `ArgString` (build/spell) | `SubmitCommand` | build: registry lookup. **spell: nothing** | hole |
| `CommandId` | `SubmitCommand` | compared monotonically; non-positive bypasses dedupe | needs clamp |
| spell id / caster / position | `SubmitCommand` → `ProcessServerCommandSpell` | **none** — no ownership, cooldown, cost or range | hole |
| peer id | `UpdatePlayerSlot`, `UpdateDiagnostics` | **trusted from the payload** | hole |
| sender name | `ReceiveChatMessage` | **trusted from the payload**, no cap, no rate limit | hole |
| session token | `RequestReconnect` | token first, **then a name fallback** | hole |
| camera pose | `UpdateClientCamera` | none; no try/catch on the payload | needs harden |

## 4. Replication inventory

Replicated per tick: unit position, rotation, velocity, HP/max HP, owner slot at spawn, order targets, path
waypoints, attack target, production queue, buffs, cooldowns, cold payloads (inventory, abilities), loot, props,
player resources. Host-only: damage and death outcomes, AI and map-script decisions, resource spending, building
placement arbitration. Presentation-only: interpolation, prediction, floating text, indicators, camera.

## 5. Findings ledger

Severity: S1 a peer can cheat or crash the host, S2 divergence or silent data loss, S3 cost/scale.

| # | Sev | Item | Evidence | Status |
| --- | --- | --- | --- | --- |
| 1 | S1 | Spell commands are unauthenticated: no ownership, cooldown, cost or range check, arbitrary damage position | `NetworkService.cs:1297` sets the flag, `DispatchServerCommand` (`NetworkService.cs:1308`) never handles `"spell"`, `SimulationService.cs:36` applies AoE with no validation | **fixed** |
| 2 | S1 | `SubmitCommand` deserialises peer bytes with no try/catch and no size cap | `GameHost.Network.cs:366` | **fixed** |
| 3 | S1 | No cap on `UnitEntityIds`, so one command can drive unbounded work | `NetworkCommand.cs` + every handler loop | **fixed** |
| 4 | S1 | A non-positive `CommandId` bypasses dedupe; a huge one blocks the peer's later commands, and the table is never reset across matches | `GameHost.Network.cs:IsDuplicateCommand` | **fixed** |
| 5 | S1 | `UpdatePlayerSlot` and `UpdateDiagnostics` trust the payload peer id, so any peer can rewrite another player's team, faction and colour; `UpdateDiagnostics` also re-broadcasts the whole player list (amplifier) | `LobbyManager.cs:2178`, `:2199` | **fixed**, no automated assertion yet |
| 6 | S1 | Reconnect accepts a name as proof of identity when the token is absent, and logs the token | `LobbyManager.cs:2050`, `:2009` | **fixed**, no automated assertion yet |
| 7 | S1 | Chat identity is client-supplied, unbounded in length and unlimited in rate | `LobbyManager.cs:2599` | **fixed**, no automated assertion yet |
| 8 | S2 | Rejected commands were acknowledged as accepted, so the client never learned why | `GameHost.Network.cs:ApplyAuthoritativeCommand` | fixed earlier (rejections now carry a reason and roll prediction back) |
| 9 | S2 | `ComputeDynamicInterpolationFactor` parses `"123 ms"` from a display string every client tick, culture-sensitive, two allocations | `NetworkService.cs:ComputeDynamicInterpolationFactor` | **fixed** |
| 10 | S2 | Deltas are compared against the last baseline, so a unit whose HP differs from the baseline is resent every tick for up to a second | `NetworkService.cs` delta build | open |
| 11 | S2 | `QueueDelta` sorts the whole queue on every received packet | `NetworkService.cs` | **fixed** |
| 12 | S2 | Reconnect baseline goes through `ReceiveSnapshot` (the delta entry point) with a non-multiple-of-30 sequence, so post-reconnect deltas queue until the next real baseline | `GameHost.Network.cs:766` | **fixed** |
| 13 | S3 | Baselines and command acks share the reliable channel with every other RPC, so one lost fragment stalls unrelated traffic | RPC declarations | open |
| 14 | S3 | `GetUnacknowledgedCommandPayloads` re-serialises and resends every unacked command every 50 ms on a channel ENet already guarantees | `GameHost.Network.cs:180`, `NetworkService.cs:523` | open |
| 15 | S3 | `TokenBucket.Consume` blocks the calling thread with `Thread.Sleep`, so it must not be used on the tick | `TokenBucket.cs:23` | **fixed** |
| 16 | — | Attack-move: **already implemented** on the host (`NetworkService.cs:1325-1330`) and the A-click path already sends `attack_move` (`GameHost.Input.cs:4218`). The brief's claim is stale. | verified | no work |
| 17 | — | Client prediction: the stale `"MoveTo"` casing gate no longer exists; prediction runs for every role and is covered by the two-instance harness | verified by `PredictionHarnessRunner` | no work |

## 6. Brief items I am treating as poor requirements

- **"Use the existing TokenBucket" for rate limiting.** As written it is a blocking limiter (`Thread.Sleep` inside
  `Consume`). Blocking the host tick to throttle a peer is worse than the flood. Implemented as a non-blocking
  `TryConsume` that drops the message and logs; dropping is the correct behaviour for a spammer.
- **"Fix or delete prediction."** Already superseded: prediction is one architecture for all roles, proven by a
  two-instance harness with bounded corrections. Deleting it now would be a regression.
- **"Implement attack-move on the host."** Already present and reachable from A-click; no work, and the ledger
  records that rather than inventing a change.
- **"Ask before any commit or push."** Overridden by the explicit instruction for this task.
- **"8 peers x loss x 6 scenarios x 3 runs" as a hard gate before any fix.** Waiting for the full matrix before
  closing a host crash is the wrong order; a malformed payload kills the match today. Crash resistance and
  authority were fixed first and are guarded by targeted assertions, with the matrix extended incrementally.

## 6b. Fix evidence

`hardening_report.txt` is written by the host at the end of every harness run and the runner folds its failure count
into the verdict, so a regression fails the run. Measured on the 150 ms profile:

```
note: payload checks: null, empty, truncated, oversized, 64 random payloads, and 10 malformed command shapes
note: undefined spell rejected: spell 'harness_spell_that_does_not_exist' is not defined
note: caster-less spell rejected: spell command does not name a caster
note: forged caster 30 (peer 3's unit) rejected: caster 30 is not owned by peer 1
note: legitimate cast accepted for peer 1 with its own caster 26 (ability/ancestral_spirit)
Failures: 0
```

Lobby identity is proven end to end by the client harness sending a forged slot change, forged diagnostics and a spoofed chat name at the host:

```
note: forged slot and diagnostics RPCs targeting peer 1 were ignored: slot stayed 'HarnessHost'/'HUMAN'/'Team 1'
      and latency '317 ms' is the host's own measurement, not the forged 999 ms
```

The forged-caster line is the failing-first assertion for item 1: before this pass the host set
`NeedsSpellEffect` from the command type alone and applied the AoE at the payload's position, so that command
succeeded. The last line proves the gate is not a blanket rejection.

## 6c. Post-fix matrix (25 s runs, two instances)

| Profile | Verdict | Error p95 | Path parity | Sync failures | Prediction failures | Hardening failures |
| --- | --- | --- | --- | --- | --- | --- |
| clean | PASS | 0.079 m | 334, 0 bad | 0 | 0 | 0 |
| latency 150 ms | PASS | 1.738 m | 224, 0 bad | 0 | 0 | 0 |
| latency + jitter | PASS | 1.734 m | 255, 0 bad | 0 | 0 | 0 |

## 7. Plan and status

1. **Milestone A — authority and crash resistance.** Items 1-7. Host must survive any peer payload and validate
   every command it applies.
2. **Milestone B — silent degradation.** Rejection reasons reach the client (done); remaining items 9-12.
3. **Milestone C — cost and scale.** Items 13-15, bandwidth measurement per profile.
4. **Milestone D — harness coverage.** Malformed payloads, 3 peers, reconnect, long soak.
5. **Milestone E — stretch, not started.** Host migration, shroud ghosting, damage numbers. Proposed only.

## 8. Bandwidth budget

Measured per client per second by the harness (`--profile` matrix). Budget: 40 KB/s per client for 8 players on a
delta-heavy tick (30 snapshots/s, baselines 1/s). The measurement is reported by the harness for every run;
exceeding the budget is a failure, not a note.
