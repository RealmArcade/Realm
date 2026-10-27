using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.MapAPI;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     Abilities as a map script describes and adjusts them: what they are called, what their cards read, and how each
///     unit's own abilities are currently presented. The authority holds the answer, so a card a script wrote and a
///     command a unit cannot take are one decision every client is told about rather than one each client works out.
/// </summary>
public partial class ServerGameAPI
{
	private readonly ScriptAbilityRegistry _abilities;

	private const string AbilityOwnerKind = "ability";
	private const string ItemOwnerKind = "item";
	private const string UnitOwnerKind = "unit";
	private const string UpgradeOwnerKind = "upgrade";

	/// <summary>What puts a name and a card on an ability no template in the map declares.</summary>
	public ScriptAbilityRegistry Abilities => _abilities;

	/// <summary>Adds an ability to every unit and building of a template, which is how a map hands out its own buttons.</summary>
	public void AddUnitTypeAbility(string unitTypeId, string abilityId)
	{
		if (string.IsNullOrWhiteSpace(unitTypeId) || string.IsNullOrWhiteSpace(abilityId)) return;

		if (AddAbilityToTemplate(_map.Catalog.FindUnit(unitTypeId), abilityId)) return;

		AddAbilityToTemplate(_map.Catalog.FindBuilding(unitTypeId), abilityId);
	}

	/// <summary>The abilities a unit or building template offers, in the order a map declared them.</summary>
	public IReadOnlyList<string> GetUnitTypeAbilities(string unitTypeId)
	{
		if (string.IsNullOrWhiteSpace(unitTypeId)) return Array.Empty<string>();

		UnitMetadata? template = _map.Catalog.FindUnitOrBuilding(unitTypeId);
		return template?.Abilities ?? Array.Empty<string>();
	}

	/// <summary>The name a unit or building shows, as its template declares it.</summary>
	public string GetUnitName(string unitTypeId)
	{
		if (string.IsNullOrWhiteSpace(unitTypeId)) return string.Empty;

		UnitMetadata? template = _map.Catalog.FindUnitOrBuilding(unitTypeId);
		if (template != null && !string.IsNullOrWhiteSpace(template.Name)) return template.Name;

		return Path.GetFileNameWithoutExtension(unitTypeId);
	}

	/// <summary>The name an ability shows, as the map registered it or as its template declares it.</summary>
	public string GetAbilityName(string abilityId)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return string.Empty;

		return _abilities.GetName(abilityId, _map.Catalog.FindAbility(abilityId)?.Name);
	}

	/// <summary>The text a string id was registered under, which is how a map names something both sides already hold.</summary>
	public string GetStringText(int stringId) => stringId <= 0 ? string.Empty : StringRegistry.Get(stringId);

	/// <summary>Puts a name and a card on an ability every player can be offered.</summary>
	public void RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath = "", bool isInstant = false)
	{
		if (!_abilities.Register(abilityId, displayName, tooltip, iconPath, isInstant)) return;

		_presentation.Write(PresentationKind.AbilityRegistered, AllPlayers, 0, abilityId, displayName ?? string.Empty, tooltip ?? string.Empty, iconPath ?? string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, isInstant ? 1 : 0, 0, 0f, 0f, 0f, isInstant);
	}

	/// <summary>Puts a name and a card on an ability one player is offered, which is what a map does for one seat.</summary>
	public void RegisterPlayerAbility(int playerIndex, string abilityId, string displayName, string tooltip, string iconPath = "", bool isInstant = false)
	{
		if (!_abilities.Register(abilityId, displayName, tooltip, iconPath, isInstant)) return;

		_presentation.Write(PresentationKind.PlayerAbilityRegistered, playerIndex, 0, abilityId, displayName ?? string.Empty, tooltip ?? string.Empty, iconPath ?? string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, isInstant ? 1 : 0, 0, 0f, 0f, 0f, isInstant);
	}

	public void SetAbilityInstant(string abilityId, bool isInstant)
	{
		_abilities.SetInstant(abilityId, isInstant);
		WriteAbilityDetail(PresentationKind.AbilityInstantSet, abilityId, string.Empty, isInstant ? 1 : 0);
	}

	public void SetAbilityIcon(string abilityId, string iconPath)
	{
		_abilities.SetIcon(abilityId, iconPath);
		WriteAbilityDetail(PresentationKind.AbilityIconSet, abilityId, iconPath ?? string.Empty, 0);
	}

	public void SetAbilityTooltip(string abilityId, string tooltip)
	{
		_abilities.SetTooltip(abilityId, tooltip);
		WriteAbilityDetail(PresentationKind.AbilityTooltipSet, abilityId, tooltip ?? string.Empty, 0);
	}

	public void SetAbilityHotkey(string abilityId, string hotkey)
	{
		_abilities.SetHotkey(abilityId, hotkey);
		WriteAbilityDetail(PresentationKind.AbilityHotkeySet, abilityId, hotkey ?? string.Empty, 0);
	}

	public void SetAbilityGridPosition(string abilityId, int x, int y) =>
		WriteAbilityDetail(PresentationKind.AbilityGridPositionSet, abilityId, string.Empty, x, y, () => _abilities.SetGridPosition(abilityId, x, y));

	public void SetItemTooltip(string itemId, string tooltip)
	{
		if (string.IsNullOrWhiteSpace(itemId)) return;

		ItemMetadata? item = _map.Catalog.FindItem(itemId);
		if (item != null) item.Description = tooltip ?? string.Empty;

		_presentation.Write(PresentationKind.ItemTooltipSet, AllPlayers, 0, itemId, tooltip ?? string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	/// <summary>Publishes a number one of the map's own cards reads.</summary>
	public void SetAbilityTooltipValue(string abilityId, string key, float value) => WriteTooltipNumber(AbilityOwnerKind, abilityId, key, value);

	public void SetAbilityTooltipText(string abilityId, string key, string value) => WriteTooltipText(AbilityOwnerKind, abilityId, key, value);

	public void SetUnitTooltipValue(UnitId unit, string key, float value)
	{
		if (!unit.IsValid) return;

		WriteTooltipNumber(UnitOwnerKind, unit.Value.ToString(), key, value);
	}

	public void SetUnitTooltipText(UnitId unit, string key, string value)
	{
		if (!unit.IsValid) return;

		WriteTooltipText(UnitOwnerKind, unit.Value.ToString(), key, value);
	}

	public void SetItemTooltipValue(string itemId, string key, float value) => WriteTooltipNumber(ItemOwnerKind, itemId, key, value);

	public void SetUpgradeTooltipValue(string upgradeId, string key, float value) => WriteTooltipNumber(UpgradeOwnerKind, upgradeId, key, value);

	/// <summary>
	///     Shows or hides one of a unit's abilities, which is how a map marks an order the unit cannot take right now.
	///     The change is published to peers against the ability's slot in that unit's own list.
	/// </summary>
	public void SetAbilityState(UnitId unit, string abilityId, bool disabled, bool hidden)
	{
		if (!unit.IsValid) return;

		_abilities.SetUnitAbilityState(unit.Value, abilityId, disabled, hidden);
	}

	/// <summary>Sets what one ability costs one unit, which the authority charges when that unit casts it.</summary>
	public void SetAbilityManaCost(UnitId unit, string abilityId, float manaCost)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return;

		_abilities.SetUnitAbilityManaCost(unit.IsValid ? unit.Value : 0, abilityId, Math.Max(0f, manaCost));
	}

	/// <summary>
	///     What a cast costs a unit, or nothing when the map has said nothing about that unit, so the authored cost
	///     stands.
	/// </summary>
	public float? AbilityManaCostFor(Entity caster, string abilityId)
	{
		if (caster == Entity.Null || !_context.World.IsAlive(caster)) return null;

		return _abilities.UnitAbilityManaCost(caster.Id, abilityId);
	}

	private void WriteAbilityDetail(PresentationKind kind, string abilityId, string text, int count, int secondCount = 0, Action? mutation = null)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return;

		mutation?.Invoke();
		_presentation.Write(kind, AllPlayers, 0, abilityId, text, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, count, secondCount, 0f, 0f, 0f, false);
	}

	private void WriteTooltipNumber(string ownerKind, string ownerId, string key, float value)
	{
		if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(key)) return;

		_abilities.SetTooltipNumber(ownerKind, ownerId, key, value);
		_presentation.Write(PresentationKind.TooltipNumberSet, AllPlayers, 0, ownerKind, ownerId, key, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, value, 0f, 0f, false);
	}

	private void WriteTooltipText(string ownerKind, string ownerId, string key, string value)
	{
		if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(key)) return;

		_abilities.SetTooltipText(ownerKind, ownerId, key, value);
		_presentation.Write(PresentationKind.TooltipTextSet, AllPlayers, 0, ownerKind, ownerId, key, value ?? string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	/// <summary>
	///     Adds an ability to a template's list. The same object is stored under the template's full id and under the id
	///     without its prefix, so the list a script reads back is the list a spawn uses.
	/// </summary>
	private static bool AddAbilityToTemplate(UnitMetadata? template, string abilityId)
	{
		if (template == null) return false;

		string[] existing = template.Abilities ?? Array.Empty<string>();
		for (int index = 0; index < existing.Length; index++)
		{
			if (string.Equals(existing[index], abilityId, StringComparison.OrdinalIgnoreCase)) return true;
		}

		var updated = new string[existing.Length + 1];
		existing.CopyTo(updated, 0);
		updated[existing.Length] = abilityId;
		template.Abilities = updated;
		return true;
	}

}
