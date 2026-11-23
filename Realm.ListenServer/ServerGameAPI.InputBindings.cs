using System.Numerics;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	public void BindPlayerKeyToAbility(int playerIndex, UnitId unit, string key, string pressAbilityId, string releaseAbilityId)
	{
		if (string.IsNullOrWhiteSpace(key)) return;

		int unitHandle = unit.IsValid && ResolveUnit(unit) != Entity.Null ? unit.Value : 0;
		_presentation.Write(
			PresentationKind.PlayerKeyBindingSet,
			playerIndex,
			unitHandle,
			key.Trim(),
			pressAbilityId ?? string.Empty,
			releaseAbilityId ?? string.Empty,
			string.Empty,
			null,
			Vector3.Zero,
			Vector3.Zero,
			Vector3.Zero,
			0,
			0,
			0f,
			0f,
			0f,
			false);
	}

	public void ClearPlayerKeyBindings(int playerIndex)
	{
		_presentation.Write(PresentationKind.PlayerKeyBindingsCleared, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}
}
