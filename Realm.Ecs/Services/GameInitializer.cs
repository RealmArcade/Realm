using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System.Numerics;

namespace Realm.Ecs.Services;

/// <summary>
///     A conceptual service that initializes the game state, including players and their starting units.
///     This demonstrates how PlayerEntity and Owner components would be used in practice.
/// </summary>
public class GameInitializer
{
	private readonly DefinitionManager _definitionManager;
	private readonly EntityFactory _entityFactory;
	private readonly WorldAccessor _ecsWorldAccessor;

	public GameInitializer(WorldAccessor ecsWorldAccessor, MapLoader mapLoader)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		_definitionManager = mapLoader.DefinitionManager;
		var archetypeManager = mapLoader.ArchetypeManager;
		_entityFactory = new EntityFactory(_ecsWorldAccessor, archetypeManager, mapLoader.DefinitionManager);
	}

	/// <summary>
	///     Creates a player entity and spawns a starting unit for them, assigning ownership.
	/// </summary>
	public void InitializePlayer(string playerName, string startingUnitArchetypeId)
	{
		var playerEntity = _ecsWorldAccessor.Current.Create();
		_ecsWorldAccessor.Current.Set(playerEntity, new Player());
		_ecsWorldAccessor.Current.Set(playerEntity, new Name(playerName));
		_ecsWorldAccessor.Current.Set(playerEntity, new PlayerResources(new Dictionary<ResourceId, int>
		{
			{ "Gold".AsResourceId(_definitionManager), 500 }
		}));

		var typedPlayerEntity = playerEntity.AsPlayerEntity(_ecsWorldAccessor.Current);

		var unitEntity =
			_entityFactory.SpawnUnit(startingUnitArchetypeId, new Vector3(0, 0, 0));

		_ecsWorldAccessor.Current.Set(unitEntity, new Owner(typedPlayerEntity));

		Console.WriteLine($"{playerName} initialized with a {startingUnitArchetypeId} unit.");
	}
}