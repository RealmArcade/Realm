# Concurrency & Thread Synchronization — review of the current match loop

Measured against `AGENTS.md` / _Match-Critical Concurrency & Thread Ownership_ as it stands today.
`docs/concurrency-audit.md` records what an earlier pass fixed; this document is a fresh read of the
code as it is now, and every item below was verified against the source rather than inherited from
that list.

Scope reviewed: `Realm.ListenServer` (simulation, snapshot publishing, script host, logging,
recording), `Realm.ListenServerHost` (transport), `Realm.Ecs` (world ownership, services, shared
memory, payload stores), `Realm.Shared/Network` and `Realm.Shared/Diagnostics`, and the client match
path (`GameHost`, `ClientServerSession`, `NetworkService`, `SnapshotTimeline`/presentation, `InGameHUD`,
`ReplayRecorder`). Editor, lobby, asset distribution and other tooling were read only where they share
the match thread.

## Thread inventory (the baseline every finding is measured against)

The authority is a Godot process with one lane that owns everything on the clock, plus three
background lanes:

| Lane | Owns | Reaches the match loop? |
| --- | --- | --- |
| Godot main thread (`_PhysicsProcess` → `Tick`) | ECS, WASM linear memory, script host, all peer bookkeeping, peer RPC callbacks, publish fork-join | it *is* the match loop |
| Publish lanes (`Parallel.For` in `PublishTick`) | read-only world access, one builder/frame slot/compressor per lane | forked from the tick, joined inside it |
| ECS service workers (`ParallelRunner` in movement, combat, client render) | assigned work slots only | forked from the tick / render frame |
| `AuthorityLogWriter` thread | the log file | queue only, never blocks |
| `AuthoritySyncRecordWriter` thread | the tick record file | queue only, never blocks |
| WASM watchdog timer callback | `IncrementEpoch` under `_watchdogGate` | gate is never taken by the tick |
| Client main thread | client ECS, presentation, HUD, replay record | it *is* the client match loop |
| `ReplayRecorderWriteThread` | the replay file | queue only, never blocks |

Rule reference below: **[A]** scope · **[B]** ownership over synchronization · **[C]** one writer per
world · **[D]** non-blocking execution · **[E]** immutable handoff and described publishing ·
**[F]** static mutable state · **[G]** Godot thread affinity · **[H]** no blocking logging or I/O ·
**[I]** synchronization exceptions.

## What landed

The per-item text below records the code as it was found, which is what makes each fix reviewable. Every
item has since been fixed, in these checkpoints:

| # | Finding | Commit |
| --- | --- | --- |
| 1 | Per-tick `ContentIdStore` lock in the publish lanes | `5fd203a1` |
| 2 | Traffic report written from inside the tick | `e7010a17` |
| 3 | Map-script saved data read and written on the tick | `e7010a17` |
| 4 | `WholeBoardGate` serialized every lane | `245e75db` |
| 7 | Leftover `_abilityLock` in the publish path | `245e75db` |
| 5 | Publish fork never declared the worker lease | `245e75db` |
| 6 | Unbounded replay queue, `GD.*` from its writer | `f8640cc5` |
| 17 | Replay object pool never trimmed | `f8640cc5` |
| 8 | `[ThreadStatic]` compressors with no owner | `5e8ea730` |
| 9 | `DeterministicRandom.Shared` built by whichever thread read it | `5e8ea730` |
| 10 | Replicated slot table ready before it was filled | `5e8ea730` |
| 11 | Traffic recorder grew with match length | `fbeaa68e` |
| 12 | Worker sampled terrain through a `Node` | `fbeaa68e` |
| 13 | Cold lane locks (stripe and buffer creation) | `245e75db`, `fbeaa68e` |
| 14 | `async void` in the in-game HUD | `fbeaa68e` |
| 15 | WASI log read on the tick (once a second, not only on traps) | `fbeaa68e` |
| 16 | `ConcurrentDictionary` for peer state | `fbeaa68e` |

Two findings were worse than first recorded and are worth naming: item 15 was not only a trap-path
read - `WasmScriptHost.Update` flushed the guest log every thirty ticks, so the match read a file once a
second; and item 11 could not simply be capped, because `MultiplayerEcsSyncTestSupport` reads the
coverage file to prove no sequence went unsent, so the recorder now keeps runs and expands them only in
the final report.

The rules themselves were revised in `AGENTS.md` to say what this pass had to decide: one writer per
world rather than "the simulation thread" (the client writes cosmetic components on the main thread),
atomic single values instead of a blanket single-writer requirement, the worker lease as the only
sanctioned way to fork over a world, an explicit in-tick budget for describing and framing instead of
`[D]` and `[E]` contradicting each other, no `Node` receiver inside a worker, no exemption for
recorded artifacts, and lazy statics that are built whole before the clock starts.

---

## The checklist, in priority order

### P0 — breaks a rule on the hottest path, every tick

**1. Every publish lane takes the process-wide `ContentIdStore` lock for every peer, every tick.**

- Where: `AuthoritativeSnapshotBuilder.AppendTextEntries` (`AuthoritativeSnapshotBuilder.cs:279-285`,
  called unconditionally from `BuildInto` at `:202`) → `StringRegistry.GetEntriesSince`
  (`Realm.Ecs/Shared/StringRegistry.cs:137`) → `ContentIdStore.GetTextEntriesSince`
  (`Realm.Ecs/Shared/ContentIdStore.cs:310-324`), which begins with `lock (_lock)`.
- Why it breaks: **[I]**. This is a single `object` monitor in a shared assembly, acquired once per
  peer per snapshot inside the fork-join, so `N` lanes serialize on it for `peerCount` acquisitions
  every tick, and it is acquired even when the text table has not grown since the peer's cursor —
  which is the permanent steady state. It is the same shape as the `ContentIdStore` finding the
  previous pass fixed, reached through the one call site that pass did not cover.
- Fix, in two steps:
  1. Fast path: `int entryCount = StringRegistry.EntryCount;` and return immediately when
     `cursor >= entryCount` (advancing `state.StringTableCursor` to `entryCount`), so the lock is only
     taken on the rare tick a map registers new text.
  2. Publish an immutable `(int Id, string Text)[]` from `ContentIdStore` on registration
     (`Volatile.Write` on a read-only array) and copy from that snapshot in `GetTextEntriesSince`, so
     the reader needs no lock at all. Registrations are load-time and script-driven, not per-tick.
- Done when: `GetTextEntriesSince` takes no lock when the cursor is at the end of the table, and a
  steady-state tick takes zero `ContentIdStore` lock acquisitions (assert in a debug counter).

**2. `--record-authority` writes three report files from inside the tick.**

- Where: `PublishTick` → `if (_trafficRecorder != null && sequence % TrafficInterval == 0)
  WriteTrafficReport();` (`ListenServerHost.cs:1106`) → `WriteTrafficReport` (`:170-177`) →
  `SnapshotTrafficRecorder.WriteReport` (`Realm.Shared/Diagnostics/SnapshotTrafficRecorder.cs:132`)
  → `WriteAtomically` at `:238-243`, three `File.WriteAllText` + `File.Move` pairs. Also called from
  `Stop` (`:495`) and `Dispose` (`:517`).
- Why it breaks: **[H]** — synchronous disk I/O on the tick thread, with three file writes and three
  renames every ten seconds, all on a thread that also drives rendering. The section names "logging,
  replay recording, or file operations" explicitly, and a diagnostics feature is not exempt.
- Fix: render the report into a `byte[]` on the tick (that part is pure CPU and bounded), then hand it
  to the same bounded producer-consumer pattern `AuthoritySyncRecorder` already uses — a small
  `BlockingCollection` with `TryAdd`, a drop counter, and a dedicated `IsBackground` writer.
- Done when: the tick path allocates the report and returns; no `File.*` call is reachable from
  `PublishTick`.

**3. A map script's saved data is read and written on the tick thread, synchronously.**

- Where: `ServerGameAPI.WriteSavedData` / `ReadSavedData`
  (`Realm.ListenServer/ServerGameAPI.Data.cs:7-15`) → `ServerSavedData`
  (`ServerSavedData.cs:19-37` `File.WriteAllText` + `CreateBackup`'s `File.Copy`, `:48-55`
  `File.ReadAllText`). These are `IGameAPI` members, so a script reaches them from `update()` inside
  `ExecuteGuardedAction` — i.e. inside `ServerSimulation.Step()`.
- Why it breaks: **[H]**, and it is script-controlled: a map that saves every tick stalls the match at
  disk speed, and the engine has no bound on how often it is called.
- Fix: an in-memory map of `(mapName, fileName) → content` that `ReadSavedData` serves directly (the
  authority is also the only reader), plus a bounded writer that coalesces by file name (last write
  wins) and drains off-thread. `CreateBackup` and the timestamped copy move to the writer. Define the
  overflow policy: coalescing means the queue is bounded by the number of distinct files, and a
  dropped write must be logged with the file name rather than silently lost.
- Done when: `IGameAPI` saved-data calls touch no `File.*` API, and a script that writes the same file
  every tick produces one write per drain, not one per tick.

### P1 — a lock or a wait in the match loop that the rule does not allow

**4. `WholeBoardGate` serializes every lane whenever the board is described whole.**

- Where: `AuthoritativeSnapshotBuilder.CollectDescribedUnits` branches to `CollectWholeBoard` when
  `_context.ShroudDisabled || viewerPlayer == Entity.Null` (`:430`), and `CollectWholeBoard`
  (`:444-476`) holds `lock (_descriptions.WholeBoardGate)` (`SnapshotDescriptionCache.cs:47`) across a
  full `_world.Query` plus two list copies.
- Why it breaks: **[I]** and **[D]**. With shroud off — the default in most maps — every peer's
  description takes the same monitor, so the fork-join degenerates into `peerCount` serial passes over
  the whole board.
- Fix: collect the whole-board set once on the tick thread before the fork and publish it immutably
  for the lanes, exactly as ability deltas are already handled by
  `ServerSimulation.PrepareAbilityDeltas` (`ServerSimulation.cs:369-385`). The lanes then copy from a
  `ReadOnlySpan`/array nobody writes, and the gate disappears from the hot path.
- Done when: no lock is acquired inside `BuildInto` on the whole-board path.

**5. The publish fork never declares a worker lease, so the structural-write assertion cannot see it.**

- Where: `ListenServerHost.cs:1094-1100` calls `Parallel.For` directly; the lease is only entered by
  `ParallelRunner.Execute(count, body, world)` (`Realm.Ecs/Common/ParallelRunner.cs:38-56`), and
  `EcsWorld.AssertNotForkedIntoWorkers` is the debug guard that catches a worker writing the world
  (`EcsWorld.cs:86-102`, on `Set`/`Add`/`Remove`/`Destroy`/buffer creation).
- Why it breaks: **[C]** as enforced. The lanes are read-only today, but the one mechanism that would
  catch a lane that stops being read-only is not armed on the path most likely to change.
- Fix: enter/exit the lease around the publish fork (either call `ParallelRunner` with a manual
  threshold of `laneCount`, or wrap the existing `Parallel.For` in `try/finally` with
  `EnterWorkerLease`/`ExitWorkerLease`). Then confirm a debug build stays quiet.
- Done when: a deliberately-added `world.Set` inside `BuildInto` fails a debug run.

**6. The client's replay recorder has an unbounded producer queue and logs from its writer thread.**

- Where: `Realm.Client/ReplaySystem/ReplayRecorder.cs:22` `ConcurrentQueue<ReplayFrame>` with no
  capacity; `RecordTick` (`:92-110`) enqueues unconditionally; `WriteLoop` calls `GD.PrintErr`
  (`:175-177`) from `ReplayRecorderWriteThread`.
- Why it breaks: **[E]** ("define overload behavior so publishing cannot accumulate unbounded work"),
  **[H]** (a stalled disk grows the queue without bound, and the frames are pooled so the memory is
  never returned), **[G]** (`GD.*` logging off the main thread).
- Fix: bound the queue with a stated drop policy (drop the oldest frame and count it — the replay is a
  diagnostic artifact and a missing frame is honest), and route the error out through a plain file
  writer the thread owns or a `CallDeferred` to the main thread. Do not call `GD.*` from the thread.
- Done when: the queue has a bound, `DroppedFrameCount` is reported at stop, and no `GD.*` call exists
  on the writer thread.

**7. `_abilityLock` is unreachable from the match and still sits in the publish path.**

- Where: `SnapshotFanOut._abilityLock` (`SnapshotFanOut.cs:22`) and
  `AuthoritativeSnapshotBuilder.cs:212-215` fall back to `lock (_abilityLock)` only when
  `preparedAbilityDeltas == null`, which happens only via the non-lane `BuildSnapshotForPeer`
  (`ServerSimulation.cs:357-360`) — called solely by `Realm.ReplicationVerifier/MapSmokeRun.cs:69`.
- Why it breaks: **[I]** in intent if not in practice: a lock object exists in the publish types with
  no live justification, and any future caller reintroduces the stall the previous pass removed.
- Fix: delete `_abilityLock` and make the prepared-delta list a required parameter; give the verifier
  its own single-peer path.
- Done when: `lock` no longer appears under `Realm.ListenServer/SnapshotFanOut.cs`.

### P2 — static state with no owner, and lifetime/ownership gaps

**8. `SnapshotWireFormat` keeps `[ThreadStatic]` compressors and a decompressor that nothing disposes.**

- Where: `Realm.Shared/Network/SnapshotWireFormat.cs:58-68` (four `[ThreadStatic]` fields), the static
  `EncodeBaseline`/`EncodeDelta` path (`:70-88`) and the `_decompressor` use at `:150` and `:168`.
- Why it breaks: **[B]**, **[E]**. The match only uses `SnapshotCompressor`, which has a real owner
  (`ListenServerHost._publishCompressors`, disposed at `Dispose`); these statics are reached only by
  `Realm.Benchmarks`, yet they are declared in the assembly the match runs, have no defined lifetime
  and hold native zstd handles until the process exits.
- Fix: delete the static encode path (move the benchmark onto `SnapshotCompressor`), and give the
  client's decode a decompressor owned by `ClientServerSession` and disposed with it.
- Done when: `[ThreadStatic]` appears nowhere under `Realm.Shared/Network`.

**9. `DeterministicRandom.Shared` is a lazily initialised static with no synchronization.**

- Where: `Realm.Ecs/Shared/DeterministicRandom.cs:21,42-56` — `if (shared == null) { shared = new ...;
  _shared = shared; }` with a plain static field.
- Why it breaks: **[F]**. Two threads can each construct a stream and one overwrites the other, which
  is a silently diverging sequence rather than a race that fails loudly. Today the authority and the
  guest each touch it from one thread, but `Shared` is reachable from any code in `Realm.Ecs`,
  including the parallel AI/training paths.
- Fix: seed it explicitly once during match setup on the owning thread
  (`DeterministicRandom.ResetForMatch(seed)` style), or publish with
  `Interlocked.CompareExchange(ref _shared, created, null)`.
- Done when: the field is written exactly once under a documented owner.

**10. `ReplicatedUnitComponentSlots` marks itself initialised before it is populated.**

- Where: `Realm.Ecs/Replication/ReplicatedComponents.cs:791-810` — `_initialized = true;` precedes the
  fill of `_slotByComponentTypeId` and `_slots`, with no lock.
- Why it breaks: **[F]**. A second thread that enters between the flag and the fill sees an empty
  table and caches the wrong answer (or `TryGetSlot` returns false and the codec is described without
  its presence bit). It is read from `AuthoritativeSnapshotBuilder`'s static initialiser, which CLR
  serialises — so this is latent, not live, but it is one refactor away from being read from a lane.
- Fix: build the tables in a static initialiser or behind `Lazy<T>`, and publish the completed
  read-only tables.
- Done when: the initialisation has no partially-visible state.

**11. The traffic recorder grows for the whole match with no bound.**

- Where: `SnapshotTrafficRecorder._sentSequencesByPeer`
  (`Realm.Shared/Diagnostics/SnapshotTrafficRecorder.cs:243-247`) appends one `int` per peer per
  sequence, and the coverage file expands them all (`:223-229`).
- Why it breaks: **[E]** memory bound. At 30 Hz a one-hour match holds ~108k entries per peer.
- Fix: keep a ring of the last N sequences per peer plus a count, or stream the coverage file from the
  writer thread as it is recorded.
- Done when: the recorder's memory is independent of match length.

**12. A worker calls a method on a `Node` and is safe only by accident.**

- Where: `Realm.Client/Core/GameHost.Simulation.cs:1035-1040` — `ComputeDirectionalRotationBasisParallel`
  runs inside `ParallelRunner` and calls `_renderGroundTerrain.GetHeightAndNormal(...)` on a
  `RuntimeTerrain` (`RuntimeTerrain.cs:3435`), which is a Godot `Node`.
- Why it breaks: **[G]**. `GetHeightAndNormal` happens to touch only managed arrays (`Cells`,
  `QuadSize`) and calls no Godot API, but nothing in the type states that, so the first person to add a
  `GD.` call or a `LocalToMap()` inside it turns this into a cross-thread native call.
- Fix: move the sampling to a `public static` helper over `TerrainCell[,]`, `quadSize` and dimensions,
  and capture those into the work item before the fork. A worker then cannot reach a `Node` at all.
- Done when: no `Node`-typed receiver appears in a `ParallelRunner` body in the client.

### P3 — cold-lane locks, lifetime smells, and observability

**13. Cold locks are still reachable from the lanes.** `SnapshotDescriptionCache` stripe locks
(`:97`, `:126`, `:156`) and `EcsWorld._componentBufferCreationLock` (`EcsWorld.cs:714`) are only hit on
the first tick a unit is described or the first time a queried component type is touched. Both are
justified, neither is documented where the lane reaches it. Fix: warm every codec buffer and query type
during match setup, and add one comment block on the stripes naming the reason and the expected
acquisition count (zero in steady state).

**14. `async void` in the in-game HUD.** `InGameHUD.RegenerateMinimapBackground`
(`Realm.Client/UI/InGameHUD.cs:1101`) is `async void` on the match path: it re-enters by flag rather
than by guard, and its completion and exception are unobservable to every caller. Fix: return `Task`
and let the single caller await it, or keep the flag and add a `TaskCompletionSource` for shutdown
ordering.

**15. Trap-path file reads on the tick thread.** `WasmScriptHost.FlushWasiLogFile`
(`WasmScriptHost.cs:273-300`) opens and reads the WASI log file synchronously from
`ExecuteGuardedAction`'s `catch` blocks, i.e. on the tick thread, once per fault. Acceptable only
because it is once per fault; state that, or read it on the writer lane.

**16. A `ConcurrentDictionary` where a fixed array would do.** `SnapshotPeerStateStore`
(`SnapshotPeerStateStore.cs:10`) is a `ConcurrentDictionary<int, SnapshotPeerState>` for at most
`EcsWorld.MAX_PLAYERS` entries, addressed by peer id from the tick thread and the lanes. It works, but
[I] asks a concurrent collection in the loop to justify itself; an array indexed by player slot (peer
ids are assigned per match) has no such question. Fix when the peer-slot mapping is next touched.

**17. The replay object pool is never trimmed.** `ReplayObjectPool`
(`Realm.Client/ReplaySystem/ReplayObjectPool.cs:7-13`) holds three static `ConcurrentBag`s sized to
peak capacity for the life of the process. Fix: trim on stop, or clear the bags when a match ends.

---

## Checks to run once the fixes land

Nothing here needs a test suite; these are inspection gates that can be scripted in CI:

1. `rg "lock \(" Realm.ListenServer Realm.Client/ReplaySystem Realm.Shared/Diagnostics` — every hit
   should be one of the documented cold-path locks.
2. `rg "File\.(Write|Read|Copy|Move|Delete)" Realm.ListenServer` — no hit may be reachable from
   `ServerSimulation.Step`, `PublishTick`, or an `IGameAPI` implementation.
3. `rg "\[ThreadStatic\]|ConcurrentBag|ConcurrentDictionary|BlockingCollection" Realm.ListenServer
   Realm.Shared/Network` — each hit names its owner and its bound.
4. Debug build: add a temporary `world.Set` inside `AuthoritativeSnapshotBuilder.BuildInto` and confirm
   the lease assertion fires (checklist item 5).
5. A counter of `ContentIdStore` lock acquisitions per tick, expected 0 in steady state (item 1).
