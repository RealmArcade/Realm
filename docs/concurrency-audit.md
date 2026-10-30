# Concurrency & Thread Synchronization — findings and what landed

Measured against the `AGENTS.md` section _Match-Critical Concurrency & Thread Ownership_.

That section governs only what a match runs on the clock: authority simulation and snapshot
publishing (`Realm.ListenServer`), and the client's match path (`GameHost`, network channel,
presentation, in-game HUD). Editor, menus and lobby, asset distribution, and other tooling are
outside it, so a finding there is review taste rather than a rule break.

Every finding below was reviewed against that scope, then fixed in the order it is listed. Each
checkpoint built clean and was committed and pushed on its own.

## Rule reference

**[A]** scope · **[B]** ownership over synchronization · **[C]** simulation-owned ECS ·
**[D]** non-blocking execution · **[E]** immutable handoff and asynchronous publishing ·
**[F]** static mutable state · **[G]** Godot thread affinity · **[H]** no blocking logging or I/O ·
**[I]** synchronization exceptions.

---

## Fixed

| # | Finding | Rule | Commit |
| --- | --- | --- | --- |
| P0-3 | `EcsWorld` had no ownership contract, and a worker that was first to touch a component type created its buffer and membership bitset by two unsynchronized writes | [C] | `f12be77a` |
| P0-4 | The ambient world was a plain static, and both payload registries cached the world and its store in two fields that a reader could pair across a swap | [F], [B] | `db1839b7` |
| P0-2 | `SnapshotDescriptionCache` lent a `ReadOnlySpan<byte>` over a payload the next tick overwrote, and took five striped locks per unit per tick to do it | [E], [I] | `8de682f6` |
| P0-1 | Publishing waited up to five seconds for a borrowed builder and then queued every lane behind one lock for ability deltas | [D], [E], [I] | `5935daad` |
| P1-1 | The WASM watchdog read its timeout with no visibility edge and could call into the engine after disposal had freed it | [D], [B] | `b0f887ae` |
| P1-2 | A dropped order wrote to disk, under a lock, with `AutoFlush`, on the tick thread | [H], [D] | `b0f887ae` |
| P1-3 | `--record-authority` wrote the whole tick record the same way | [H] | `b0f887ae` |
| P1-4 | `ContentIdStore` guarded every lookup with one process-wide lock that the publish lane hit per unit per codec | [I], [D] | `ce644205` |
| P1-5 | `ColdPayloadStore.Get` locked on the simulation path with no competing writer to justify it | [I], [B] | `ce644205` |
| P2-1 | `ComponentTypeRegistry` assigned dynamic ids on first touch | [F] | `e3cae292` |
| P2-2 | The combat tick held two `ConcurrentDictionary` caches that only it touches | [I] | `e3cae292` |
| P2-3 | Payload buffers were allocated under a lock, and the per-peer frame allocation had no stated reason | [F] | `8de682f6`, `6c51dc61` |
| P2-4 | The replay recorder's frame count crossed threads with no visibility edge | [B], [D] | `e3cae292` |
| P3-1 | `EcsSharedMemory` documented no writer | [B] | `e3cae292` |
| P3-2 | Wire-format compressors were `[ThreadStatic]` and never disposed | [B], [E] | `e3cae292` |
| P3-3 | The diagnostics loss walk read the received array outside the gate that guards it | [E] | `e3cae292` |

## What the fixes turned on

Three shapes account for all sixteen:

- **Publish, then never mutate.** The description cache and the cold payload store both handed out
  state that a later writer could still edit. Both now write into the generation that is not
  published and move an index, so a reader's view is immutable for as long as it holds it.
- **Fixed lanes, owned builders.** Publish work is assigned to lanes up front; a lane owns its
  builder, its frame slot, and its compressors for the whole match, so nothing queues and nothing
  borrows. Ability deltas are collected once on the tick thread before the fork, because the
  registry behind them has one scratch table per process.
- **Name the writer.** `EcsWorld`, `EcsSharedMemory` and the component type registry all followed a
  single-writer rule that was only in the heads of whoever wrote them. The rule is now in the type,
  and a structural write from inside a fork-join is reported in debug builds rather than corrupting a
  buffer a worker is reading.

## Not done, deliberately

**Publishing still happens on the tick thread.** All of its blocking was removed - no borrowed
builders, no ability lock, no lock-per-unit cache - so what remains inside `PublishTick` is bounded
fork-join work over per-peer slots, which [D] permits explicitly.

Moving it fully asynchronous is a larger change than the rest of this list combined, and it cannot
be done safely as it stands: a publish lane reads the ECS world, the simulation thread is that
world's only writer, and the tick is the only point at which the world is consistent. Making
publication overlap simulation therefore requires either double-buffering the world itself or
handing completed descriptions to a lane that only serializes and sends - and the latter still has
to serialise against the per-peer `SentUnits` / `SentPayloads` state that the next description
mutates. That is a design decision worth taking on its own, with the benchmark harness in front of
it, rather than as the tail of this checklist.

## Parked — outside the section's scope

Recorded so the work is not lost, in the order it would matter if these ever reach the match loop.

| Was | Where | Why parked |
| --- | --- | --- |
| `RebuildIndexFromCas` holds one lock across a rebuild and a full hash verify | `AssetIndexService.cs:823-869` | editor |
| Two undocumented static lock orders in the same service | `AssetIndexService.cs:1290-1296`, `:1007-1053`, `MapAssetManager.cs:114-135` | editor, asset distribution |
| Background scans take the reader's lock per file | `AssetIndexService.cs:1698-1718`, `:1786-1819` | editor |
| Per-hash lock that protects nothing, held across file IO | `ContentAddressableStorage.cs:127-141` | asset distribution |
| `ArchiveLock` spans whole-archive deletes while extraction runs unlocked | `MapAssetManager.cs:112-135`, `:725-755`, `:307-320` | asset distribution |
| Godot `Image` decoded on a pool thread | `AssetThumbnailProvider.cs:323-395` | editor; a moment of editor delay is acceptable |
| Global `Mutex` held for the whole GLB rigging pipeline | `GlbAutoRigger.cs:8-80` | tooling |
| Two owners for one `MultiplayerPeer` | `PeerSeederManager.cs:36-71` | lobby, pre-match |
| Lobby background loops read node state, `task.Wait(2000)` on exit | `LobbyManager.cs:244-259`, `:1382-1392`, `:387-395` | lobby, pre-match |
| Fire-and-forget tasks with unobserved exceptions | `Realm.AdminServer/*`, `SaveLoadService.cs:493` | tooling |
| Launcher shutdown flag not volatile | `ServerProcessLauncher.cs:142,363,433` | launcher process, not the match |

Two are worth a sentence in the section if they are ever to be covered: cross-process mutexes
(`GlbAutoRigger`) and pre-match lobby threads, neither of which any current bullet mentions.

## Locks still reachable from the match loop

[I] requires each of these to justify itself. What is left after the fixes:

| Lock | Reached from | Why it stays |
| --- | --- | --- |
| `ContentIdStore._lock` | registration only | resolving a hash collision has to be atomic with the lookup that found it; reads no longer take it |
| `ColdPayloadStore._writeLock` | payload registration, release | one writer for the handle table; reads publish an immutable view |
| `WorldPayloadStores._lock` | first resolution of a payload type | lazy type map, taken once per type |
| `SnapshotDescriptionCache` stripes | payload write side | two peers describing the same unit must not both be first; reads take no lock |
| `ComponentTypeRegistry._lock` | first touch of a dynamic type | lazy id assignment; ids never cross a process |
| `WorldPayloadStores.Reset` | world teardown | ordered before the stores it resets |
