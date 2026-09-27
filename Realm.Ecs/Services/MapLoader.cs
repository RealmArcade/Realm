using Realm.Ecs.Definitions;
using System.Text.Json;

namespace Realm.Ecs.Services;

/// <summary>
///     Responsible for loading the game's map definition (metadata.json) and
///     orchestrating the initialization of all data managers.
/// </summary>
internal class MapLoader
{
	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public MapLoader(WorldAccessor ecsWorldAccessor, string definitionsBasePath)
	{
		DefinitionManager = new DefinitionManager(ecsWorldAccessor);

		var metadataJsonPath = Path.Combine(definitionsBasePath, "metadata.json");
		var mapJson = File.Exists(metadataJsonPath) ? File.ReadAllText(metadataJsonPath) : "{}";
		MapDefinition = JsonSerializer.Deserialize<MapDefinition>(mapJson, Options) ?? new MapDefinition();

		ArchetypeManager = new ArchetypeManager(ecsWorldAccessor, MapDefinition.Units, DefinitionManager);
	}

	public MapDefinition MapDefinition { get; }
	public DefinitionManager DefinitionManager { get; }
	public ArchetypeManager ArchetypeManager { get; private set; }
}