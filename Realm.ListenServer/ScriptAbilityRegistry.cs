using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Replication;
using Realm.Shared.Abilities;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;

namespace Realm.ListenServer;

public readonly record struct ScriptAbilityState(bool Disabled, bool Hidden, string DeclaredId);

public class ScriptAbilityRegistry
{
	private readonly DefinitionCatalog _catalog;
	private readonly Dictionary<string, AbilityDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Dictionary<string, float>> _tooltipNumbers = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Dictionary<string, string>> _tooltipTexts = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<(int UnitId, string AbilityId), ScriptAbilityState> _unitAbilityStates = new();
	private readonly Dictionary<(int UnitId, string AbilityId), float> _unitAbilityManaCosts = new();
	private readonly Dictionary<int, int> _abilityListHandles = new();
	private readonly Dictionary<int, int> _publishedAbilityListHandles = new();
	private readonly List<AbilityStateDelta> _deltas = new();
	private readonly Dictionary<(int UnitId, short AbilityIndex), byte> _currentFlags = new();
	private readonly Dictionary<(int UnitId, string AbilityId), byte> _currentIdFlags = new();
	private readonly Dictionary<int, Dictionary<(int UnitId, short AbilityIndex), byte>> _toldByPeer = new();
	private readonly Dictionary<int, Dictionary<(int UnitId, string AbilityId), byte>> _toldIdFlagsByPeer = new();
	private readonly List<(int UnitId, short AbilityIndex)> _clearedKeys = new();
	private bool _abilityListsPublished;

	public ScriptAbilityRegistry(DefinitionCatalog catalog)
	{
		_catalog = catalog;
	}

	public AbilityDefinition GetDefinition(string abilityId)
	{
		string key = NormalizeKey(abilityId);
		if (_definitions.TryGetValue(key, out AbilityDefinition? definition)) return definition;

		definition = BuildFromCatalog(abilityId);
		_definitions[key] = definition;
		return definition;
	}

	public bool Knows(string abilityId) => !string.IsNullOrWhiteSpace(abilityId);

	public bool Register(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return false;

		AbilityDefinition definition = GetDefinition(abilityId);
		definition.DisplayName = displayName ?? string.Empty;
		definition.TooltipTemplate = tooltip ?? string.Empty;
		if (!string.IsNullOrEmpty(iconPath)) definition.IconPath = iconPath;
		definition.IsInstant = isInstant;

		return true;
	}

	public void SetInstant(string abilityId, bool isInstant) => GetDefinition(abilityId).IsInstant = isInstant;

	public void SetIcon(string abilityId, string iconPath) => GetDefinition(abilityId).IconPath = iconPath ?? string.Empty;

	public void SetTooltip(string abilityId, string tooltip) => GetDefinition(abilityId).TooltipTemplate = tooltip ?? string.Empty;

	public void SetHotkey(string abilityId, string hotkey) => GetDefinition(abilityId).Hotkey = hotkey?.Trim() ?? string.Empty;

	public void SetGridPosition(string abilityId, int x, int y)
	{
		AbilityDefinition definition = GetDefinition(abilityId);
		definition.GridX = x;
		definition.GridY = y;
	}

	public void SetManaCost(string abilityId, float manaCost) => GetDefinition(abilityId).ManaCost = manaCost;

	public string GetName(string abilityId, string? templateName)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return string.Empty;

		AbilityDefinition definition = GetDefinition(abilityId);
		if (!string.IsNullOrWhiteSpace(definition.DisplayName)) return definition.DisplayName;
		if (!string.IsNullOrWhiteSpace(templateName)) return templateName;

		return Path.GetFileNameWithoutExtension(abilityId);
	}

	private AbilityDefinition BuildFromCatalog(string abilityId)
	{
		var definition = new AbilityDefinition { Id = abilityId };
		AbilityMetadata? metadata = _catalog.FindAbility(abilityId);
		if (metadata == null) return definition;

		string description = metadata.Description ?? string.Empty;
		definition.DisplayName = metadata.Name ?? string.Empty;
		definition.TooltipTemplate = string.IsNullOrWhiteSpace(metadata.Role) ? description : $"[{metadata.Role.Trim()}] {description}";
		definition.IconPath = metadata.IconPath ?? string.Empty;
		definition.IsInstant = string.Equals(metadata.AbilityType, "instant_spell", StringComparison.OrdinalIgnoreCase);
		definition.AutoCastEnabled = metadata.AutoCast?.Enabled ?? true;
		definition.AutoCastRequiresEnemy = metadata.AutoCast?.RequiresEnemyTarget ?? true;
		definition.AutoCastAllyHealthPercentBelow = metadata.AutoCast?.AllyHealthPercentBelow ?? 1f;
		definition.AutoCastManaReserve = metadata.AutoCast?.ManaReserve ?? 0f;
		definition.AutoCastMinManaPercent = metadata.AutoCast?.MinManaPercent ?? 0f;
		definition.ManaCost = metadata.ManaCost;
		definition.Cooldown = metadata.Cooldown;
		definition.TargetRange = metadata.TargetRange;
		definition.AreaOfEffectRadius = metadata.AreaOfEffectRadius;
		definition.VisualEffect = metadata.VisualEffect;
		definition.CastSound = metadata.CastSound;
		definition.GridX = metadata.GridX;
		definition.GridY = metadata.GridY;
		definition.Hotkey = metadata.Hotkey ?? string.Empty;

		definition.Effects = AbilityEffectFactory.Create(metadata, metadata.AreaOfEffectRadius, out float maxDamage, out float maxHealing);
		definition.Damage = maxDamage;
		definition.Healing = maxHealing;
		return definition;
	}

	private static string NormalizeKey(string abilityId)
	{
		ReadOnlySpan<char> span = abilityId.AsSpan().Trim();
		string[] prefixes = ["ability/", "unit/", "building/"];
		for (int index = 0; index < prefixes.Length; index++)
		{
			if (!span.StartsWith(prefixes[index], StringComparison.OrdinalIgnoreCase)) continue;

			return span[prefixes[index].Length..].ToString();
		}

		return span.ToString();
	}

	public void SetTooltipNumber(string ownerKind, string ownerId, string key, float value)
	{
		if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(key)) return;

		string owner = OwnerKey(ownerKind, ownerId);
		if (!_tooltipNumbers.TryGetValue(owner, out Dictionary<string, float>? numbers))
		{
			numbers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
			_tooltipNumbers[owner] = numbers;
		}

		numbers[key] = value;
	}

	public void SetTooltipText(string ownerKind, string ownerId, string key, string value)
	{
		if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(key)) return;

		string owner = OwnerKey(ownerKind, ownerId);
		if (!_tooltipTexts.TryGetValue(owner, out Dictionary<string, string>? texts))
		{
			texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			_tooltipTexts[owner] = texts;
		}

		texts[key] = value ?? string.Empty;
	}

	public bool TryGetTooltipNumber(string ownerKind, string ownerId, string key, out float value)
	{
		value = 0f;
		if (!_tooltipNumbers.TryGetValue(OwnerKey(ownerKind, ownerId), out Dictionary<string, float>? numbers)) return false;

		return numbers.TryGetValue(key, out value);
	}

	public bool TryGetTooltipText(string ownerKind, string ownerId, string key, out string value)
	{
		value = string.Empty;
		if (!_tooltipTexts.TryGetValue(OwnerKey(ownerKind, ownerId), out Dictionary<string, string>? texts)) return false;

		return texts.TryGetValue(key, out value!);
	}

	public static string OwnerKey(string kind, string id) => $"{kind}:{id}";

	public static string OwnerKey(string kind, int id) => $"{kind}:{id}";

	public void SetUnitAbilityState(int unitId, string abilityId, bool disabled, bool hidden)
	{
		if (unitId <= 0 || string.IsNullOrWhiteSpace(abilityId)) return;

		foreach (string candidate in NormalizedAbilityIds(abilityId))
		{
			var key = (unitId, candidate);
			_unitAbilityStates[key] = new ScriptAbilityState(disabled, hidden, abilityId);
		}
	}

	public bool IsUnitAbilityDisabled(int unitId, string abilityId)
	{
		return TryGetUnitAbilityState(unitId, abilityId, out ScriptAbilityState state) && state.Disabled;
	}

	public bool IsUnitAbilityHidden(int unitId, string abilityId)
	{
		return TryGetUnitAbilityState(unitId, abilityId, out ScriptAbilityState state) && state.Hidden;
	}

	public bool TryGetUnitAbilityState(int unitId, string abilityId, out ScriptAbilityState state)
	{
		state = default;
		if (unitId <= 0 || string.IsNullOrWhiteSpace(abilityId)) return false;

		foreach (string candidate in NormalizedAbilityIds(abilityId))
		{
			if (!_unitAbilityStates.TryGetValue((unitId, candidate), out state)) continue;

			return true;
		}

		return false;
	}

	public void SetUnitAbilityManaCost(int unitId, string abilityId, float manaCost)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return;

		SetManaCost(abilityId, manaCost);
		if (unitId <= 0) return;

		foreach (string candidate in NormalizedAbilityIds(abilityId))
		{
			_unitAbilityManaCosts[(unitId, candidate)] = manaCost;
		}
	}

	public float? UnitAbilityManaCost(int unitId, string abilityId)
	{
		if (unitId <= 0 || string.IsNullOrWhiteSpace(abilityId)) return null;

		foreach (string candidate in NormalizedAbilityIds(abilityId))
		{
			if (_unitAbilityManaCosts.TryGetValue((unitId, candidate), out float cost)) return cost;
		}

		return null;
	}

	public List<AbilityStateDelta> CollectDeltas(int peerId, EcsWorld world, bool includeAll)
	{
		_deltas.Clear();
		CollectAbilityListHandles(world);

		bool abilityListsChanged = _abilityListsPublished && HasAnyAbilityListChanged();
		_abilityListsPublished = true;
		if (abilityListsChanged) _toldByPeer.Clear();

		Dictionary<(int UnitId, short AbilityIndex), byte> told = GetTold(peerId);
		CollectExceptionFlags(world);

		foreach (var entry in _currentFlags)
		{
			if (!includeAll && told.TryGetValue(entry.Key, out byte toldFlags) && toldFlags == entry.Value) continue;

			_deltas.Add(new AbilityStateDelta
			{
				ServerEntityId = entry.Key.UnitId,
				AbilityIndex = entry.Key.AbilityIndex,
				Flags = entry.Value
			});

			told[entry.Key] = entry.Value;
		}

		_clearedKeys.Clear();
		foreach (var entry in told)
		{
			if (_currentFlags.ContainsKey(entry.Key)) continue;

			_clearedKeys.Add(entry.Key);
			_deltas.Add(new AbilityStateDelta
			{
				ServerEntityId = entry.Key.UnitId,
				AbilityIndex = entry.Key.AbilityIndex,
				Flags = 0
			});
		}

		for (int index = 0; index < _clearedKeys.Count; index++) told.Remove(_clearedKeys[index]);

		CollectNamedDeltas(peerId, includeAll);

		_publishedAbilityListHandles.Clear();
		foreach (var entry in _abilityListHandles)
		{
			_publishedAbilityListHandles[entry.Key] = entry.Value;
		}

		_deltas.Sort(static (left, right) =>
		{
			int byEntity = left.ServerEntityId.CompareTo(right.ServerEntityId);
			return byEntity != 0 ? byEntity : left.AbilityIndex.CompareTo(right.AbilityIndex);
		});

		return _deltas;
	}

	private void CollectNamedDeltas(int peerId, bool includeAll)
	{
		if (!_toldIdFlagsByPeer.TryGetValue(peerId, out Dictionary<(int UnitId, string AbilityId), byte>? told))
		{
			told = new Dictionary<(int UnitId, string AbilityId), byte>();
			_toldIdFlagsByPeer[peerId] = told;
		}

		foreach (var entry in _currentIdFlags)
		{
			if (!includeAll && told.TryGetValue(entry.Key, out byte toldFlags) && toldFlags == entry.Value) continue;

			_deltas.Add(new AbilityStateDelta
			{
				ServerEntityId = entry.Key.UnitId,
				AbilityIndex = -1,
				Flags = entry.Value,
				AbilityId = entry.Key.AbilityId
			});

			told[entry.Key] = entry.Value;
		}

		_clearedNamedKeys.Clear();
		foreach (var entry in told)
		{
			if (_currentIdFlags.ContainsKey(entry.Key)) continue;

			_clearedNamedKeys.Add(entry.Key);
			_deltas.Add(new AbilityStateDelta
			{
				ServerEntityId = entry.Key.UnitId,
				AbilityIndex = -1,
				Flags = 0,
				AbilityId = entry.Key.AbilityId
			});
		}

		for (int index = 0; index < _clearedNamedKeys.Count; index++) told.Remove(_clearedNamedKeys[index]);
	}

	private readonly List<(int UnitId, string AbilityId)> _clearedNamedKeys = new();

	public void ForgetPeer(int peerId)
	{
		_toldByPeer.Remove(peerId);
		_toldIdFlagsByPeer.Remove(peerId);
	}

	private Dictionary<(int UnitId, short AbilityIndex), byte> GetTold(int peerId)
	{
		if (_toldByPeer.TryGetValue(peerId, out Dictionary<(int UnitId, short AbilityIndex), byte>? told)) return told;

		told = new Dictionary<(int UnitId, short AbilityIndex), byte>();
		_toldByPeer[peerId] = told;
		return told;
	}

	private void CollectExceptionFlags(EcsWorld world)
	{
		_currentFlags.Clear();
		_currentIdFlags.Clear();

		foreach (var entry in _unitAbilityStates)
		{
			byte flags = (byte)((entry.Value.Disabled ? 1 : 0) | (entry.Value.Hidden ? 2 : 0));
			if (flags == 0) continue;

			if (!TryResolveAbilityIndex(world, entry.Key.UnitId, entry.Key.AbilityId, out short abilityIndex))
			{
				var namedKey = (entry.Key.UnitId, entry.Value.DeclaredId);
				_currentIdFlags[namedKey] = (byte)(_currentIdFlags.TryGetValue(namedKey, out byte namedExisting) ? namedExisting | flags : flags);
				continue;
			}

			var key = (entry.Key.UnitId, abilityIndex);
			_currentFlags[key] = (byte)(_currentFlags.TryGetValue(key, out byte existing) ? existing | flags : flags);
		}
	}

	public void ForgetUnit(int unitId)
	{
		_abilityListHandles.Remove(unitId);
		_publishedAbilityListHandles.Remove(unitId);

		_forgottenKeys.Clear();
		foreach (var key in _unitAbilityStates.Keys)
		{
			if (key.UnitId == unitId) _forgottenKeys.Add(key);
		}

		for (int index = 0; index < _forgottenKeys.Count; index++)
		{
			_unitAbilityStates.Remove(_forgottenKeys[index]);
		}

		_forgottenKeys.Clear();
		foreach (var key in _unitAbilityManaCosts.Keys)
		{
			if (key.UnitId == unitId) _forgottenKeys.Add(key);
		}

		for (int index = 0; index < _forgottenKeys.Count; index++) _unitAbilityManaCosts.Remove(_forgottenKeys[index]);

		_forgottenKeys.Clear();

		foreach (var told in _toldByPeer.Values)
		{
			_toldKeys.Clear();
			foreach (var entry in told)
			{
				if (entry.Key.UnitId == unitId) _toldKeys.Add(entry.Key);
			}

			for (int index = 0; index < _toldKeys.Count; index++) told.Remove(_toldKeys[index]);
		}

		foreach (var told in _toldIdFlagsByPeer.Values)
		{
			_clearedNamedKeys.Clear();
			foreach (var entry in told)
			{
				if (entry.Key.UnitId == unitId) _clearedNamedKeys.Add(entry.Key);
			}

			for (int index = 0; index < _clearedNamedKeys.Count; index++) told.Remove(_clearedNamedKeys[index]);
		}
	}

	private readonly List<(int UnitId, string AbilityId)> _forgottenKeys = new();
	private readonly List<(int UnitId, short AbilityIndex)> _toldKeys = new();

	private void CollectAbilityListHandles(EcsWorld world)
	{
		_abilityListHandles.Clear();
		foreach (Entity entity in world.Query<AbilityState>())
		{
			ref AbilityState abilityState = ref world.Get<AbilityState>(entity);
			_abilityListHandles[entity.Id] = abilityState.Handle;
		}
	}

	private bool HasAnyAbilityListChanged()
	{
		if (_abilityListHandles.Count != _publishedAbilityListHandles.Count) return true;

		foreach (var entry in _abilityListHandles)
		{
			if (!_publishedAbilityListHandles.TryGetValue(entry.Key, out int publishedHandle)) return true;
			if (publishedHandle != entry.Value) return true;
		}

		return false;
	}

	private static bool TryResolveAbilityIndex(EcsWorld world, int unitId, string abilityId, out short abilityIndex)
	{
		abilityIndex = -1;

		var entity = new Entity(unitId);
		if (!world.IsAlive(entity) || !world.Has<AbilityState>(entity)) return false;

		AbilityState abilityState = world.Get<AbilityState>(entity);
		for (int index = 0; index < abilityState.Count; index++)
		{
			string? listed = abilityState.Get(index);
			if (listed == null) continue;
			if (!IsSameAbility(listed, abilityId)) continue;

			abilityIndex = (short)index;
			return true;
		}

		return false;
	}

	private static bool IsSameAbility(string left, string right) => AbilityCore(left).Equals(AbilityCore(right), StringComparison.OrdinalIgnoreCase);

	private static ReadOnlySpan<char> AbilityCore(string abilityId)
	{
		ReadOnlySpan<char> span = abilityId.AsSpan();
		return span.StartsWith("ability/", StringComparison.OrdinalIgnoreCase) ? span[8..] : span;
	}

	private static IEnumerable<string> NormalizedAbilityIds(string abilityId)
	{
		yield return abilityId;

		string trimmed = abilityId;
		string[] prefixes = ["ability/", "unit/", "building/"];
		for (int index = 0; index < prefixes.Length; index++)
		{
			if (!trimmed.StartsWith(prefixes[index], StringComparison.OrdinalIgnoreCase)) continue;

			yield return trimmed[prefixes[index].Length..];
			yield break;
		}

		yield return $"ability/{trimmed}";
	}

}
