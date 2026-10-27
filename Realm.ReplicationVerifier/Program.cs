using System.Reflection;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Common;
using Realm.Ecs.Replication;

int failures = 0;
int checks = 0;

void Check(bool condition, string description)
{
	checks++;
	if (condition) return;
	failures++;
	Console.WriteLine($"FAIL: {description}");
}

var world = new EcsWorld();
var codecs = ReplicatedComponents.All;

Check(codecs.Count > 0, "replicated component registry is populated");

var duplicateIds = codecs.GroupBy(codec => codec.ComponentTypeId).Where(group => group.Count() > 1).ToList();
Check(duplicateIds.Count == 0, $"component type ids are unique ({string.Join(", ", duplicateIds.Select(g => g.Key))})");

var addMethod = typeof(EcsWorld).GetMethods(BindingFlags.Public | BindingFlags.Instance)
	.First(method => method.Name == "Add" && method.IsGenericMethodDefinition && method.GetParameters().Length == 2);

foreach (var codec in codecs)
{
	var componentType = typeof(EcsWorld).Assembly.GetTypes().FirstOrDefault(candidate =>
		candidate.Name == codec.ComponentName &&
		candidate.Namespace != null &&
		candidate.Namespace.StartsWith("Realm.Ecs.Components", StringComparison.Ordinal));
	Check(componentType != null, $"{codec.ComponentName}: codec resolves to a component type");
	if (componentType == null) continue;

	var entity = world.Create();
	addMethod.MakeGenericMethod(componentType).Invoke(world, new object[] { entity, Activator.CreateInstance(componentType)! });

	bool isTag = codec.GetType().IsGenericType && codec.GetType().GetGenericTypeDefinition() == typeof(TagComponentCodec<>);
	if (isTag)
	{
		var present = new byte[] { 1 };
		var absent = new byte[] { 0 };
		codec.Apply(world, entity, present);
		var roundTrip = new byte[codec.SizeInBytes];
		Check(codec.TryWrite(world, entity, roundTrip), $"{codec.ComponentName}: tag survives apply");
		Check(roundTrip.SequenceEqual(present), $"{codec.ComponentName}: tag presence round-trips");
		codec.Apply(world, entity, absent);
		Check(!codec.TryWrite(world, entity, roundTrip), $"{codec.ComponentName}: cleared tag stops replicating");
		continue;
	}

	var payload = new byte[codec.SizeInBytes];
	new Random(7919 + codec.ComponentTypeId).NextBytes(payload);
	codec.Apply(world, entity, payload);
	var encoded = new byte[codec.SizeInBytes];
	Check(codec.TryWrite(world, entity, encoded), $"{codec.ComponentName}: encodes after apply");

	if (codec is INormalizingComponentCodec)
	{
		codec.Apply(world, entity, encoded);
		var reencoded = new byte[codec.SizeInBytes];
		Check(codec.TryWrite(world, entity, reencoded) && encoded.AsSpan().SequenceEqual(reencoded), $"{codec.ComponentName}: normalized payload re-encodes identically");
		continue;
	}

	Check(encoded.AsSpan().SequenceEqual(payload), $"{codec.ComponentName}: byte payload round-trips exactly");
}

var missingEntity = world.Create();
foreach (var codec in codecs)
{
	if (codec.GetType().IsGenericType && codec.GetType().GetGenericTypeDefinition() == typeof(TagComponentCodec<>)) continue;
	var buffer = new byte[codec.SizeInBytes];
	bool wrote = codec.TryWrite(world, missingEntity, buffer);
	Check(!wrote, $"{codec.ComponentName}: absent component is not replicated");
}

foreach (var codec in codecs)
{
	var removalComponentType = typeof(EcsWorld).Assembly.GetTypes().FirstOrDefault(candidate =>
		candidate.Name == codec.ComponentName &&
		candidate.Namespace != null &&
		candidate.Namespace.StartsWith("Realm.Ecs.Components", StringComparison.Ordinal));
	Check(removalComponentType != null, $"{codec.ComponentName}: removal codec resolves to a component type");
	if (removalComponentType == null) continue;

	var removalEntity = world.Create();
	addMethod.MakeGenericMethod(removalComponentType).Invoke(world, new object[] { removalEntity, Activator.CreateInstance(removalComponentType)! });
	codec.Remove(world, removalEntity);
	var removalProbe = new byte[codec.SizeInBytes];
	Check(!codec.TryWrite(world, removalEntity, removalProbe), $"{codec.ComponentName}: removal tombstones the component");
}

var deltas = new List<ComponentDelta>
{
	new() { Key = 0, ComponentTypeId = 1, Scope = (byte)ReplicatedScope.Player, Bytes = new byte[] { 1, 2, 3 } },
	new() { Key = 5, ComponentTypeId = 2, Scope = (byte)ReplicatedScope.Unit, Bytes = new byte[] { 9 } }
};
var serialized = MemoryPackSerializer.Serialize(deltas);
var restored = MemoryPackSerializer.Deserialize<List<ComponentDelta>>(serialized);
Check(restored != null && restored.Count == deltas.Count, "component delta list survives MemoryPack");
Check(restored![0].Bytes.SequenceEqual(deltas[0].Bytes) && restored[1].Key == 5, "component delta contents survive MemoryPack");

var mirrorA = new ReplicatedStateMirror();
var mirrorB = new ReplicatedStateMirror();
var forward = new[]
{
	new ComponentDelta { Key = 0, ComponentTypeId = 10, Scope = (byte)ReplicatedScope.Player, Bytes = new byte[] { 4, 5 } },
	new ComponentDelta { Key = 1, ComponentTypeId = 11, Scope = (byte)ReplicatedScope.Player, Bytes = new byte[] { 6, 7 } }
};
foreach (var delta in forward) mirrorA.RecordComponent(delta);
foreach (var delta in forward.Reverse()) mirrorB.RecordComponent(delta);
Check(mirrorA.ComputeFingerprint() == mirrorB.ComputeFingerprint(), "fingerprint is independent of delta arrival order");

mirrorA.RecordAbility(new AbilityStateDelta { ServerEntityId = 3, AbilityIndex = 0, Flags = 1 });
mirrorA.RecordText(new StaticTextDelta { Handle = 1, Text = "wave", PositionX = 1f, PositionY = 2f, PositionZ = 3f, Visible = 1 });
mirrorB.RecordAbility(new AbilityStateDelta { ServerEntityId = 3, AbilityIndex = 0, Flags = 1 });
mirrorB.RecordText(new StaticTextDelta { Handle = 1, Text = "wave", PositionX = 1f, PositionY = 2f, PositionZ = 3f, Visible = 1 });
Check(mirrorA.ComputeFingerprint() == mirrorB.ComputeFingerprint(), "fingerprint covers ability state and static text symmetrically");

uint baselineFingerprint = mirrorA.ComputeFingerprint();
mirrorA.RecordAbility(new AbilityStateDelta { ServerEntityId = 3, AbilityIndex = 0, Flags = 2 });
Check(mirrorA.ComputeFingerprint() != baselineFingerprint, "fingerprint detects an ability flag change");

mirrorA.RecordText(new StaticTextDelta { Handle = 1, Text = "wave", PositionX = 1.5f, PositionY = 2f, PositionZ = 3f, Visible = 1 });
uint textChanged = mirrorA.ComputeFingerprint();
mirrorA.RecordText(new StaticTextDelta { Handle = 1, Text = "wave", PositionX = 1.5f, PositionY = 2f, PositionZ = 3f, Visible = 0 });
Check(mirrorA.ComputeFingerprint() != textChanged, "fingerprint detects a static text visibility change");

uint componentFingerprint = mirrorB.ComputeFingerprint();
mirrorB.RecordComponent(new ComponentDelta { Key = 0, ComponentTypeId = 10, Scope = (byte)ReplicatedScope.Player, Bytes = new byte[] { 4, 6 } });
Check(mirrorB.ComputeFingerprint() != componentFingerprint, "fingerprint detects a changed component byte");

var unitScoped = new ReplicatedStateMirror();
unitScoped.RecordComponent(new ComponentDelta { Key = 9, ComponentTypeId = 12, Scope = (byte)ReplicatedScope.Unit, Bytes = new byte[] { 1 } });
Check(unitScoped.ComputeFingerprint() == ReplicatedStateFingerprint.Seed, "unit-scoped components stay out of the global fingerprint");

var payloadDeltas = new List<ComponentDelta>();
foreach (var codec in codecs)
{
	var bytes = new byte[codec.SizeInBytes];
	new Random(104729 + codec.ComponentTypeId).NextBytes(bytes);
	payloadDeltas.Add(new ComponentDelta { Key = codec.ComponentTypeId, ComponentTypeId = codec.ComponentTypeId, Scope = (byte)codec.Scope, Bytes = bytes });
}

var weaponSlots = new List<Weapon> { new Weapon(12f, maxRange: 6f), new Weapon(7f, maxRange: 9f) };
var firstWeapons = new Weapons(weaponSlots);
var secondWeapons = new Weapons(new List<Weapon>(weaponSlots));
Check(firstWeapons.Count == 2 && firstWeapons.Handle == secondWeapons.Handle, "identical weapon slots share one content id payload");
Check(firstWeapons.TryGet(0, out Weapon firstSlot) && firstSlot.BaseDamage == 12f && firstSlot.MaxRange == 6f, "a weapon slot round trips through the content id payload");
Check(!firstWeapons.TryGet(5, out _), "a missing weapon slot reports a miss");
Check(new Weapons(null).Handle == 0 && new Weapons(new List<Weapon>()).Count == 0, "an entity without weapons stores no payload");

var abilities = new List<AbilityStateDelta>
{
	new() { ServerEntityId = 7, AbilityIndex = 1, Flags = 2 },
	new() { ServerEntityId = 8, AbilityIndex = 1, Flags = 0 }
};
var texts = new List<StaticTextDelta>
{
	new() { Handle = 4, Text = "spawn 3", PositionX = 1f, PositionY = 2f, PositionZ = 3f, FontSize = 48f, Visible = 1 }
};

var hostMirror = new ReplicatedStateMirror();
ReplicatedPayloadReplay.Replay(hostMirror, payloadDeltas, abilities, texts, null);
uint hostFingerprint = hostMirror.ComputeFingerprint();

var clientMirror = new ReplicatedStateMirror();
var replayErrors = new List<string>();
ReplicatedPayloadReplay.Replay(clientMirror, payloadDeltas, abilities, texts, replayErrors);
Check(replayErrors.Count == 0, $"payload replay reports no structural errors ({string.Join("; ", replayErrors)})");
Check(clientMirror.ComputeFingerprint() == hostFingerprint, "a lossless replay reproduces the host fingerprint");

var lossyDeltas = new List<ComponentDelta>(payloadDeltas);
lossyDeltas.RemoveAll(delta => delta.Scope == (byte)ReplicatedScope.Player);
var lossyMirror = new ReplicatedStateMirror();
ReplicatedPayloadReplay.Replay(lossyMirror, lossyDeltas, abilities, texts, null);
Check(lossyMirror.ComputeFingerprint() != hostFingerprint, "a dropped player-scope delta is detected by the fingerprint");

var corruptDeltas = new List<ComponentDelta>(payloadDeltas);
int corruptIndex = corruptDeltas.FindIndex(delta => delta.Scope == (byte)ReplicatedScope.Player);
if (corruptIndex >= 0)
{
	var corrupt = corruptDeltas[corruptIndex];
	corrupt.Bytes = new byte[Math.Max(1, corrupt.Bytes.Length + 1)];
	corruptDeltas[corruptIndex] = corrupt;
}

var corruptErrors = new List<string>();
var corruptMirror = new ReplicatedStateMirror();
ReplicatedPayloadReplay.Replay(corruptMirror, corruptDeltas, abilities, texts, corruptErrors);
Check(corruptErrors.Count > 0, "a delta with the wrong byte length is rejected as a structural error");

var truncatedCatalog = new ReplicatedStateMirror();
ReplicatedPayloadReplay.Replay(truncatedCatalog, null, null, texts, null);
ReplicatedPayloadReplay.Replay(truncatedCatalog, null, null, new List<StaticTextDelta> { new() { Handle = 4, Destroyed = 1 } }, null);
Check(!truncatedCatalog.Texts.ContainsKey(4), "a destroyed static text leaves the mirror");
Check(truncatedCatalog.ComputeFingerprint() == ReplicatedStateFingerprint.Seed, "a fully destroyed text restores the empty fingerprint");

var coldPayloads = new List<ColdPayloadDelta>
{
	new() { Scope = (byte)ReplicatedScope.World, Key = 0, Kind = (byte)ColdPayloadKind.ContentIdPayload, ContentId = 42, Bytes = new byte[] { 1, 2, 3 } }
};
var coldMirror = new ReplicatedStateMirror();
var coldBaseline = new ReplicatedStateMirror();
ReplicatedPayloadReplay.Replay(coldBaseline, payloadDeltas, abilities, texts, null);
uint fingerprintWithoutColdPayload = coldBaseline.ComputeFingerprint();
ReplicatedPayloadReplay.Replay(coldMirror, null, null, null, null, coldPayloads);
Check(coldMirror.ComputeFingerprint() != fingerprintWithoutColdPayload, "a cold payload changes the fingerprint");
ReplicatedPayloadReplay.Replay(coldMirror, null, null, null, null, new List<ColdPayloadDelta>
{
	new() { Scope = (byte)ReplicatedScope.World, Key = 0, Kind = (byte)ColdPayloadKind.ContentIdPayload, Destroyed = 1 }
});
Check(coldMirror.ColdPayloads.Count == 0, "a cold payload tombstone removes the entry");
Check(ColdPayloadCodecs.ComputeContentId(new byte[] { 1, 2, 3 }) != ColdPayloadCodecs.ComputeContentId(new byte[] { 1, 2, 4 }), "cold payload content ids distinguish payload bytes");

var serializedLeaderboards = ColdPayloadCodecs.SerializeLeaderboards(new LeaderboardBoards());
Check(serializedLeaderboards.Length > 0 && ColdPayloadCodecs.ComputeContentId(serializedLeaderboards) != 0, "an empty leaderboard snapshot serializes to a stable payload");

var budgetWorld = new EcsWorld();
var budgetEntity = budgetWorld.Create();
budgetWorld.Add(budgetEntity, new MovementStats(3f, 4f, 5f));
var budgetCodec = ReplicatedComponents.Find(ComponentType<MovementStats>.Id)!;
var budgetBuffer = new byte[budgetCodec.SizeInBytes];
for (int warmup = 0; warmup < 100; warmup++)
{
	budgetCodec.TryWrite(budgetWorld, budgetEntity, budgetBuffer);
}

long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
for (int iteration = 0; iteration < 500; iteration++)
{
	budgetCodec.TryWrite(budgetWorld, budgetEntity, budgetBuffer);
}

long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
Check(allocatedBytes == 0, $"500 component writes into one reused buffer allocate nothing (allocated {allocatedBytes} bytes)");

EcsWorld.SetAmbient(world);
var emptyWorldEntity = world.Create();
Check(!world.Has<Realm.Ecs.Components.Core.PlayerPopulation>(emptyWorldEntity), "bare entity has no population component");

StringRegistry.Reset();
int goldId = StringRegistry.GetOrAdd("Gold");
Check(goldId != 0 && goldId == StringRegistry.ComputeId("Gold"), "string ids are content hashes");
Check(StringRegistry.ComputeId("Neutral") == 975127632, "text ids stay identical to the established hash");
Check(ContentIdStore.SchemeVersion == 1, "the identifier scheme version is pinned");
Check(ContentIdStore.GetOrAddBytes(ContentIdStore.WeaponListKind, new byte[] { 1, 2, 3, 4 }) == 721294077, "payload ids stay identical to the established hash");
Check(ContentIdStore.GetOrAddBytes(ContentIdStore.TextListKind, new byte[] { 1, 2, 3, 4 }) != 721294077, "payload ids include their kind");
Check(ContentIdStore.TryGetKind(goldId, out byte goldKind) && goldKind == ContentIdStore.TextKind, "registered text carries the text kind tag");
Check(StringRegistry.GetOrAdd("Gold") == goldId, "string ids are stable across repeated registration");
int woodId = StringRegistry.GetOrAdd("Wood");
Check(woodId != 0 && woodId != goldId, "distinct text yields distinct ids");

StringRegistry.Reset();
Check(StringRegistry.Get(goldId) == string.Empty, "a reset text table stops resolving text");
Check(StringRegistry.GetOrAdd("Gold") == goldId, "content hash ids survive a text table reset");
Check(StringRegistry.Get(goldId) == "Gold", "registering the text again restores it");
Check(StringRegistry.RegisterRemote(woodId, "Wood") && StringRegistry.Get(woodId) == "Wood", "text received from a peer resolves locally");
Check(!StringRegistry.RegisterRemote(woodId, "Stone") && StringRegistry.Get(woodId) == "Wood", "remote text never overwrites a known id");

var copiedEntries = new List<(int Id, string Text)>();
StringRegistry.CopyEntries(copiedEntries);
Check(copiedEntries.Count == StringRegistry.EntryCount && copiedEntries.TrueForAll(entry => StringRegistry.Get(entry.Id) == entry.Text), "published entries resolve back through the table");

var firstPublication = new List<(int Id, string Text)>();
int publicationCursor = StringRegistry.GetEntriesSince(0, firstPublication);
Check(firstPublication.Count == StringRegistry.EntryCount, "an empty cursor publishes the whole text table");

var emptyPublication = new List<(int Id, string Text)>();
Check(StringRegistry.GetEntriesSince(publicationCursor, emptyPublication) == publicationCursor && emptyPublication.Count == 0, "a cursor at the end publishes nothing");

int latePublishedId = StringRegistry.GetOrAdd("late published text");
Check(StringRegistry.GetEntriesSince(publicationCursor, emptyPublication) == StringRegistry.EntryCount && emptyPublication.Count == 1 && emptyPublication[0].Id == latePublishedId, "only text registered after the cursor is published");

int snapshotTextId = StringRegistry.GetOrAdd("snapshot text probe");
var publishedTextEntries = new List<(int Id, string Text)>();
StringRegistry.GetEntriesSince(0, publishedTextEntries);

var publishedCatalog = new List<CatalogEntry>();
foreach (var entry in publishedTextEntries)
{
	publishedCatalog.Add(new CatalogEntry
	{
		Channel = "string",
		Key = entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
		Value = entry.Text
	});
}

StringRegistry.Reset();
Check(StringRegistry.Get(snapshotTextId) == string.Empty, "resetting the table drops the text a peer has to receive");

foreach (var entry in publishedCatalog)
{
	if (int.TryParse(entry.Key, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int publishedId))
	{
		StringRegistry.RegisterRemote(publishedId, entry.Value);
	}
}
Check(StringRegistry.Get(snapshotTextId) == "snapshot text probe", "the string catalog channel restores text on a peer");

var binarySnapshotWorld = new EcsWorld();
byte[] binarySnapshot = binarySnapshotWorld.ExportBinarySnapshot();
Check(binarySnapshot.Length == 32 + EcsSharedMemory.TOTAL_SIZE_BYTES, "a binary snapshot carries ecs memory only");

int entriesBeforeImport = StringRegistry.EntryCount;
var binarySnapshotTarget = new EcsWorld();
binarySnapshotTarget.ImportBinarySnapshot(binarySnapshot);
Check(StringRegistry.EntryCount == entriesBeforeImport, "importing a binary snapshot does not carry text");

Check(StringRegistry.TryGetText(snapshotTextId, out string knownText) && knownText == "snapshot text probe", "a known identifier is readable without a host round trip");
Check(!StringRegistry.TryGetText(0, out _), "the reserved zero identifier reports a miss");
Check(!StringRegistry.TryGetText(StringRegistry.ComputeId("text nobody registered"), out _), "an unknown identifier reports a miss instead of empty text");

string tableDump = StringRegistry.DescribeTable();
Check(tableDump.Contains("kind=string text='snapshot text probe'", StringComparison.Ordinal), "the table dump lists identifier, kind and text");
Check(tableDump.Contains($"{StringRegistry.EntryCount} entries", StringComparison.Ordinal), "the table dump reports the entry count");
Check(StringRegistry.CollisionCount == 0, $"built-in strings do not collide ({StringRegistry.CollisionSummary})");
Check(StringRegistry.GetOrAdd(string.Empty) == 0 && StringRegistry.GetOrAdd(null) == 0, "empty text keeps the reserved zero id");

StringRegistry.Reset();
int firstCursorText = StringRegistry.GetOrAdd("cursor probe one");
var cursorFirstBatch = new List<(int Id, string Text)>();
int textCursor = StringRegistry.GetEntriesSince(0, cursorFirstBatch);
Check(cursorFirstBatch.Count == 1 && cursorFirstBatch[0].Id == firstCursorText && textCursor == 1, "an empty text cursor publishes the text table");

ContentIdStore.GetOrAddTextList(new[] { firstCursorText });
int secondCursorText = StringRegistry.GetOrAdd("cursor probe two");
var cursorSecondBatch = new List<(int Id, string Text)>();
int advancedTextCursor = StringRegistry.GetEntriesSince(textCursor, cursorSecondBatch);
Check(cursorSecondBatch.Count == 1 && cursorSecondBatch[0].Id == secondCursorText && advancedTextCursor == 2, "a payload of another kind does not advance the text cursor");

var abilityList = new AbilityState(new[] { "fireball", "heal" });
Check(abilityList.Count == 2 && abilityList.Get(0) == "fireball" && abilityList.Get(1) == "heal", "an ability list keeps its authored order");
int sharedAbilityHandle = abilityList.Handle;
Check(!abilityList.Add("FIREBALL") && abilityList.Handle == sharedAbilityHandle, "adding an ability that already exists keeps the payload");
Check(abilityList.Add("blink") && abilityList.Count == 3 && abilityList.Handle != sharedAbilityHandle, "adding a new ability writes a new payload");
Check(new AbilityState(null).Count == 0 && new AbilityState(Array.Empty<string>()).Handle == 0, "an entity without abilities stores no payload");
Check(abilityList.Contains("BLINK") && !abilityList.Contains("teleport") && abilityList.Get(9) == null, "ability lookups ignore case and report misses");

var firstQueue = new ProductionQueue();
var secondQueue = new ProductionQueue();
firstQueue.Enqueue("worker");
firstQueue.Enqueue("farm");
secondQueue.Enqueue("worker");
secondQueue.Enqueue("farm");
Check(firstQueue.Count == 2 && firstQueue.Get(0) == "worker" && firstQueue.Get(1) == "farm", "a production queue preserves insertion order");
Check(firstQueue.Handle == secondQueue.Handle, "queues with identical content share one content id payload");
Check(ContentIdStore.TryGetKind(firstQueue.Handle, out byte queueKind) && queueKind == ContentIdStore.TextListKind, "a production queue payload carries the text list kind");

int sharedQueueHandle = firstQueue.Handle;
firstQueue.Enqueue("grunt");
Check(firstQueue.Handle != sharedQueueHandle, "enqueueing writes a new payload instead of mutating the shared one");
Check(secondQueue.Handle == sharedQueueHandle && secondQueue.Count == 2, "the queue that shared the payload is untouched by the other queue's append");
Check(firstQueue.Count == 3 && firstQueue.Get(2) == "grunt", "the new payload keeps the appended unit");

Check(secondQueue.RemoveAt(0) == "worker" && secondQueue.Count == 1 && secondQueue.Get(0) == "farm", "removing an item reports it and shifts the rest");
secondQueue.Clear();
Check(secondQueue.Count == 0 && secondQueue.Handle == 0, "clearing a queue drops the payload");
Check(firstQueue.Get(3) == null && firstQueue.RemoveAt(5) == null, "reads and removals outside the queue report nothing");

var seededBuffState = new BuffState(new Dictionary<string, float> { { "stun", 3f } });
Check(seededBuffState.Count == 1 && seededBuffState.TryGetValue("stun", out float seededRemaining) && seededRemaining == 3f, "a seeded buff state keeps the authored duration as remaining time");
Check(seededBuffState.TryGetDuration("stun", out float seededDuration) && seededDuration == 3f, "a seeded buff state keeps the authored duration as its total");

var timedBuffs = new Buffs(null);
timedBuffs.Set("stun", 2f, 2f);
timedBuffs.Set("slow", 5f, 6f);
Check(timedBuffs.Count == 2 && timedBuffs.GetKey(0) == "stun" && timedBuffs.GetKey(1) == "slow", "status slots keep insertion order");
Check(timedBuffs.TryGetDuration("slow", out float slowDuration) && slowDuration == 6f, "a status slot keeps its original duration beside the remaining time");

timedBuffs.Advance(2.5f);
Check(timedBuffs.Count == 1 && !timedBuffs.TryGetValue("stun", out _) && timedBuffs.GetKey(0) == "slow", "advancing a status drops the expired slot and keeps the rest");
Check(timedBuffs.TryGetValue("slow", out float slowRemaining) && Math.Abs(slowRemaining - 2.5f) < 0.0001f, "advancing a status keeps the exact remaining time");

timedBuffs.Set("stun", 1f, 1f);
Check(timedBuffs.Count == 2 && timedBuffs.TryGetValue("stun", out _), "re-applying an expired status takes the freed slot");
timedBuffs.Remove("stun");
Check(timedBuffs.Count == 1 && timedBuffs.GetKey(0) == "slow" && !timedBuffs.TryGetValue("stun", out _), "removing a status compacts the slots");
Check(!timedBuffs.TryGetValue(null, out _) && !timedBuffs.TryGetValue(string.Empty, out _), "a missing status id reports a miss");

var saturatedCooldowns = new Cooldowns(null);
for (int index = 0; index < BuffSlotArray.Capacity; index++)
{
	saturatedCooldowns.Set($"action_{index}", index + 2f, 10f);
}
Check(saturatedCooldowns.Count == BuffSlotArray.Capacity, "a cooldown store fills every slot the component holds");
saturatedCooldowns.Set("action_overflow", 1f, 10f);
Check(saturatedCooldowns.Count == BuffSlotArray.Capacity && saturatedCooldowns.TryGetValue("action_overflow", out _), "a full cooldown store accepts the newest entry");
Check(!saturatedCooldowns.TryGetValue("action_0", out _), "a full cooldown store evicts the entry that was about to expire");
Check(saturatedCooldowns.TryGetValue("action_1", out _) && saturatedCooldowns.TryGetValue($"action_{BuffSlotArray.Capacity - 1}", out _), "eviction keeps every other entry");

var spellTimers = new SpellCooldowns(null);
spellTimers.Set("meteor", 3f, 12f);
spellTimers.Advance(1f);
Check(spellTimers.TryGetValue("meteor", out float meteorRemaining) && Math.Abs(meteorRemaining - 2f) < 0.0001f, "a spell timer advances without losing its identity");
Check(spellTimers.TryGetDuration("meteor", out float meteorDuration) && meteorDuration == 12f, "a spell timer keeps the full cooldown beside the remaining time");
spellTimers.Clear();
Check(spellTimers.Count == 0 && !spellTimers.TryGetValue("meteor", out _), "clearing a spell timer list frees every slot");

var carriedItems = new Inventory(null);
Check(carriedItems.Count == 0 && carriedItems.GetItemCount("sword") == 0 && carriedItems.GetItemId(0) == null, "an empty inventory reports nothing carried");
Check(carriedItems.AddItem("sword") && carriedItems.AddItem("shield", 3), "carrying new items succeeds");
Check(carriedItems.Count == 2 && carriedItems.GetItemId(0) == "sword" && carriedItems.GetCharges(1) == 3, "carried items keep pickup order and charges");
Check(carriedItems.GetItemCount("SWORD") == 1 && carriedItems.HasItem("Shield"), "inventory lookups ignore case");
carriedItems.AddItem("sword", 2);
Check(carriedItems.Count == 2 && carriedItems.GetItemCount("sword") == 3, "adding more of a carried item stacks onto its slot");
Check(carriedItems.RemoveItem("sword", 3) && carriedItems.Count == 1 && carriedItems.GetItemId(0) == "shield", "spending every charge drops the slot and keeps the rest in order");
Check(!carriedItems.RemoveItem("torch"), "spending charges of an item nobody carries reports a miss");

var saturatedInventory = new Inventory(null);
for (int index = 0; index < InventorySlotArray.Capacity; index++)
{
	saturatedInventory.AddItem($"item_{index}");
}
Check(saturatedInventory.Count == InventorySlotArray.Capacity, "an inventory fills every slot it owns");
Check(!saturatedInventory.AddItem("item_overflow") && saturatedInventory.Count == InventorySlotArray.Capacity, "a full inventory refuses another distinct item");
Check(saturatedInventory.AddItem($"item_{InventorySlotArray.Capacity - 1}") && saturatedInventory.GetItemCount($"item_{InventorySlotArray.Capacity - 1}") == 2, "a full inventory still stacks an item it already carries");
saturatedInventory.SetItemCount("item_overflow", 4);
Check(saturatedInventory.Count == InventorySlotArray.Capacity && saturatedInventory.GetItemCount("item_overflow") == 0, "a full inventory refuses a new item written directly");
Check(saturatedInventory.ToList().Count == InventorySlotArray.Capacity, "listing carried items reports every slot");
saturatedInventory.Clear();
Check(saturatedInventory.Count == 0 && !saturatedInventory.HasItem("item_0"), "clearing an inventory frees every slot");

var appliedModifiers = new ModifierState(null);
appliedModifiers.Add(new StatModifier(new StatId("Attack"), ModifierType.Flat, 5f, 2f));
appliedModifiers.Add(new StatModifier(new StatId("Armor"), ModifierType.Flat, 3f));
Check(appliedModifiers.Count == 2 && Math.Abs(appliedModifiers.Get(0).Value - 5f) < 0.0001f, "modifiers keep the order they were applied in");
Check(!appliedModifiers.Advance(1f) && appliedModifiers.Count == 2 && Math.Abs(appliedModifiers.Get(0).Duration - 1f) < 0.0001f, "advancing a timed modifier shortens it without removing it");
Check(appliedModifiers.Advance(1.5f) && appliedModifiers.Count == 1 && Math.Abs(appliedModifiers.Get(0).Value - 3f) < 0.0001f, "an expired modifier is removed and a permanent one survives");
Check(appliedModifiers.Get(0).Duration <= 0f && appliedModifiers.Get(5).Value == 0f, "a permanent modifier keeps no duration and reads outside the list as empty");

appliedModifiers.RemoveAt(0);
Check(appliedModifiers.Count == 0, "removing the last modifier empties the list");
appliedModifiers.Add(new StatModifier(new StatId("Speed"), ModifierType.PercentAdditive, 0.1f));
appliedModifiers.Add(new StatModifier(new StatId("Armor"), ModifierType.Flat, 9f));
appliedModifiers.RemoveAt(0);
Check(appliedModifiers.Count == 1 && Math.Abs(appliedModifiers.Get(0).Value - 9f) < 0.0001f, "removing a modifier shifts the rest instead of swapping order");

var saturatedModifiers = new ModifierState(null);
for (int index = 0; index < ModifierSlotArray.Capacity; index++)
{
	saturatedModifiers.Add(new StatModifier(new StatId("Armor"), ModifierType.Flat, index, index + 1f));
}
Check(saturatedModifiers.Count == ModifierSlotArray.Capacity, "a modifier list fills every slot the component holds");
saturatedModifiers.Add(new StatModifier(new StatId("Armor"), ModifierType.Flat, -1f, 0.5f));
Check(saturatedModifiers.Count == ModifierSlotArray.Capacity && Math.Abs(saturatedModifiers.Get(0).Duration - 0.5f) < 0.0001f, "a full modifier list evicts the modifier closest to expiry");
Check(Math.Abs(saturatedModifiers.Get(1).Duration - 2f) < 0.0001f && Math.Abs(saturatedModifiers.Get(ModifierSlotArray.Capacity - 1).Duration - ModifierSlotArray.Capacity) < 0.0001f, "eviction keeps every other modifier");

var zoneOccupancy = new OccupiedZones();
Check(zoneOccupancy.Add(3) && zoneOccupancy.Contains(3), "occupying a zone records the membership");
Check(!zoneOccupancy.Add(3), "occupying the same zone twice reports no new entry");
Check(zoneOccupancy.Add(OccupiedZoneMask.Capacity - 1) && zoneOccupancy.Contains(OccupiedZoneMask.Capacity - 1), "the highest supported zone is tracked");
Check(!zoneOccupancy.Contains(4) && !zoneOccupancy.Contains(-1) && !zoneOccupancy.Contains(OccupiedZoneMask.Capacity), "unoccupied and out of range zones report no membership");
Check(zoneOccupancy.Remove(3) && !zoneOccupancy.Contains(3) && !zoneOccupancy.Remove(3), "leaving a zone clears its membership once");
zoneOccupancy.Clear();
Check(!zoneOccupancy.Contains(OccupiedZoneMask.Capacity - 1), "clearing the zone mask drops every membership");

var entityStats = new Stats(null);
entityStats.Set(new StatId("Armor"), 4f);
entityStats.Set(new StatId("Attack"), 9f);
Check(entityStats.Count == 2 && Math.Abs(entityStats.Get(new StatId("Armor")) - 4f) < 0.0001f, "stats keep the value written for each stat");
entityStats.Set(new StatId("Armor"), 11f);
Check(entityStats.Count == 2 && Math.Abs(entityStats.Get(new StatId("Armor")) - 11f) < 0.0001f, "writing a stat twice replaces its value instead of adding a slot");
Check(!entityStats.TryGetValue(new StatId("Evasion"), out float missingStatValue) && missingStatValue == 0f, "a stat that was never written reports a miss");

var saturatedStats = new Stats(null);
for (int index = 0; index < StatSlotArray.Capacity; index++)
{
	saturatedStats.Set(new StatId($"stat_{index}"), index);
}
Check(saturatedStats.Count == StatSlotArray.Capacity, "a stats component fills every slot it holds");
saturatedStats.Set(new StatId("stat_overflow"), 1f);
Check(saturatedStats.Count == StatSlotArray.Capacity && !saturatedStats.TryGetValue(new StatId("stat_overflow"), out _), "a full stats component refuses a new stat");
Check(Math.Abs(saturatedStats.Get(new StatId("stat_0")) - 0f) < 0.0001f, "refusing a new stat keeps the existing values");
saturatedStats.Clear();
Check(saturatedStats.Count == 0 && !saturatedStats.TryGetValue(new StatId("stat_0"), out _), "clearing stats frees every slot");

var scriptPlayers = new ScriptPlayersState(null);
Check(ScriptPlayersState.SlotCount == 25 && !scriptPlayers.IsValidSlot(25) && !scriptPlayers.IsValidSlot(-1), "the player table addresses exactly the slot range the map API exposes");
scriptPlayers.SetName(3, "Horaid_Topa");
scriptPlayers.SetActive(3, true);
scriptPlayers.SetComputerControlled(3, false);
scriptPlayers.SetTeam(3, 2, StringRegistry.GetOrAdd("Team 2"));
scriptPlayers.SetGold(3, 120f);
scriptPlayers.SetWood(3, 40f);
scriptPlayers.SetStone(3, 7f);
scriptPlayers.SetKills(3, 5);
var recordedPlayer = scriptPlayers.Get(3);
Check(recordedPlayer.Name == "Horaid_Topa" && recordedPlayer.Active && !recordedPlayer.IsComputer, "player slots keep the name and flags written to them");
Check(recordedPlayer.Team == 2 && recordedPlayer.TeamName == "Team 2", "a player slot keeps its team and team name");
Check(Math.Abs(recordedPlayer.Gold - 120f) < 0.0001f && Math.Abs(recordedPlayer.Wood - 40f) < 0.0001f && Math.Abs(recordedPlayer.Stone - 7f) < 0.0001f && recordedPlayer.KillCount == 5, "a player slot keeps its resources and kills");
scriptPlayers.SetName(31, "nobody");
Check(scriptPlayers.Get(31).Name.Length == 0, "writing outside the player table changes nothing");

var seededPlayers = new ScriptPlayersState(new[] { recordedPlayer });
Check(seededPlayers.Get(0).Name == "Horaid_Topa" && seededPlayers.Get(1).Name.Length == 0, "seeding the player table copies only the slots it is given");

var scriptZones = new ScriptZonesState(null);
Check(scriptZones.Count == 0 && scriptZones.GetZones().Length == 0 && scriptZones.GetZone(0).MaxX == 0f, "a map without zones reports no zones");
scriptZones.Add(new ZoneBounds { MinX = 0f, MinZ = 0f, MaxX = 10f, MaxZ = 10f });
scriptZones.Add(new ZoneBounds { MinX = 20f, MinZ = 20f, MaxX = 30f, MaxZ = 30f });
Check(scriptZones.Count == 2 && scriptZones.GetZones().Length == 2, "adding a zone grows the content id zone list");
Check(Math.Abs(scriptZones.GetZone(0).MaxX - 10f) < 0.0001f && Math.Abs(scriptZones.GetZone(1).MinX - 20f) < 0.0001f, "zones keep the order they were defined in");
Check(scriptZones.GetZone(-1).MaxX == 0f && scriptZones.GetZone(7).MaxX == 0f, "a zone index outside the list reads as an empty zone");
Check(ContentIdStore.TryGetKind(scriptZones.Handle, out byte zoneKind) && zoneKind == ContentIdStore.ZoneListKind, "the zone list payload carries the zone list kind");
var identicalZones = new ScriptZonesState(null);
identicalZones.Add(new ZoneBounds { MinX = 0f, MinZ = 0f, MaxX = 10f, MaxZ = 10f });
identicalZones.Add(new ZoneBounds { MinX = 20f, MinZ = 20f, MaxX = 30f, MaxZ = 30f });
Check(identicalZones.Handle == scriptZones.Handle, "maps that define the same zones share one content id payload");
scriptZones.Clear();
Check(scriptZones.Count == 0 && scriptZones.Handle == 0 && scriptZones.GetZones().Length == 0, "clearing the zone list drops the payload");

var effectQueue = new VFXQueue(null);
Check(effectQueue.Count == 0 && effectQueue.Get(0).Scale == 0f, "an empty effect queue hands out nothing");
effectQueue.Enqueue(new VFXRequest("heal", default, default, 1f, 0f));
effectQueue.Enqueue(new VFXRequest("arrow", default, default, 2f, 0f));
Check(effectQueue.Count == 2 && effectQueue.Get(0).EffectTypeId == "heal" && effectQueue.Get(1).Scale == 2f, "queued effects keep the order they were raised in");

var saturatedEffects = new VFXQueue(null);
for (int index = 0; index < VFXSlotArray.Capacity; index++)
{
	saturatedEffects.Enqueue(new VFXRequest($"effect_{index}", default, default, index, 0f));
}
Check(saturatedEffects.Count == VFXSlotArray.Capacity, "an effect queue fills every slot it holds");
saturatedEffects.Enqueue(new VFXRequest("effect_overflow", default, default, 1f, 0f));
Check(saturatedEffects.Count == VFXSlotArray.Capacity && saturatedEffects.Get(VFXSlotArray.Capacity - 1).EffectTypeId == "effect_overflow", "a full effect queue keeps the newest request");
Check(saturatedEffects.Get(0).EffectTypeId == "effect_1", "a full effect queue drops the oldest request");
saturatedEffects.Clear();
Check(saturatedEffects.Count == 0, "clearing the effect queue frees every slot");

var handleWorld = new EcsWorld();
handleWorld.PayloadStores.GetOrCreateColdPayloadStore<List<string>>().Reset();
const string sharedContentKey = "production_queue|worker,farm";
int sharedHandle = ColdPayloadRegistry<List<string>>.Share(new List<string> { "worker", "farm" }, sharedContentKey);
int secondSharedHandle = ColdPayloadRegistry<List<string>>.Share(new List<string> { "worker", "farm" }, sharedContentKey);
Check(sharedHandle != 0 && sharedHandle == secondSharedHandle, "identical content keys receive one handle");
Check(ColdPayloadRegistry<List<string>>.GetReferenceCount(sharedHandle) == 2, "shared handles track their references");

ColdPayloadRegistry<List<string>>.Free(sharedHandle);
Check(ColdPayloadRegistry<List<string>>.Get(secondSharedHandle) != null, "releasing one reference keeps a shared payload alive");
ColdPayloadRegistry<List<string>>.Free(secondSharedHandle);
Check(ColdPayloadRegistry<List<string>>.Get(sharedHandle) == null, "releasing the last reference frees a shared payload");

int unsharedFirst = ColdPayloadRegistry<List<string>>.Register(new List<string> { "same" });
int unsharedSecond = ColdPayloadRegistry<List<string>>.Register(new List<string> { "same" });
Check(unsharedFirst != unsharedSecond, "unshared registrations stay independent for mutable payloads");

List<string>? firstWorldPayload = ColdPayloadRegistry<List<string>>.Get(unsharedFirst);
var otherWorld = new EcsWorld();
Check(ColdPayloadRegistry<List<string>>.Get(unsharedFirst) == null, "a second world never resolves the first world's cold payloads");
var otherWorldPayload = new List<string> { "same" };
int otherWorldHandle = ColdPayloadRegistry<List<string>>.Register(otherWorldPayload);
Check(ReferenceEquals(otherWorld.PayloadStores.GetOrCreateColdPayloadStore<List<string>>().Get(otherWorldHandle), otherWorldPayload), "a world's store holds only the payloads registered in it");
Check(!ReferenceEquals(handleWorld.PayloadStores.GetOrCreateColdPayloadStore<List<string>>().Get(unsharedFirst), otherWorldPayload), "the two worlds never share a payload slot");
EcsWorld.SetAmbient(handleWorld);
Check(ReferenceEquals(ColdPayloadRegistry<List<string>>.Get(unsharedFirst), firstWorldPayload), "switching the ambient world resolves the payload the handle was registered in");

Console.WriteLine($"replication verifier: {checks - failures}/{checks} checks passed");
if (failures > 0)
{
	Console.WriteLine($"FAILED: {failures}");
	return 1;
}

Console.WriteLine("PASS");
return 0;
