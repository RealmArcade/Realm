using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Replication;

namespace Realm.ListenServer;

/// <summary>What a map said about one ability when it registered or adjusted it.</summary>
public readonly record struct ScriptAbilityDefinition(
	string DisplayName,
	string TooltipTemplate,
	string IconPath,
	bool IsInstant,
	string Hotkey,
	int GridX,
	int GridY,
	float ManaCost);

/// <summary>Whether one unit's ability is currently shown as unusable, hidden, or both.</summary>
public readonly record struct ScriptAbilityState(bool Disabled, bool Hidden);

/// <summary>
///     The authority's own view of abilities: what a map registered, what it wrote on a card, and how each unit's
///     abilities are currently presented. A script changes all of it through the authoritative process, and the state
///     that has to be seen is published to peers as ability-state deltas rather than being worked out by each client.
/// </summary>
public class ScriptAbilityRegistry
{
	private readonly Dictionary<string, ScriptAbilityDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Dictionary<string, float>> _tooltipNumbers = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Dictionary<string, string>> _tooltipTexts = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<(int UnitId, string AbilityId), ScriptAbilityState> _unitAbilityStates = new();
	private readonly Dictionary<(int UnitId, string AbilityId), float> _unitAbilityManaCosts = new();
	private readonly Dictionary<int, int> _abilityListHandles = new();
	private readonly Dictionary<int, int> _publishedAbilityListHandles = new();
	private readonly List<AbilityStateDelta> _deltas = new();
	private readonly Dictionary<(int UnitId, short AbilityIndex), byte> _currentFlags = new();
	private readonly Dictionary<int, Dictionary<(int UnitId, short AbilityIndex), byte>> _toldByPeer = new();
	private readonly List<(int UnitId, short AbilityIndex)> _clearedKeys = new();
	private bool _abilityListsPublished;

	/// <summary>Records what an ability is called and what its card says, replacing what the map said before.</summary>
	public bool Register(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return false;

		ScriptAbilityDefinition existing = _definitions.TryGetValue(abilityId, out ScriptAbilityDefinition found) ? found : default;
		_definitions[abilityId] = new ScriptAbilityDefinition(
			displayName ?? string.Empty,
			tooltip ?? string.Empty,
			string.IsNullOrEmpty(iconPath) ? existing.IconPath : iconPath,
			isInstant,
			existing.Hotkey,
			existing.GridX,
			existing.GridY,
			existing.ManaCost);

		return true;
	}

	public void SetInstant(string abilityId, bool isInstant) => Mutate(abilityId, definition => definition with { IsInstant = isInstant });

	public void SetIcon(string abilityId, string iconPath) => Mutate(abilityId, definition => definition with { IconPath = iconPath ?? string.Empty });

	public void SetTooltip(string abilityId, string tooltip) => Mutate(abilityId, definition => definition with { TooltipTemplate = tooltip ?? string.Empty });

	public void SetHotkey(string abilityId, string hotkey) => Mutate(abilityId, definition => definition with { Hotkey = hotkey?.Trim() ?? string.Empty });

	public void SetGridPosition(string abilityId, int x, int y) => Mutate(abilityId, definition => definition with { GridX = x, GridY = y });

	/// <summary>What an ability costs a caster unless a map says otherwise for one unit.</summary>
	public void SetManaCost(string abilityId, float manaCost) => Mutate(abilityId, definition => definition with { ManaCost = manaCost });

	/// <summary>The name an ability shows, as the map registered it, falling back to the template's own name.</summary>
	public string GetName(string abilityId, string? templateName)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return string.Empty;
		if (_definitions.TryGetValue(abilityId, out ScriptAbilityDefinition definition) && !string.IsNullOrWhiteSpace(definition.DisplayName)) return definition.DisplayName;
		if (!string.IsNullOrWhiteSpace(templateName)) return templateName;

		return Path.GetFileNameWithoutExtension(abilityId);
	}

	public bool TryGetDefinition(string abilityId, out ScriptAbilityDefinition definition)
	{
		definition = default;
		if (string.IsNullOrWhiteSpace(abilityId)) return false;

		return _definitions.TryGetValue(abilityId, out definition);
	}

	/// <summary>Records a number a registered card reads, keyed by what owns it.</summary>
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

	/// <summary>Records a string a registered card reads, keyed by what owns it.</summary>
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

	/// <summary>How a card names what its values belong to, so the client's own cards look up the same place.</summary>
	public static string OwnerKey(string kind, string id) => $"{kind}:{id}";

	public static string OwnerKey(string kind, int id) => $"{kind}:{id}";

	/// <summary>Publishes how one unit's ability is shown: an order it cannot take, or an option it does not offer.</summary>
	public void SetUnitAbilityState(int unitId, string abilityId, bool disabled, bool hidden)
	{
		if (unitId <= 0 || string.IsNullOrWhiteSpace(abilityId)) return;

		foreach (string candidate in NormalizedAbilityIds(abilityId))
		{
			var key = (unitId, candidate);
			_unitAbilityStates[key] = new ScriptAbilityState(disabled, hidden);
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

	/// <summary>Sets what one ability costs one unit, which is what a map does when it hands a unit a free cast.</summary>
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

	/// <summary>What an ability costs one unit, or nothing when the map has said nothing about that unit.</summary>
	public float? UnitAbilityManaCost(int unitId, string abilityId)
	{
		if (unitId <= 0 || string.IsNullOrWhiteSpace(abilityId)) return null;

		foreach (string candidate in NormalizedAbilityIds(abilityId))
		{
			if (_unitAbilityManaCosts.TryGetValue((unitId, candidate), out float cost)) return cost;
		}

		return null;
	}

	/// <summary>
	///     How every unit's abilities are presented, as one peer has not heard it. A delta names a slot in the unit's
	///     own ability list rather than an ability id, so what one peer has been told is remembered against that slot and
	///     only what changed for that peer travels. A slot only means the same ability while the list is unchanged, so a
	///     list that gained or lost an ability makes every exception be restated against the new one.
	/// </summary>
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

		// An ability that has gone back to being usable normally has to be said so, or the peer keeps showing the
		// order as unavailable for the rest of the match.
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

	/// <summary>Forgets what a peer was told, so the peer that takes its place is described from scratch.</summary>
	public void ForgetPeer(int peerId)
	{
		_toldByPeer.Remove(peerId);
	}

	private Dictionary<(int UnitId, short AbilityIndex), byte> GetTold(int peerId)
	{
		if (_toldByPeer.TryGetValue(peerId, out Dictionary<(int UnitId, short AbilityIndex), byte>? told)) return told;

		told = new Dictionary<(int UnitId, short AbilityIndex), byte>();
		_toldByPeer[peerId] = told;
		return told;
	}

	/// <summary>Every ability a unit is not currently offering normally, keyed by the slot the peer is told about.</summary>
	private void CollectExceptionFlags(EcsWorld world)
	{
		_currentFlags.Clear();
		foreach (var entry in _unitAbilityStates)
		{
			byte flags = (byte)((entry.Value.Disabled ? 1 : 0) | (entry.Value.Hidden ? 2 : 0));
			if (flags == 0) continue;
			if (!TryResolveAbilityIndex(world, entry.Key.UnitId, entry.Key.AbilityId, out short abilityIndex)) continue;

			var key = (entry.Key.UnitId, abilityIndex);
			_currentFlags[key] = (byte)(_currentFlags.TryGetValue(key, out byte existing) ? existing | flags : flags);
		}
	}

	/// <summary>Forgets a unit that is gone, so a recycled entity id does not inherit what its predecessor showed.</summary>
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

	/// <summary>The slot an ability occupies in a unit's own list, which is what a peer is told about.</summary>
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

	/// <summary>
	///     The ids a script's ability name answers to: the name it used and the name without its type prefix, which is
	///     how a map that says "ability/ember_bolt" and one that says "ember_bolt" mean the same ability.
	/// </summary>
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

	private void Mutate(string abilityId, Func<ScriptAbilityDefinition, ScriptAbilityDefinition> mutation)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return;

		ScriptAbilityDefinition definition = _definitions.TryGetValue(abilityId, out ScriptAbilityDefinition existing) ? existing : default;
		_definitions[abilityId] = mutation(definition);
	}
}
