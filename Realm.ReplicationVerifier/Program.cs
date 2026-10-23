using System.Reflection;
using MemoryPack;
using Realm.Ecs;
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

Console.WriteLine($"replication verifier: {checks - failures}/{checks} checks passed");
if (failures > 0)
{
	Console.WriteLine($"FAILED: {failures}");
	return 1;
}

Console.WriteLine("PASS");
return 0;
