using Realm.Ecs.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Services;
using Realm.Shared.Simulation;

namespace Realm.Shared.Maps;

/// <summary>
///     The authored stats, costs and model keys a map declares, keyed by template id. Both the client and the
///     authoritative process build one from the same <c>metadata.json</c>, so a unit is spawned with identical numbers
///     wherever it is spawned from, and the identifier aliases and default fixups exist in exactly one place.
/// </summary>
public class DefinitionCatalog
{
	private static readonly string[] StrippablePrefixes = ["unit/", "building/", "prop/", "resource/", "item/", "upgrade/", "weapon/", "ability/"];

	private readonly Dictionary<string, UnitMetadata> _units = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, UnitMetadata> _buildings = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PropMetadata> _props = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ResourceMetadata> _resources = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ItemMetadata> _items = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, UpgradeMetadata> _upgrades = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, WeaponMetadata> _weapons = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, AbilityMetadata> _abilities = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>The metadata document this catalog was built from.</summary>
	public MapMetadata Metadata { get; }

	private DefinitionCatalog(MapMetadata metadata)
	{
		Metadata = metadata;
	}

	/// <summary>Loads and indexes the catalog a map folder declares.</summary>
	public static DefinitionCatalog LoadFromMapFolder(string mapFolderPath)
	{
		return Build(MapFileService.LoadMetadata(mapFolderPath));
	}

	/// <summary>
	///     Indexes a metadata document. The same objects are stored under their full id and under their id with the
	///     type prefix removed, which is how map files and map scripts have always addressed them.
	/// </summary>
	public static DefinitionCatalog Build(MapMetadata metadata)
	{
		var catalog = new DefinitionCatalog(metadata ?? new MapMetadata());
		TemplateContainer templates = catalog.Metadata.Templates ??= new TemplateContainer();

		for (int index = 0; index < templates.Units.Count; index++)
		{
			UnitMetadata unit = templates.Units[index];
			if (string.IsNullOrEmpty(unit.TemplateID)) continue;
			if (unit.Scale <= 0f) unit.Scale = 1.0f;
			catalog.AddWithAliases(catalog._units, unit.TemplateID, unit);
		}

		for (int index = 0; index < templates.Buildings.Count; index++)
		{
			UnitMetadata building = templates.Buildings[index];
			if (string.IsNullOrEmpty(building.TemplateID)) continue;
			if (building.Scale <= 0f) building.Scale = 1.5f;
			catalog.AddWithAliases(catalog._buildings, building.TemplateID, building);
		}

		for (int index = 0; index < templates.Resources.Count; index++)
		{
			ResourceMetadata resource = templates.Resources[index];
			if (string.IsNullOrEmpty(resource.TemplateID)) continue;
			if (resource.Scale <= 0f) resource.Scale = 2.75f;
			if (resource.PathingType == 0) resource.PathingType = 255;
			catalog.AddWithAliases(catalog._resources, resource.TemplateID, resource);
		}

		for (int index = 0; index < templates.Props.Count; index++)
		{
			PropMetadata prop = templates.Props[index];
			if (string.IsNullOrEmpty(prop.TemplateID)) continue;
			if (prop.Scale <= 0f) prop.Scale = 1.25f;
			if (prop.PathingType == 0) prop.PathingType = 255;
			catalog.AddWithAliases(catalog._props, prop.TemplateID, prop);
		}

		for (int index = 0; index < templates.Items.Count; index++)
		{
			ItemMetadata item = templates.Items[index];
			if (!string.IsNullOrEmpty(item.TemplateID)) catalog.AddWithAliases(catalog._items, item.TemplateID, item);
		}

		for (int index = 0; index < templates.Upgrades.Count; index++)
		{
			UpgradeMetadata upgrade = templates.Upgrades[index];
			if (!string.IsNullOrEmpty(upgrade.TemplateID)) catalog.AddWithAliases(catalog._upgrades, upgrade.TemplateID, upgrade);
		}

		for (int index = 0; index < templates.Weapons.Count; index++)
		{
			WeaponMetadata weapon = templates.Weapons[index];
			if (!string.IsNullOrEmpty(weapon.TemplateID)) catalog.AddWithAliases(catalog._weapons, weapon.TemplateID, weapon);
		}

		for (int index = 0; index < templates.Abilities.Count; index++)
		{
			AbilityMetadata ability = templates.Abilities[index];
			if (!string.IsNullOrEmpty(ability.TemplateID)) catalog.AddWithAliases(catalog._abilities, ability.TemplateID, ability);
		}

		return catalog;
	}

	/// <summary>
	///     Points a match's rule catalog at these templates, so a rule that asks what a definition can build or what
	///     population it costs gets the map's answer. Both processes bind the catalog they loaded, which is what keeps a
	///     delivery point and a population total identical on either side.
	/// </summary>
	public void BindTo(MapRulesCatalog rules)
	{
		rules.BuildOptionsProvider = templateId => FindBuilding(templateId)?.BuildOptions;
		rules.PopulationCostProvider = templateId => FindUnit(templateId)?.PopCost ?? 0;
	}

	/// <summary>
	///     Points the command authority at these templates, so the mana a cast spends, the cooldown it starts and the
	///     resources an order costs are the map's numbers. Both processes bind what they loaded, which is what stops an
	///     order being affordable on one side and refused on the other.
	/// </summary>
	public void BindTo(CommandAuthorityService authority)
	{
		authority.AbilityRulesProvider = spellId => FindAbility(spellId) is { } ability
			? new AbilityRules(ability.ManaCost, ability.Cooldown, ability.TargetRange)
			: null;

		authority.ProductionCostsProvider = unitId => FindUnitOrBuilding(unitId) is { } unit
			? new ProductionCosts(unit.CostGold, unit.CostWood, unit.CostStone, unit.ProductionTime)
			: null;

		authority.UpgradeRulesProvider = upgradeId => FindUpgrade(upgradeId) is { } upgrade
			? new UpgradeRules(upgrade.TemplateID, upgrade.MaxLevel > 0 ? upgrade.MaxLevel : 1, upgrade.CostGold, upgrade.CostWood, upgrade.CostStone)
			: null;
	}

	public UnitMetadata? FindUnit(string templateId) => Find(_units, templateId);

	public UnitMetadata? FindBuilding(string templateId) => Find(_buildings, templateId);

	/// <summary>Resolves a unit or a building, which is how a save file and a training order name either one.</summary>
	public UnitMetadata? FindUnitOrBuilding(string templateId) => Find(_units, templateId) ?? Find(_buildings, templateId);

	public PropMetadata? FindProp(string templateId) => Find(_props, templateId);

	public ResourceMetadata? FindResource(string templateId) => Find(_resources, templateId);

	public ItemMetadata? FindItem(string templateId) => Find(_items, templateId);

	public UpgradeMetadata? FindUpgrade(string templateId) => Find(_upgrades, templateId);

	public WeaponMetadata? FindWeapon(string templateId) => Find(_weapons, templateId);

	public AbilityMetadata? FindAbility(string templateId) => Find(_abilities, templateId);

	public IReadOnlyDictionary<string, UnitMetadata> Units => _units;

	public IReadOnlyDictionary<string, UnitMetadata> Buildings => _buildings;

	public IReadOnlyDictionary<string, PropMetadata> Props => _props;

	public IReadOnlyDictionary<string, ResourceMetadata> Resources => _resources;

	/// <summary>
	///     The resource amount a prop yields when the map does not declare one. Gold mines, rock and trees are the
	///     engine's own resource node types, so their amounts live here rather than in whichever process loads them.
	/// </summary>
	public static float DefaultResourceAmount(string propId)
	{
		return propId switch
		{
			"goldmine" => 2000f,
			"rock" => 1000f,
			"tree" => 500f,
			_ => 0f
		};
	}

	private void AddWithAliases<T>(Dictionary<string, T> target, string templateId, T value)
	{
		target[templateId] = value;

		for (int index = 0; index < StrippablePrefixes.Length; index++)
		{
			string prefix = StrippablePrefixes[index];
			if (!templateId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

			target.TryAdd(templateId[prefix.Length..], value);
			return;
		}
	}

	private static T? Find<T>(Dictionary<string, T> source, string templateId) where T : class
	{
		if (string.IsNullOrEmpty(templateId)) return null;
		return source.TryGetValue(templateId, out T? value) ? value : null;
	}
}
