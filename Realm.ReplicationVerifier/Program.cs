using System.Reflection;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Replication;
using Realm.MapAPI;

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

mirrorA.RecordAbility(new AbilityStateDelta { ServerEntityId = 3, AbilityId = "build", Flags = 1 });
mirrorA.RecordText(new StaticTextDelta { Handle = 1, Text = "wave", PositionX = 1f, PositionY = 2f, PositionZ = 3f, Visible = 1 });
mirrorB.RecordAbility(new AbilityStateDelta { ServerEntityId = 3, AbilityId = "build", Flags = 1 });
mirrorB.RecordText(new StaticTextDelta { Handle = 1, Text = "wave", PositionX = 1f, PositionY = 2f, PositionZ = 3f, Visible = 1 });
Check(mirrorA.ComputeFingerprint() == mirrorB.ComputeFingerprint(), "fingerprint covers ability state and static text symmetrically");

uint baselineFingerprint = mirrorA.ComputeFingerprint();
mirrorA.RecordAbility(new AbilityStateDelta { ServerEntityId = 3, AbilityId = "build", Flags = 2 });
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

var abilities = new List<AbilityStateDelta>
{
	new() { ServerEntityId = 7, AbilityId = "build_draft", Flags = 2 },
	new() { ServerEntityId = 8, AbilityId = "build_draft", Flags = 0 }
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

var emptyWorldEntity = world.Create();
Check(!world.Has<Realm.Ecs.Components.Core.PlayerPopulation>(emptyWorldEntity), "bare entity has no population component");

StringRegistry.Reset();
int goldId = StringRegistry.GetOrAdd("Gold");
Check(goldId != 0 && goldId == StringRegistry.ComputeId("Gold"), "string ids are content hashes");
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

var snapshotSourceWorld = new EcsWorld();
int snapshotTextId = StringRegistry.GetOrAdd("snapshot text probe");
byte[] binarySnapshot = snapshotSourceWorld.ExportBinarySnapshot();
Check(binarySnapshot.Length > 32 + EcsSharedMemory.TOTAL_SIZE_BYTES, "a binary snapshot carries a text section after shared memory");

StringRegistry.Reset();
Check(StringRegistry.Get(snapshotTextId) == string.Empty, "resetting the table drops the text a snapshot will restore");

var snapshotTargetWorld = new EcsWorld();
snapshotTargetWorld.ImportBinarySnapshot(binarySnapshot);
Check(StringRegistry.Get(snapshotTextId) == "snapshot text probe", "importing a binary snapshot restores its text table");
Check(StringRegistry.CollisionCount == 0, $"built-in strings do not collide ({StringRegistry.CollisionSummary})");
Check(StringRegistry.GetOrAdd(string.Empty) == 0 && StringRegistry.GetOrAdd(null) == 0, "empty text keeps the reserved zero id");

ColdPayloadRegistry<List<string>>.Reset();
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

Console.WriteLine($"replication verifier: {checks - failures}/{checks} checks passed");
if (failures > 0)
{
	Console.WriteLine($"FAILED: {failures}");
	return 1;
}

Console.WriteLine("PASS");
return 0;
