using System.Collections.Generic;
using Godot;

namespace Realm.Client.Services;

public readonly struct ServerCommandResult
{
	public readonly bool NeedsBuildUnit;
	public readonly string BuildUnitType;
	public readonly Vector3 BuildPosition;
	public readonly int BuildPeerOwner;
	public readonly int BuildWorkerServerEntityId;

	public readonly bool NeedsSpellEffect;
	public readonly string SpellId;
	public readonly Vector3 SpellPosition;

	public readonly List<int> StopVelocityEntityIds;
	public readonly List<int> HoldVelocityEntityIds;

	public ServerCommandResult(
		bool needsBuildUnit, string buildUnitType, Vector3 buildPosition, int buildPeerOwner, int buildWorkerServerEntityId,
		bool needsSpellEffect, string spellId, Vector3 spellPosition,
		List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds)
	{
		NeedsBuildUnit = needsBuildUnit;
		BuildUnitType = buildUnitType;
		BuildPosition = buildPosition;
		BuildPeerOwner = buildPeerOwner;
		BuildWorkerServerEntityId = buildWorkerServerEntityId;
		NeedsSpellEffect = needsSpellEffect;
		SpellId = spellId;
		SpellPosition = spellPosition;
		StopVelocityEntityIds = stopVelocityEntityIds;
		HoldVelocityEntityIds = holdVelocityEntityIds;
	}
}