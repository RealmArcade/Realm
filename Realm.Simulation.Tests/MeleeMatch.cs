using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.ListenServer;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the authored melee map on a headless authority: the real script, the real snapshot-free simulation and the
///     real command routing, with one seated peer per player slot so a test acts as the screen those players hold.
/// </summary>
internal sealed class MeleeMatch : IDisposable
{
	public const string NexusTemplateId = "building/nexus";
	public const string BeginConstructionAbilityId = "ability/begin_construction";
	public const string AbandonConstructionAbilityId = "ability/abandon_construction";
	public const string RequisitionAbilityId = "ability/requisition";
	public const string EmergencyLevyAbilityId = "ability/emergency_levy";
	public const float BuildingWoodCost = 100f;
	public const float LongestAuthoredConstructionSeconds = 40f;
	public const float LongestAuthoredProductionSeconds = 40f;

	private const int PeerIdBase = 7000;

	private readonly ServerLog _log;
	private readonly string _workDirectory;
	private readonly List<ServerPresentationEvent> _events = new();
	private int _nextCommandId = 1;

	private MeleeMatch(string[] factions, int seed, string mapFolder, string scriptPath, MatchOptionSet? matchOptions = null, int[]? teams = null, bool cheatsEnabled = false)
	{
		Factions = factions;
		_workDirectory = Path.Combine(Path.GetTempPath(), "realm-melee-simulation", $"{Environment.ProcessId}-{seed}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_workDirectory);

		var roster = new MatchRosterSeat[factions.Length];
		for (int index = 0; index < factions.Length; index++)
		{
			int slot = index + 1;
			int team = teams != null && index < teams.Length ? teams[index] : slot;
			roster[index] = new MatchRosterSeat(slot, 0, factions[index], team, $"Team {team}", IsComputer: false, Faction: factions[index]);
		}

		LoadedMap map = MapWorldLoader.Load(mapFolder, roster);
		_log = new ServerLog(Path.Combine(_workDirectory, "authority.log"));
		var paths = ServerMatchPaths.ForMap(map, mapFolder, Path.Combine(_workDirectory, "data"), _workDirectory);

		Simulation = new ServerSimulation(
			map,
			paths,
			_log,
			seed,
			cheatsEnabled: cheatsEnabled,
			wasmDebug: false,
			mapWorkspacePath: null,
			roster: roster,
			hostPlayerSlot: 1,
			preparedScriptHost: null,
			scriptPathOverride: scriptPath,
			matchOptions: matchOptions);

		for (int slot = 1; slot <= factions.Length; slot++)
		{
			if (!Simulation.SeatPeerAtSlot(PeerFor(slot), slot)) throw new InvalidOperationException($"The match refused to seat peer {PeerFor(slot)} at slot {slot}.");
		}

		DrainPresentation();
	}

	public ServerSimulation Simulation { get; }

	public ServerGameAPI Api => Simulation.ScriptApi;

	public EcsWorld World => Simulation.Map.World;

	public string[] Factions { get; }

	public int PlayerCount => Factions.Length;

	public static MeleeMatch Start(string[] factions, int seed = 1, MatchOptionSet? matchOptions = null, int[]? teams = null, bool cheatsEnabled = false)
	{
		return new MeleeMatch(factions, seed, MeleeTestMaps.BoardFolder, MeleeTestMaps.ScriptPath, matchOptions, teams, cheatsEnabled);
	}

	/// <summary>
	///     Starts a match whose seats share teams, which is how an alliance declared in a lobby reaches the match.
	/// </summary>
	/// <param name="factions">The factions the seats play.</param>
	/// <param name="teams">The team each seat belongs to, in seat order.</param>
	/// <param name="seed">The seed the match draws from.</param>
	/// <returns>The match, seated and ready to step.</returns>
	public static MeleeMatch StartWithTeams(string[] factions, int[] teams, int seed = 1)
	{
		return new MeleeMatch(factions, seed, MeleeTestMaps.BoardFolder, MeleeTestMaps.ScriptPath, matchOptions: null, teams: teams);
	}

	public static MeleeMatch StartDuel(string firstFaction = "Vale", string secondFaction = "Sable", int seed = 1, MatchOptionSet? matchOptions = null, bool cheatsEnabled = false)
	{
		return Start(new[] { firstFaction, secondFaction }, seed, matchOptions, cheatsEnabled: cheatsEnabled);
	}

	/// <summary>
	///     Plays a map folder other than the authored ruleset, which is how a composed package is seated on the authority
	///     exactly as a downloaded map would be.
	/// </summary>
	public static MeleeMatch StartOnMap(string mapFolder, string[] factions, string scriptPath, int seed = 1, int[]? teams = null)
	{
		return new MeleeMatch(factions, seed, mapFolder, scriptPath, matchOptions: null, teams: teams);
	}

	public static int PeerFor(int slot) => PeerIdBase + slot;

	public void Dispose()
	{
		Simulation.Dispose();
		_log.Dispose();

		try
		{
			Directory.Delete(_workDirectory, recursive: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}

	public void Step(float seconds)
	{
		int ticks = Math.Max(1, (int)MathF.Round(seconds / (float)MatchTiming.FixedTickSeconds));
		for (int tick = 0; tick < ticks; tick++)
		{
			Simulation.Step();
			DrainPresentation();
		}
	}

	public void StepTicks(int ticks)
	{
		Step(ticks * (float)MatchTiming.FixedTickSeconds);
	}

	private void DrainPresentation()
	{
		ReadOnlySpan<ServerPresentationEvent> drained = Simulation.CollectPresentation();
		if (drained.Length == 0) return;

		for (int index = 0; index < drained.Length; index++)
		{
			_events.Add(drained[index]);
		}
	}

	public IReadOnlyList<ServerPresentationEvent> EventsFor(int slot, PresentationKind kind)
	{
		var matches = new List<ServerPresentationEvent>();
		for (int index = 0; index < _events.Count; index++)
		{
			ServerPresentationEvent candidate = _events[index];
			if (candidate.Kind != kind) continue;
			if (candidate.PlayerIndex != slot && candidate.PlayerIndex != ServerPresentationEvent.Everyone) continue;

			matches.Add(candidate);
		}

		return matches;
	}

	public int CountEventsFor(int slot, PresentationKind kind) => EventsFor(slot, kind).Count;

	public IReadOnlyList<UnitId> Owned(int slot)
	{
		return Api.GetUnitsOwnedByPlayer(slot).ToArray();
	}

	public Entity PlayerEntity(int slot) => Simulation.Map.GetPlayerEntity(slot).Value;

	public bool IsGameOver => Simulation.IsGameOver;

	public bool FastBuildEnabled => Simulation.Map.Simulation.FastBuildEnabled;

	public bool UnlimitedPowerEnabled => Simulation.Map.Simulation.UnlimitedPowerEnabled;

	public bool ShroudDisabled => Simulation.Map.Simulation.ShroudDisabled;

	public float SpeedOf(UnitId unit) => TryGet(unit, out MovementStats movement) ? movement.Speed : 0f;

	public float StatOf(UnitId unit, string statName)
	{
		return TryGet(unit, out Stats stats) ? stats.Get(new Realm.Ecs.Common.StatId(statName)) : 0f;
	}

	public float ArmorOf(UnitId unit) => StatOf(unit, "Armor");

	public float TenacityOf(UnitId unit) => StatOf(unit, "Tenacity");

	public float AttackDamageOf(UnitId unit) => TryGet(unit, out Attack attack) ? attack.Damage : 0f;

	public UnitId Hero(int slot)
	{
		foreach (UnitId unit in Owned(slot))
		{
			if (Has<Hero>(unit)) return unit;
		}

		return UnitId.Invalid;
	}

	public UnitId Nexus(int slot)
	{
		foreach (UnitId unit in Owned(slot))
		{
			if (TemplateOf(unit) == NexusTemplateId) return unit;
		}

		return UnitId.Invalid;
	}

	public IReadOnlyList<UnitId> Buildings(int slot)
	{
		var buildings = new List<UnitId>();
		foreach (UnitId unit in Owned(slot))
		{
			if (Has<Building>(unit)) buildings.Add(unit);
		}

		return buildings;
	}

	public IReadOnlyList<UnitId> Soldiers(int slot)
	{
		var soldiers = new List<UnitId>();
		foreach (UnitId unit in Owned(slot))
		{
			if (Has<Building>(unit) || Has<Hero>(unit)) continue;
			if (Array.IndexOf(MeleeContent.SoldierTemplateIds, TemplateOf(unit)) < 0) continue;
			soldiers.Add(unit);
		}

		return soldiers;
	}

	public UnitId SiteOf(int slot, string templateId)
	{
		foreach (UnitId unit in Owned(slot))
		{
			if (!Has<Building>(unit)) continue;
			if (TemplateOf(unit) != templateId) continue;
			if (Has<UnderConstruction>(unit)) return unit;
		}

		return UnitId.Invalid;
	}

	public string TemplateOf(UnitId unit)
	{
		Entity entity = new(unit.Value);
		return World.TryGet(entity, out DefinitionId definitionId) ? definitionId.Value : string.Empty;
	}

	public bool IsAlive(UnitId unit)
	{
		return unit.IsValid && World.IsAlive(new Entity(unit.Value)) && !World.Has<Dead>(new Entity(unit.Value));
	}

	public bool Has<T>(UnitId unit) where T : unmanaged
	{
		return unit.IsValid && World.IsAlive(new Entity(unit.Value)) && World.Has<T>(new Entity(unit.Value));
	}

	public bool TryGet<T>(UnitId unit, out T component) where T : unmanaged
	{
		component = default;
		if (!unit.IsValid) return false;
		return World.IsAlive(new Entity(unit.Value)) && World.TryGet(new Entity(unit.Value), out component);
	}

	/// <summary>
	///     Reads the multiplier a player's damage carries against another player, which is the table the anti-kingmaker rule
	///     writes and the engine applies to every blow the player lands.
	/// </summary>
	/// <param name="attackingSlot">The slot whose damage is scaled.</param>
	/// <param name="defendingSlot">The slot the scale applies against.</param>
	/// <returns>The multiplier the match currently plays with.</returns>
	public float Effectiveness(int attackingSlot, int defendingSlot)
	{
		Entity playerEntity = Simulation.Map.GetPlayerEntity(attackingSlot).Value;
		if (!World.TryGet(playerEntity, out PlayerDamageEffectiveness table)) return PlayerDamageEffectiveness.FullEffectiveness;

		return table.GetAgainst(defendingSlot);
	}

	public float Gold(int slot) => Api.GetPlayerGold(slot);

	public float Wood(int slot) => Api.GetPlayerWood(slot);

	public void SetWood(int slot, float amount) => Api.SetPlayerWood(slot, amount);

	public void SetGold(int slot, float amount) => Api.SetPlayerGold(slot, amount);

	public ScriptPlayer ReadPlayer(int slot)
	{
		Entity worldEntity = Simulation.Map.WorldEntity;
		ScriptPlayersState players = World.Get<ScriptPlayersState>(worldEntity);
		return players.Get(slot);
	}

	public bool IsEliminated(int slot) => !ReadPlayer(slot).Active;

	public void Cast(int slot, string abilityId, Vector3 position)
	{
		Api.CastAbility(Hero(slot), abilityId, position);
		DrainPresentation();
	}

	public void CastRequisition(int slot)
	{
		Cast(slot, RequisitionAbilityId, Vector3.Zero);
	}

	public void CastEmergencyLevy(int slot)
	{
		Cast(slot, EmergencyLevyAbilityId, Vector3.Zero);
	}

	public Vector3 PositionOf(UnitId unit)
	{
		Entity entity = new(unit.Value);
		return World.TryGet(entity, out Position position) ? position.Value : Vector3.Zero;
	}

	/// <summary>
	///     Finds a resource node of a template, choosing the one nearest a position when several are placed. A board decides
	///     where its nodes stand, so a test reads the placed node rather than a position chosen for it.
	/// </summary>
	/// <param name="templateId">The node template to look for.</param>
	/// <param name="near">The position to choose the closest node to, or null to take the first one placed.</param>
	/// <returns>The node's entity, or an invalid id when the board places none of that template.</returns>
	public UnitId ResourceNode(string templateId, Vector3? near = null)
	{
		UnitId found = UnitId.Invalid;
		float closestDistance = float.MaxValue;
		var query = QueryCache.AllPropIdentityAndPositionQuery;
		World.Query(in query, (Entity entity, ref Position position, ref PropIdentity identity) =>
		{
			if (!string.Equals(identity.PropId, templateId, StringComparison.OrdinalIgnoreCase)) return;

			if (near.HasValue)
			{
				float distance = Vector3.Distance(position.Value, near.Value);
				if (distance >= closestDistance) return;
				closestDistance = distance;
			}
			else if (found.IsValid)
			{
				return;
			}

			found = new UnitId(entity.Id);
		});

		return found;
	}

	public float ResourceAmount(UnitId node) => TryGet(node, out ResourceNode resourceNode) ? resourceNode.Amount : 0f;

	/// <summary>
	///     Empties one placed node the way a worker would over time, so a test can watch what the worker does when its
	///     node runs dry without spending the minutes a full swing count would take.
	/// </summary>
	/// <param name="node">The placed node whose capacity is emptied.</param>
	public void EmptyResourceNode(UnitId node)
	{
		if (!TryGet(node, out Position position)) return;

		foreach (ResourceNodeId candidate in Api.GetResourceNodes())
		{
			if (Vector3.Distance(Api.GetResourceNodePosition(candidate), position.Value) > 0.01f) continue;

			Api.SetResourceNodeAmount(candidate, 0f);
			return;
		}
	}

	/// <summary>
	///     Adds up what every node on the board still holds, which is how a test tells what the workers took out of them.
	/// </summary>
	/// <returns>The remaining capacity of every resource node.</returns>
	public float TotalResourceAmount()
	{
		float total = 0f;
		foreach (ResourceNodeId node in Api.GetResourceNodes()) total += Api.GetResourceNodeAmount(node);
		return total;
	}

	/// <summary>
	///     Adds up what every worker of a seat is holding, which is what a node has given up but the player has not been
	///     paid yet.
	/// </summary>
	/// <param name="slot">The seat whose workers are counted.</param>
	/// <returns>The total a seat's gatherers carry in their hands.</returns>
	/// <summary>
	///     Reads how much food a seat's living units have consumed, which is what the lobby row shows as population.
	/// </summary>
	/// <param name="slot">The seat whose food is read.</param>
	/// <returns>The food the seat's units consume.</returns>
	public int PopulationOf(int slot) => ReadPopulation(slot).Current;

	/// <summary>
	///     Reads the food cap a seat was given.
	/// </summary>
	/// <param name="slot">The seat whose food cap is read.</param>
	/// <returns>The greatest food the seat may consume.</returns>
	public int MaxPopulationOf(int slot) => ReadPopulation(slot).Max;

	private PlayerPopulation ReadPopulation(int slot)
	{
		Entity worldEntity = Simulation.Map.WorldEntity;
		if (!World.TryGet(worldEntity, out NetworkMappingState mapping) || mapping.PlayerEntities == null) return default;
		if (slot < 0 || slot >= mapping.PlayerEntities.Length) return default;

		return TryGet(new UnitId(mapping.PlayerEntities[slot].Id), out PlayerPopulation population) ? population : default;
	}

	/// <summary>
	///     Moves a placed prop to another point on the board, which is how a test puts the same node at two different
	///     distances from the same building without authoring a second board.
	/// </summary>
	/// <param name="prop">The prop entity to move.</param>
	/// <param name="position">The point it should stand at.</param>
	public void MoveProp(UnitId prop, Vector3 position)
	{
		World.Set(new Entity(prop.Value), new Position(position));
	}

	public float TotalCarriedAmount(int slot)
	{
		float total = 0f;
		foreach (UnitId unit in Owned(slot))
		{
			if (!IsAlive(unit)) continue;
			if (TryGet(unit, out Gatherer gatherer)) total += gatherer.CarriedAmount;
		}

		return total;
	}

	public bool IsGathering(UnitId unit) => Has<Gatherer>(unit);

	/// <summary>
	///     Sends a line the way chat does when a player types a cheat, so a test reads the authority's own answer rather
	///     than inferring it from the effect the cheat applied.
	/// </summary>
	/// <param name="slot">The seat that typed the line.</param>
	/// <param name="cheatWord">The word that was typed, exactly as it was typed.</param>
	/// <returns>What the authority answered the line with.</returns>
	public ServerCommandResult SubmitCheat(int slot, string cheatWord, params UnitId[] units)
	{
		var selectedIds = new System.Collections.Generic.List<int>();
		for (int index = 0; index < units.Length; index++)
		{
			if (units[index].IsValid) selectedIds.Add(units[index].Value);
		}

		var command = new NetworkCommand
		{
			CommandId = _nextCommandId++,
			CommandType = "cheat",
			UnitEntityIds = selectedIds,
			TargetPosition = new NetworkVector3(0f, 0f, 0f),
			TargetEntityId = 0,
			ArgString = cheatWord
		};

		return Send(slot, command);
	}

	/// <summary>
	///     Sends one of the movement orders the command card offers, so a test plays the same routing a button press does.
	/// </summary>
	/// <param name="slot">The seat issuing the order.</param>
	/// <param name="commandType">The command the card sends, such as stop, hold, attack_move or patrol.</param>
	/// <param name="position">The ground point the order names, or zero for an order that names none.</param>
	/// <param name="units">The units the order applies to.</param>
	/// <returns>What the authority answered the order with.</returns>
	public ServerCommandResult Order(int slot, string commandType, Vector3 position, params UnitId[] units)
	{
		var unitIds = new System.Collections.Generic.List<int>();
		for (int index = 0; index < units.Length; index++)
		{
			if (units[index].IsValid) unitIds.Add(units[index].Value);
		}

		var command = new NetworkCommand
		{
			CommandId = _nextCommandId++,
			CommandType = commandType,
			UnitEntityIds = unitIds,
			TargetPosition = new NetworkVector3(position.X, position.Y, position.Z),
			TargetEntityId = 0,
			ArgString = string.Empty
		};

		return Send(slot, command);
	}

	/// <summary>
	///     Sends the order a player's right-click on an enemy unit sends, so a test exercises the authority's own attack
	///     routing rather than writing a target component directly.
	/// </summary>
	/// <param name="slot">The seat issuing the order.</param>
	/// <param name="unit">The unit being ordered to attack.</param>
	/// <param name="target">The unit that was clicked.</param>
	/// <returns>What the authority answered the order with.</returns>
	public ServerCommandResult Attack(int slot, UnitId unit, UnitId target)
	{
		var command = new NetworkCommand
		{
			CommandId = _nextCommandId++,
			CommandType = "attack",
			UnitEntityIds = new System.Collections.Generic.List<int> { unit.Value },
			TargetPosition = new NetworkVector3(0f, 0f, 0f),
			TargetEntityId = target.Value,
			ArgString = string.Empty
		};

		return Send(slot, command);
	}

	/// <summary>
	///     Sends the order a player's right-click on a resource node sends, so a test exercises the authority's own gather
	///     routing rather than writing a gatherer component directly.
	/// </summary>
	/// <param name="slot">The seat issuing the order.</param>
	/// <param name="unit">The unit being ordered to gather.</param>
	/// <param name="nodePosition">The position of the node that was clicked.</param>
	/// <returns>What the authority answered the order with.</returns>
	public ServerCommandResult Gather(int slot, UnitId unit, Vector3 nodePosition)
	{
		var command = new NetworkCommand
		{
			CommandId = _nextCommandId++,
			CommandType = "gather",
			UnitEntityIds = new System.Collections.Generic.List<int> { unit.Value },
			TargetPosition = new NetworkVector3(nodePosition.X, nodePosition.Y, nodePosition.Z),
			TargetEntityId = 0,
			ArgString = string.Empty
		};

		return Send(slot, command);
	}

	/// <summary>
	///     Sends the order a player's right-click on a damaged building sends, so a test exercises the authority's own
	///     repair routing rather than writing a repair task directly.
	/// </summary>
	/// <param name="slot">The seat issuing the order.</param>
	/// <param name="unit">The unit being ordered to mend the building.</param>
	/// <param name="building">The building that was clicked.</param>
	/// <returns>What the authority answered the order with.</returns>
	public ServerCommandResult Repair(int slot, UnitId unit, UnitId building)
	{
		var command = new NetworkCommand
		{
			CommandId = _nextCommandId++,
			CommandType = "repair",
			UnitEntityIds = new System.Collections.Generic.List<int> { unit.Value },
			TargetPosition = new NetworkVector3(0f, 0f, 0f),
			TargetEntityId = building.Value,
			ArgString = string.Empty
		};

		return Send(slot, command);
	}

	/// <summary>
	///     Reads whether a building is dormant, which is the state a repairable building is left in when its health runs out.
	/// </summary>
	public bool IsDormant(UnitId unit) => Has<Realm.Ecs.Components.Tags.Dormant>(unit);

	/// <summary>Reads whether a building is one the map lets fall dormant instead of being destroyed.</summary>
	public bool IsRepairable(UnitId unit) => Has<Realm.Ecs.Components.Tags.RepairableBuilding>(unit);

	/// <summary>
	///     Starts a construction the way a player's Begin Construction button does, and reads back the packages the
	///     command grid is now offering: the units that carry the grid only show the abilities the map revealed.
	/// </summary>
	public DraftOffer BeginConstruction(int slot, Vector3 position)
	{
		Cast(slot, BeginConstructionAbilityId, position);
		return new DraftOffer(slot, DraftOptions(slot));
	}

	/// <summary>
	///     The blueprint a draw leaves standing on the ground the player cast at. It is the unit whose command grid offers
	///     the packages the deck drew, so a test reads the draw from it rather than from the hero that paid for the site.
	/// </summary>
	/// <param name="slot">The seat whose blueprint is wanted.</param>
	/// <returns>The blueprint, or an invalid id when no draw is open.</returns>
	public UnitId Blueprint(int slot)
	{
		foreach (UnitId unit in Owned(slot))
		{
			if (!IsAlive(unit)) continue;
			if (string.Equals(TemplateOf(unit), "unit/blueprint", StringComparison.OrdinalIgnoreCase)) return unit;
		}

		return UnitId.Invalid;
	}

	public string[] DraftOptions(int slot)
	{
		var options = new List<string>();
		UnitId blueprint = Blueprint(slot);
		if (!blueprint.IsValid) return options.ToArray();

		foreach (string buildingTemplateId in MeleeContent.DraftableBuildings)
		{
			if (IsAbilityVisible(blueprint, buildingTemplateId)) options.Add(buildingTemplateId);
		}

		return options.ToArray();
	}

	public bool IsDraftAbandonVisible(int slot)
	{
		UnitId blueprint = Blueprint(slot);
		return blueprint.IsValid && IsAbilityVisible(blueprint, AbandonConstructionAbilityId);
	}

	public bool IsAbilityVisible(UnitId unit, string abilityId)
	{
		if (!unit.IsValid || !IsAlive(unit)) return false;

		IReadOnlyList<string> carried = Api.GetUnitTypeAbilities(TemplateOf(unit));
		bool carriesAbility = false;
		for (int index = 0; index < carried.Count; index++)
		{
			if (!string.Equals(carried[index], abilityId, StringComparison.OrdinalIgnoreCase)) continue;

			carriesAbility = true;
			break;
		}

		if (!carriesAbility) return false;

		return !Api.Abilities.IsUnitAbilityHidden(unit.UniqueId, abilityId);
	}

	/// <summary>
	///     Reads the tooltip a player is shown for an ability: the last one the authority published for it, whether it
	///     arrived with the registration or was set afterwards.
	/// </summary>
	public string AbilityTooltip(int slot, string abilityId)
	{
		string tooltip = string.Empty;
		for (int index = 0; index < _events.Count; index++)
		{
			ServerPresentationEvent candidate = _events[index];
			if (candidate.Kind != PresentationKind.AbilityRegistered && candidate.Kind != PresentationKind.AbilityTooltipSet) continue;
			if (candidate.PlayerIndex != slot && candidate.PlayerIndex != ServerPresentationEvent.Everyone) continue;
			if (!string.Equals(candidate.Key, abilityId, StringComparison.OrdinalIgnoreCase)) continue;

			tooltip = candidate.Text;
		}

		return tooltip;
	}

	/// <summary>
	///     Casts a package the command grid is offering, which is what pressing that command button sends.
	/// </summary>
	public void LockDraftOption(int slot, string buildingTemplateId, Vector3 position)
	{
		UnitId blueprint = Blueprint(slot);
		if (!blueprint.IsValid) throw new InvalidOperationException("No blueprint stands to answer the draw from.");

		CastFrom(slot, blueprint, buildingTemplateId, position);
		if (DraftOptions(slot).Length == 0) StepTicks(2);
	}

	public void AbandonDraft(int slot)
	{
		UnitId blueprint = Blueprint(slot);
		if (!blueprint.IsValid) return;

		CastFrom(slot, blueprint, AbandonConstructionAbilityId, Vector3.Zero);
		StepTicks(2);
	}

	public ServerCommandResult Send(int slot, NetworkCommand command)
	{
		ServerCommandResult result = Simulation.ApplyCommand(PeerFor(slot), command);
		DrainPresentation();
		return result;
	}

	public UnitId SpawnSoldier(int slot, string templateId, Vector3 position)
	{
		UnitId soldier = Api.SpawnUnitForPlayer(templateId, new Vector2(position.X, position.Z), slot, executeSpawnShader: true, bypassPopulation: true);
		DrainPresentation();
		return soldier;
	}

	public bool AddRelic(int slot, string itemId)
	{
		bool added = Api.AddItemToUnit(Hero(slot), itemId);
		DrainPresentation();
		return added;
	}

	public bool UnitHasItem(UnitId unit, string itemId) => Api.UnitHasItem(unit, itemId);

	public float ExperienceOf(UnitId unit) => TryGet(unit, out Experience experience) ? experience.Current : 0f;

	public bool IsAbilityDisabled(UnitId unit, string abilityId) => Api.Abilities.IsUnitAbilityDisabled(unit.UniqueId, abilityId);

	/// <summary>
	///     Casts an ability from a chosen unit, which is how a player uses the Nexus it selected rather than its hero.
	/// </summary>
	public void CastFrom(int slot, UnitId caster, string abilityId, Vector3 position)
	{
		Api.CastAbility(caster, abilityId, position);
		DrainPresentation();
	}

	/// <summary>
	///     Reads the last feedback line a seat was shown, which is where a refused purchase explains itself.
	/// </summary>
	public string LastFeedbackText(int slot)
	{
		string text = string.Empty;
		for (int index = 0; index < _events.Count; index++)
		{
			ServerPresentationEvent candidate = _events[index];
			if (candidate.Kind != PresentationKind.PlayerFeedbackText) continue;
			if (candidate.PlayerIndex != slot && candidate.PlayerIndex != ServerPresentationEvent.Everyone) continue;

			text = candidate.Text;
		}

		return text;
	}

	/// <summary>
	///     Reads one inspector row the authority published for a unit, as the client's stat panel receives it.
	/// </summary>
	/// <param name="slot">The seat whose presentation stream is read.</param>
	/// <param name="unit">The unit whose readout is being read.</param>
	/// <param name="row">The readout row, zero based.</param>
	/// <param name="valueColumn">Whether to read the row's value instead of its name.</param>
	/// <returns>The last text published for that row, or an empty string when it was never published.</returns>
	public string UnitReadout(int slot, UnitId unit, int row, bool valueColumn = false)
	{
		string key = $"readout.{row}.{(valueColumn ? "value" : "name")}";
		string owner = unit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
		string text = string.Empty;

		for (int index = 0; index < _events.Count; index++)
		{
			ServerPresentationEvent candidate = _events[index];
			if (candidate.Kind != PresentationKind.TooltipTextSet) continue;
			if (candidate.PlayerIndex != slot && candidate.PlayerIndex != ServerPresentationEvent.Everyone) continue;
			if (!string.Equals(candidate.Key, "unit", StringComparison.OrdinalIgnoreCase)) continue;
			if (!string.Equals(candidate.Text, owner, StringComparison.Ordinal)) continue;
			if (!string.Equals(candidate.Text2, key, StringComparison.Ordinal)) continue;

			text = candidate.Text3;
		}

		return text;
	}

	public void Kill(UnitId target, UnitId attacker, float damage = 100000f)
	{
		Api.DealTypedDamage(attacker, target, damage, "physical");
		DrainPresentation();
	}

	/// <summary>
	///     Reads the unit an order is currently aimed at, which is how a test observes the target a ruleset's targeting
	///     rule selected rather than inferring it from damage other units may also have dealt.
	/// </summary>
	public UnitId CurrentTarget(UnitId unit)
	{
		return TryGet(unit, out AttackTarget target) ? new UnitId(target.Target.Id) : UnitId.Invalid;
	}

	public int LevelOf(UnitId unit)
	{
		return TryGet(unit, out Level level) ? level.Value : 0;
	}

	public float HealthOf(UnitId unit) => TryGet(unit, out Health health) ? health.Current : 0f;

	public float MaxHealthOf(UnitId unit) => TryGet(unit, out Health health) ? health.Max : 0f;

	/// <summary>
	///     Sums the modifiers one source granted to a unit, which is how a map's stat effect is observed without asking the
	///     derived stats how they fold it in.
	/// </summary>
	public float ModifierTotalFrom(UnitId unit, string sourceId, string statName)
	{
		if (!TryGet(unit, out ModifierState modifiers)) return 0f;

		var statId = new Realm.Ecs.Common.StatId(statName);
		float total = 0f;
		for (int index = 0; index < modifiers.Count; index++)
		{
			StatModifier modifier = modifiers.Get(index);
			if (!string.Equals(modifier.SourceUpgradeId, sourceId, StringComparison.OrdinalIgnoreCase)) continue;
			if (modifier.StatTypeId != statId) continue;

			total += modifier.Value;
		}

		return total;
	}

	/// <summary>
	///     Draws a construction and picks one of the packages the command grid offers, returning the site the map placed.
	///     A filter picks the package by the name its command carries, which is how a test asks for a building the deck drew
	///     rather than the first one it happened to offer.
	/// </summary>
	public UnitId PlaceDraftBuilding(Vector3 position, Func<string, bool>? optionFilter = null)
	{
		SetWood(1, 500f);
		DraftOffer offer = BeginConstruction(1, position);
		LockDraftOption(1, SelectOption(offer, optionFilter), position);

		foreach (UnitId building in Buildings(1))
		{
			if (Has<UnderConstruction>(building)) return building;
		}

		throw new InvalidOperationException("Picking a drawn package left no construction site.");
	}

	/// <summary>
	///     Raises a building and waits out its site, because a building only produces once it stands and the authored
	///     construction times differ per building.
	/// </summary>
	public UnitId PlaceCompletedBuilding(Vector3 position, Func<string, bool>? optionFilter = null)
	{
		UnitId building = PlaceDraftBuilding(position, optionFilter);

		float waited = 0f;
		while (IsUnderConstruction(building) && waited < LongestAuthoredConstructionSeconds)
		{
			Step(1f);
			waited += 1f;
		}

		if (IsUnderConstruction(building)) throw new InvalidOperationException("The placement did not finish within the longest authored construction time.");

		return building;
	}

	private string SelectOption(DraftOffer offer, Func<string, bool>? optionFilter)
	{
		if (offer.AbilityIds.Length == 0) throw new InvalidOperationException("The draw offered no building.");
		if (optionFilter == null) return offer.AbilityIds[0];

		for (int index = 0; index < offer.AbilityIds.Length; index++)
		{
			string abilityId = offer.AbilityIds[index];
			if (optionFilter(Api.GetAbilityName(abilityId))) return abilityId;
		}

		throw new InvalidOperationException($"The draw offered no building matching the filter: {string.Join(", ", offer.AbilityIds)}.");
	}

	/// <summary>
	///     Waits for the player's first soldier and returns how many soldiers it then holds.
	/// </summary>
	public int StepUntilSoldiers(int slot)
	{
		float waited = 0f;
		while (Soldiers(slot).Count == 0 && waited < LongestAuthoredProductionSeconds)
		{
			Step(1f);
			waited += 1f;
		}

		return Soldiers(slot).Count;
	}

	public int HeroLevel(int slot)
	{
		UnitId hero = Hero(slot);
		return TryGet(hero, out Level level) ? level.Value : 0;
	}

	public int BuildingLimitOf(int slot)
	{
		Entity playerEntity = Simulation.Map.GetPlayerEntity(slot).Value;
		return World.TryGet(playerEntity, out PlayerBuildingLimit limit) ? limit.Value : 0;
	}

	/// <summary>Reads how many charges a building may hold at most, which a lobby's charge multiplier scales.</summary>
	public int MaximumChargesOf(UnitId building)
	{
		return TryGet(building, out UnitCharges charges) ? charges.MaximumCharges : 0;
	}

	public int CountCharges(UnitId building)
	{
		Entity entity = new(building.Value);
		return World.TryGet(entity, out OverheadBadge badge) ? badge.Value : 0;
	}

	public float ConstructionProgress(UnitId building) => Api.GetUnitConstructionProgress(building);

	public bool IsUnderConstruction(UnitId building) => Has<UnderConstruction>(building);
}

internal readonly record struct DraftOffer(int Slot, string[] AbilityIds);
