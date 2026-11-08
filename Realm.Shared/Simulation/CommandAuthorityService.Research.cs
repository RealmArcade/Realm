using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Ecs.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.Shared.Simulation;

/// <summary>research and paying the costs it charges.</summary>
public partial class CommandAuthorityService
	{
	private void HandleResearchCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		string requestedId = cmd.ArgString ?? string.Empty;
		if (string.IsNullOrEmpty(requestedId))
		{
			rejectionReason = "a research order names no technology";
			return;
		}

		if (UpgradeRulesProvider == null || TechLevelReader == null || TechLevelWriter == null)
		{
			rejectionReason = $"'{requestedId}' cannot be researched in this match";
			return;
		}

		UpgradeRules? resolved = UpgradeRulesProvider(requestedId);
		if (resolved == null)
		{
			rejectionReason = $"the map declares no technology '{requestedId}'";
			return;
		}

		UpgradeRules rules = resolved.Value;
		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity))
		{
			rejectionReason = $"peer {peerId} has no player entity";
			return;
		}

		int playerIndex = _context.PlayerSlotForEntity(playerEntity);
		int currentLevel = TechLevelReader(playerIndex, rules.CanonicalId);
		if (currentLevel >= rules.MaxLevel)
		{
			rejectionReason = $"technology '{rules.CanonicalId}' is already at its last level ({currentLevel})";
			return;
		}

		if (!TryPayCosts(playerEntity, rules.Gold, rules.Wood, rules.Stone))
		{
			rejectionReason = $"technology '{rules.CanonicalId}' costs more than the player has";
			return;
		}

		TechLevelWriter(playerIndex, rules.CanonicalId, currentLevel + 1);
	}

	private bool TryPayCosts(Entity playerEntity, float gold, float wood, float stone)
	{
		if (!_context.World.TryGet<PlayerResources>(playerEntity, out PlayerResources resources)) return false;

		var goldResourceId = new ResourceId("Gold");
		var woodResourceId = new ResourceId("Wood");
		var stoneResourceId = new ResourceId("Stone");

		int costGold = (int)gold;
		int costWood = (int)wood;
		int costStone = (int)stone;

		if (resources[goldResourceId] < costGold || resources[woodResourceId] < costWood || resources[stoneResourceId] < costStone) return false;

		resources[goldResourceId] -= costGold;
		resources[woodResourceId] -= costWood;
		resources[stoneResourceId] -= costStone;
		_context.World.Set(playerEntity, resources);
		CostsPaidSink?.Invoke(_context.PlayerSlotForEntity(playerEntity), costGold);
		return true;
	}
}
