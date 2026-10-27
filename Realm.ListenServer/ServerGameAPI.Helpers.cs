using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.MapAPI;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	private static readonly QueryDescription ResourceNodeQuery = new QueryDescription().WithAll<ResourceNode>();
	private static readonly QueryDescription OwnedUnitQuery = new QueryDescription().WithAll<Owner>().WithNone<Dead>();

	private readonly EntityCallback<ResourceNode> _countResourceNodes;
	private readonly EntityCallback<Owner> _countOwnedUnits;
	private Entity _countedOwner;

	private delegate void PlayersStateMutation(ref ScriptPlayersState playersState, int playerSlot);

	/// <summary>Which of a player's three resources a write or a read is about.</summary>
	private enum PlayerResourceKind
	{
		Gold,
		Wood,
		Stone
	}

	/// <summary>The script-visible player table the world carries, if the match has published one.</summary>
	private bool TryReadPlayersState(out ScriptPlayersState playersState)
	{
		playersState = default;

		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return false;
		if (!_context.World.Has<ScriptPlayersState>(worldEntity)) return false;

		playersState = _context.World.Get<ScriptPlayersState>(worldEntity);
		return true;
	}

	/// <summary>
	///     Changes one player's script-visible row and publishes the row back to the buffer a script reads, so a write
	///     through the surface and a read through a player handle agree on the same numbers.
	/// </summary>
	private void MutatePlayersState(int playerIndex, PlayersStateMutation mutation)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return;
		if (!_context.World.Has<ScriptPlayersState>(worldEntity)) return;

		ref ScriptPlayersState playersState = ref _context.World.Get<ScriptPlayersState>(worldEntity);
		if (!playersState.IsValidSlot(playerIndex)) return;

		mutation(ref playersState, playerIndex);

		ScriptPlayer updated = playersState.Get(playerIndex);
		_context.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in updated);
	}

	private float ReadPlayerResource(int playerIndex, PlayerResourceKind kind)
	{
		if (!TryReadPlayersState(out ScriptPlayersState playersState) || !playersState.IsValidSlot(playerIndex)) return 0f;

		ScriptPlayer player = playersState.Get(playerIndex);
		return kind switch
		{
			PlayerResourceKind.Gold => player.Gold,
			PlayerResourceKind.Wood => player.Wood,
			_ => player.Stone
		};
	}

	/// <summary>
	///     Writes a player's resource where the match reads it and where clients are told about it. Gold is also kept in
	///     the replicated resource store, which is what the resource bars and costs are settled against.
	/// </summary>
	private void WritePlayerResource(int playerIndex, PlayerResourceKind kind, float amount)
	{
		float clamped = Math.Max(0f, amount);

		MutatePlayersState(playerIndex, (ref ScriptPlayersState playersState, int playerSlot) =>
		{
			switch (kind)
			{
				case PlayerResourceKind.Gold:
					playersState.SetGold(playerSlot, clamped);
					break;
				case PlayerResourceKind.Wood:
					playersState.SetWood(playerSlot, clamped);
					break;
				default:
					playersState.SetStone(playerSlot, clamped);
					break;
			}
		});

		Entity playerEntity = _context.GetPlayerEntity(playerIndex);
		if (playerEntity == Entity.Null) return;
		if (!_context.World.Has<PlayerResources>(playerEntity)) return;

		ref PlayerResources resources = ref _context.World.Get<PlayerResources>(playerEntity);
		switch (kind)
		{
			case PlayerResourceKind.Gold:
				resources[_context.PlayerResourceService.GoldResourceId] = (int)clamped;
				break;
			case PlayerResourceKind.Wood:
				resources[_context.PlayerResourceService.WoodResourceId] = (int)clamped;
				break;
			default:
				resources[_context.PlayerResourceService.StoneResourceId] = (int)clamped;
				break;
		}
	}

	/// <summary>The entity a script's unit handle names, or nothing when it names a unit that is gone.</summary>
	private Entity ResolveUnit(UnitId unit)
	{
		if (!unit.IsValid) return Entity.Null;

		var entity = new Entity(unit.Value);
		return _context.World.IsAlive(entity) ? entity : Entity.Null;
	}

	/// <summary>Stops a unit taking orders and animating, which is what a script means by frozen.</summary>
	private void SetFrozenFlags(Entity entity, bool frozen)
	{
		if (frozen)
		{
			if (!_context.World.Has<CommandsFrozen>(entity)) _context.World.Add(entity, default(CommandsFrozen));
			if (!_context.World.Has<AnimationsFrozen>(entity)) _context.World.Add(entity, default(AnimationsFrozen));
			return;
		}

		if (_context.World.Has<CommandsFrozen>(entity)) _context.World.Remove<CommandsFrozen>(entity);
		if (_context.World.Has<AnimationsFrozen>(entity)) _context.World.Remove<AnimationsFrozen>(entity);
	}

	/// <summary>Turns a unit's flag component on or off, which is how the surface immunizes or freezes a unit.</summary>
	private void SetUnitFlag<TFlag>(UnitId unit, bool enabled) where TFlag : unmanaged
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		if (enabled)
		{
			if (!_context.World.Has<TFlag>(entity)) _context.World.Add(entity, default(TFlag));
			return;
		}

		if (_context.World.Has<TFlag>(entity)) _context.World.Remove<TFlag>(entity);
	}

	private void CountResourceNodes(Entity entity, ref ResourceNode node) => _countingScratch++;

	private void CountOwnedUnits(Entity entity, ref Owner owner)
	{
		if (owner.PlayerEntity.Value == _countedOwner) _countingScratch++;
	}
}
