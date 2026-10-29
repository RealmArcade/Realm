using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.MapAPI;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	private readonly ScriptAbilityRegistry _abilities;

	private const string AbilityOwnerKind = "ability";
	private const string ItemOwnerKind = "item";
	private const string UnitOwnerKind = "unit";
	private const string UpgradeOwnerKind = "upgrade";

	public ScriptAbilityRegistry Abilities => _abilities;

	public void AddUnitTypeAbility(string unitTypeId, string abilityId)
	{
		if (string.IsNullOrWhiteSpace(unitTypeId) || string.IsNullOrWhiteSpace(abilityId)) return;

		bool added = AddAbilityToTemplate(_map.Catalog.FindUnit(unitTypeId), abilityId);
		if (!added) added = AddAbilityToTemplate(_map.Catalog.FindBuilding(unitTypeId), abilityId);
		if (!added) return;

		PublishTemplateAbility(abilityId);
	}

	private void PublishTemplateAbility(string abilityId)
	{
		if (!_declaredTemplateAbilities.Add(abilityId)) return;

		AbilityMetadata? ability = _map.Catalog.FindAbility(abilityId);
		UnitMetadata? unit = ability == null ? _map.Catalog.FindUnitOrBuilding(abilityId) : null;

		string displayName = ability?.Name ?? unit?.Name ?? GetUnitName(abilityId);
		string tooltip = ability?.Description ?? unit?.Description ?? string.Empty;
		string iconPath = ability?.IconPath ?? string.Empty;

		_presentation.Write(PresentationKind.AbilityRegistered, AllPlayers, 0, abilityId, displayName, tooltip, iconPath, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	private readonly HashSet<string> _declaredTemplateAbilities = new(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyList<string> GetUnitTypeAbilities(string unitTypeId)
	{
		if (string.IsNullOrWhiteSpace(unitTypeId)) return Array.Empty<string>();

		UnitMetadata? template = _map.Catalog.FindUnitOrBuilding(unitTypeId);
		return template?.Abilities ?? Array.Empty<string>();
	}

	public string GetUnitName(string unitTypeId)
	{
		if (string.IsNullOrWhiteSpace(unitTypeId)) return string.Empty;

		UnitMetadata? template = _map.Catalog.FindUnitOrBuilding(unitTypeId);
		if (template != null && !string.IsNullOrWhiteSpace(template.Name)) return template.Name;

		return Path.GetFileNameWithoutExtension(unitTypeId);
	}

	public string GetAbilityName(string abilityId)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return string.Empty;

		return _abilities.GetName(abilityId, _map.Catalog.FindAbility(abilityId)?.Name);
	}

	public string GetStringText(int stringId) => stringId <= 0 ? string.Empty : StringRegistry.Get(stringId);

	public void RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath = "", bool isInstant = false)
	{
		if (!_abilities.Register(abilityId, displayName, tooltip, iconPath, isInstant)) return;

		_presentation.Write(PresentationKind.AbilityRegistered, AllPlayers, 0, abilityId, displayName ?? string.Empty, tooltip ?? string.Empty, iconPath ?? string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, isInstant ? 1 : 0, 0, 0f, 0f, 0f, isInstant);
	}

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

	public void SetAbilityState(UnitId unit, string abilityId, bool disabled, bool hidden)
	{
		if (!unit.IsValid) return;

		_abilities.SetUnitAbilityState(unit.Value, abilityId, disabled, hidden);
	}

	public void SetAbilityManaCost(UnitId unit, string abilityId, float manaCost)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return;

		_abilities.SetUnitAbilityManaCost(unit.IsValid ? unit.Value : 0, abilityId, Math.Max(0f, manaCost));
	}

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
