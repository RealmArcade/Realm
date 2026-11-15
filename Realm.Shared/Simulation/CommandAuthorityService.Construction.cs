using System;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Shared.Network;

namespace Realm.Shared.Simulation;

/// <summary>cancelling a site that is being raised and paying its owner back what it cost.</summary>
public partial class CommandAuthorityService
{
	/// <summary>
	///     Raised with a site this order destroyed, so the process that owns the map script can forget what the script
	///     declared about it. A site is not killed and no death is announced: it was taken down by its owner before it
	///     ever became a building, so nothing pays a bounty or hears that a unit died.
	/// </summary>
	public Action<Entity>? ConstructionCancelledSink { get; set; }

	private bool DispatchConstructionCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		if (cmd.CommandType != "cancel_construction") return false;

		HandleCancelConstructionCommand(peerId, cmd, ref rejectionReason);
		return true;
	}

	/// <summary>
	///     Takes down one of the player's own unfinished sites. The order is answered for a site whose owner asked, which
	///     is the only entity that may be taken down this way: a whole building is not a site, and taking one down on
	///     demand would be a demolish order this rule does not offer. The full cost the placement charged is paid back, so
	///     cancelling a site never costs the player anything they had not already spent on work they keep.
	/// </summary>
	private void HandleCancelConstructionCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			Entity site = _targets.Resolve(serverId);
			if (site == Entity.Null || !IsClientAuthorized(peerId, site))
			{
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			if (!_context.World.Has<UnderConstruction>(site) || !_context.World.Has<ConstructionState>(site))
			{
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "the unit is not a site that is being raised";
				continue;
			}

			string? templateId = _context.World.TryGet<DefinitionId>(site, out DefinitionId definition) ? definition.Value : null;
			if (!_context.World.TryGet<Owner>(site, out Owner owner) || owner.PlayerEntity.Value == Entity.Null)
			{
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "the site states no owner to be paid back";
				continue;
			}

			Entity ownerEntity = owner.PlayerEntity.Value;
			RefundSiteCost(ownerEntity, templateId);

			// The workers raising it are let go by the construction tick, which drops the task of every builder whose site
			// has left the world.
			_context.World.Destroy(site);

			ConstructionCancelledSink?.Invoke(site);
		}
	}

	/// <summary>
	///     Pays back the whole cost the placement charged for a site, which is the template's own price: a placement pays
	///     that price before the site exists, so cancelling before the work is done returns exactly what was taken.
	/// </summary>
	/// <param name="ownerEntity">The player entity to pay.</param>
	/// <param name="templateId">The site's template, which states the price.</param>
	private void RefundSiteCost(Entity ownerEntity, string? templateId)
	{
		if (!TryGetProductionCosts(templateId, out ProductionCosts costs)) return;
		if (!_context.World.TryGet<PlayerResources>(ownerEntity, out PlayerResources resources)) return;

		resources.Gold += (int)costs.Gold;
		resources.Wood += (int)costs.Wood;
		resources.Stone += (int)costs.Stone;
		_context.World.Set(ownerEntity, resources);
	}
}
