using System.Collections.Generic;

namespace Realm.Ecs.Replication;

public static class ReplicatedPayloadReplay
{
	public static void Replay(
		ReplicatedStateMirror mirror,
		List<ComponentDelta>? componentDeltas,
		List<AbilityStateDelta>? abilityStates,
		List<StaticTextDelta>? staticTexts,
		List<string>? errors,
		List<ColdPayloadDelta>? coldPayloads = null)
	{
		if (coldPayloads != null)
		{
			for (int i = 0; i < coldPayloads.Count; i++)
			{
				mirror.RecordColdPayload(coldPayloads[i]);
			}
		}

		if (componentDeltas != null)
		{
			for (int i = 0; i < componentDeltas.Count; i++)
			{
				var delta = componentDeltas[i];
				var codec = ReplicatedComponents.Find(delta.ComponentTypeId);
				if (codec == null)
				{
					errors?.Add($"unregistered component type {delta.ComponentTypeId}");
					continue;
				}

				if (delta.Bytes == null || (delta.Bytes.Length != 0 && delta.Bytes.Length != codec.SizeInBytes))
				{
					errors?.Add($"{codec.ComponentName} carries {delta.Bytes?.Length ?? 0} bytes, expected {codec.SizeInBytes} or a removal tombstone");
					continue;
				}

				mirror.RecordComponent(delta);
			}
		}

		if (abilityStates != null)
		{
			for (int i = 0; i < abilityStates.Count; i++)
			{
				mirror.RecordAbility(abilityStates[i]);
			}
		}

		if (staticTexts != null)
		{
			for (int i = 0; i < staticTexts.Count; i++)
			{
				var text = staticTexts[i];
				if (text.Destroyed != 0)
				{
					mirror.Texts.Remove(text.Handle);
					continue;
				}
				mirror.RecordText(text);
			}
		}
	}
}
