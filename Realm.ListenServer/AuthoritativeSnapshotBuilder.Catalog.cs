using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>the rule and text catalog entries a snapshot carries.</summary>
public partial class AuthoritativeSnapshotBuilder
	{
	private void AppendRuleEntries(List<CatalogEntry> entries)
	{
		IReadOnlyList<(string DefinitionKey, float Radius)> placementRadii = _rules.PlacementRadiusRules;
		for (int index = 0; index < placementRadii.Count; index++)
		{
			entries.Add(new CatalogEntry { Channel = "placement_radius", Key = placementRadii[index].DefinitionKey, Number = placementRadii[index].Radius });
		}

		IReadOnlyList<string> dropoffBuildings = _rules.ResourceDropoffBuildingKeys;
		for (int index = 0; index < dropoffBuildings.Count; index++)
		{
			entries.Add(new CatalogEntry { Channel = "dropoff", Key = dropoffBuildings[index] });
		}

		IReadOnlyList<string> workerUnits = _rules.ResourceWorkerUnitKeys;
		for (int index = 0; index < workerUnits.Count; index++)
		{
			entries.Add(new CatalogEntry { Channel = "worker", Key = workerUnits[index] });
		}

		IReadOnlyList<string> primaryBuildings = _rules.PrimaryBuildingIds;
		for (int index = 0; index < primaryBuildings.Count; index++)
		{
			entries.Add(new CatalogEntry { Channel = "primary", Key = primaryBuildings[index] });
		}

		foreach (var nodeType in _rules.ResourceNodeTypes)
		{
			entries.Add(new CatalogEntry { Channel = "resource_node_type", Key = nodeType.Key, Value = nodeType.Value });
		}

		entries.Add(new CatalogEntry { Channel = "inventory_slots", Key = "unit", Number = _rules.InventorySlotLimit });

		entries.Add(new CatalogEntry { Channel = "unit_bar", Key = "health", Number = _rules.UnitHealthBarEnabled ? 1f : 0f });
		entries.Add(new CatalogEntry { Channel = "unit_bar", Key = "mana", Number = _rules.UnitManaBarEnabled ? 1f : 0f });
		entries.Add(new CatalogEntry { Channel = "unit_bar", Key = "level", Number = _rules.UnitLevelBadgeEnabled ? 1f : 0f });

		TechLevels?.AppendDescription(entries);
	}

	private const string TextChannel = "string";

	private void AppendTextEntries(SnapshotPeerState state, List<CatalogEntry> entries)
	{
		int entryCount = StringRegistry.EntryCount;
		int cursor = state.StringTableCursor > entryCount ? 0 : state.StringTableCursor;

		_stringTableEntries.Clear();
		state.StringTableCursor = StringRegistry.GetEntriesSince(cursor, _stringTableEntries);

		for (int index = 0; index < _stringTableEntries.Count; index++)
		{
			(int id, string text) = _stringTableEntries[index];
			if (id == 0 || string.IsNullOrEmpty(text)) continue;

			entries.Add(new CatalogEntry
			{
				Channel = TextChannel,
				Key = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
				Value = text
			});
		}
	}

	private readonly List<(int Id, string Text)> _stringTableEntries = new();

	/// <summary>
	///     Describes the text a text-list payload names in the same snapshot that describes the payload itself.
	/// </summary>
	/// <remarks>
	///     The incremental text table only tells a peer about text registered since its cursor, and the cursor advances on
	///     the snapshot that carries those entries. A payload whose text was registered long before it — an ability list a
	///     unit type only ever fields when it is first spawned, for instance — therefore arrives with handles this peer has
	///     never been given the text for, and every slot renders as a blank named slot until the next baseline. Pairing the
	///     two makes the payload carry its own text, so a payload arriving is enough to render it.
	/// </remarks>
	private void AppendTextBehindPayload(WorldSnapshot snapshot, int handle)
	{
		int textCount = ContentIdStore.GetTextListCount(handle);
		for (int index = 0; index < textCount; index++)
		{
			int textId = ContentIdStore.GetTextListItemId(handle, index);
			if (textId == 0) continue;
			if (!StringRegistry.TryGetText(textId, out string text) || string.IsNullOrEmpty(text)) continue;

			snapshot.Catalog.Add(new CatalogEntry
			{
				Channel = TextChannel,
				Key = textId.ToString(System.Globalization.CultureInfo.InvariantCulture),
				Value = text
			});
		}
	}
}
