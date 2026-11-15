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

public partial class CommandAuthorityService
	{
	private readonly SimulationContext _context;
	private readonly CommandTargetResolver _targets;

	public Func<string, AbilityRules?>? AbilityRulesProvider { get; set; }

	/// <summary>
	///     The abilities a map registers while it runs, which no template declares. A card the script created is a
	///     real command the player can press, so the authority has to be able to price and range-check it too.
	/// </summary>
	public Func<string, AbilityRules?>? ScriptAbilityRulesProvider { get; set; }

	public Func<string, ProductionCosts?>? ProductionCostsProvider { get; set; }

	public Func<string, UpgradeRules?>? UpgradeRulesProvider { get; set; }

	public Func<int, string, int>? TechLevelReader { get; set; }

	public Action<int, string, int>? TechLevelWriter { get; set; }

	public Func<string, ItemMetadata?>? ConsumableProvider { get; set; }

	public Action<string>? DroppedOrderSink { get; set; }

	public Action<int, float>? CostsPaidSink { get; set; }

	/// <summary>
	///     Raised with the unit whose consumable just changed and the consumable it now carries, or an empty id when it
	///     lost one. The attachment lives in the world, so a map script watching its own consumables would otherwise never
	///     hear that a purchase landed.
	/// </summary>
	public Action<int, string>? ConsumableChangedSink { get; set; }

	public CommandTargetResolver Targets => _targets;

	public CommandAuthorityService(SimulationContext context)
	{
		_context = context;
		_targets = new CommandTargetResolver(context);
	}

	private bool TryGetAbilityRules(string? spellId, out AbilityRules rules)
	{
		rules = default;
		if (string.IsNullOrEmpty(spellId)) return false;

		AbilityRules? resolved = AbilityRulesProvider?.Invoke(spellId) ?? ScriptAbilityRulesProvider?.Invoke(spellId);
		if (resolved == null) return false;

		rules = resolved.Value;
		return true;
	}

public ServerCommandResult ExecuteServerCommand(int peerId, NetworkCommand cmd)
	{
		var stopVelocityEntityIds = new List<int>();
		var holdVelocityEntityIds = new List<int>();
		var rejectedServerEntityIds = new List<int>();
		string rejectionReason = string.Empty;

		DispatchServerCommand(peerId, cmd, stopVelocityEntityIds, holdVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason);

		bool needsBuildUnit = cmd.CommandType == "build";
		string? buildUnitType = needsBuildUnit ? cmd.ArgString : null;
		Vector3 buildPosition = needsBuildUnit ? new Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z) : Vector3.Zero;
		int buildPeerOwner = needsBuildUnit ? peerId : -1;
		int buildWorkerServerEntityId = needsBuildUnit && cmd.UnitEntityIds.Count > 0 ? cmd.UnitEntityIds[0] : -1;

		bool needsSpellEffect = cmd.CommandType == "spell";
		if (needsSpellEffect && !TryAuthorizeSpellCommand(peerId, cmd, out string spellRejection))
		{
			needsSpellEffect = false;
			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = spellRejection;
			if (cmd.UnitEntityIds.Count > 0) RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[0], spellRejection);
		}

		string? spellId = needsSpellEffect ? cmd.ArgString : null;
		Vector3 spellPosition = needsSpellEffect ? new Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z) : Vector3.Zero;

		return new ServerCommandResult(
			needsBuildUnit, buildUnitType, buildPosition, buildPeerOwner, buildWorkerServerEntityId,
			needsSpellEffect, spellId, spellPosition,
			stopVelocityEntityIds, holdVelocityEntityIds,
			rejectedServerEntityIds, rejectionReason);
	}

}
