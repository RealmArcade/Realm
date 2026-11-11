using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Replication;
using Realm.Shared.Abilities;
using Realm.Shared.Diagnostics;
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
	private readonly Dictionary<int, Dictionary<(int UnitId, short AbilityIndex), int>> _restatementsByPeer = new();
	private readonly Dictionary<int, Dictionary<(int UnitId, string AbilityId), int>> _namedRestatementsByPeer = new();

	/// <summary>
	///     How many descriptions a peer is told the same exception for. A component that changes is restated while it keeps
	///     differing, but an exception the map declared once has nothing left to say after the description that carried it —
	///     and that one description is not a delivery. It can be lost, or arrive for a unit this screen is still building, and
	///     the match had already recorded it as told, so the grid offered every ability the map withheld for the rest of the
	///     match. Stating it a few times is what makes a lost one cost a description rather than the match, which is the same
	///     allowance a component removal is given.
	/// </summary>
	private const int RestatedDescriptionCount = 4;
	private readonly List<(int UnitId, short AbilityIndex)> _clearedKeys = new();
	private readonly List<int> _changedAbilityListUnitIds = new();
	private readonly HashSet<int> _unitsOwedAbilityRestatement = new();
	private bool _abilityListsPublished;
	private bool _statesChanged = true;

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
		definition.CostGold = metadata.CostGold;
		definition.CostWood = metadata.CostWood;
		definition.CostStone = metadata.CostStone;
		definition.Cooldown = metadata.Cooldown;
		definition.TargetRange = metadata.TargetRange;
		definition.AreaOfEffectRadius = metadata.AreaOfEffectRadius;
		definition.VisualEffect = metadata.VisualEffect;
		definition.ProjectileId = metadata.ProjectileId;
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

	/// <summary>
	///     Records what a map declared about one unit's ability. A map restates the same declaration every time it
	///     re-applies a tile or recomputes an offer, which is many times a tick, so a call that repeats the state already
	///     recorded is dropped before it normalizes the identifier: normalizing allocates a string per spelling, and the
	///     table it writes into is walked once a tick for as long as the match runs.
	/// </summary>
	public void SetUnitAbilityState(int unitId, string abilityId, bool disabled, bool hidden)
	{
		if (unitId <= 0 || string.IsNullOrWhiteSpace(abilityId)) return;

		if (_unitAbilityStates.TryGetValue((unitId, abilityId), out ScriptAbilityState recorded)
		    && recorded.Disabled == disabled
		    && recorded.Hidden == hidden
		    && string.Equals(recorded.DeclaredId, abilityId, StringComparison.Ordinal))
		{
			return;
		}

		_statesChanged = true;
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

	/// <summary>
	///     Collects what every peer's deltas are computed from, once per tick rather than once per peer. The ability list a
	///     unit publishes and the disabled/hidden flags it carries are the same for every screen, and rebuilding them for
	///     each of twelve peers cost a full room five milliseconds of every thirty-three when they were collected that way.
	/// </summary>
	public void PrepareTick(EcsWorld world)
	{
		long handlesStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		CollectAbilityListHandles(world);

		bool abilityListsChanged = CollectChangedAbilityListUnits();
		_abilityListsPublished = true;

		// A list that changed renumbers every slot the exceptions were addressed by, so what every peer was told about
		// those slots stops meaning what it said: the units whose list moved forget what they were told by slot, and are
		// stated again in full. What every peer was told about those units by name is kept, because a name is not a slot
		// and a list moving cannot change what it means - and it is the only record of which exceptions a screen holds, so
		// dropping it lost the change that makes an ability visible again, since a state the map stopped withholding is
		// stated by the delta that clears it and by nothing else.
		if (abilityListsChanged)
		{
			for (int index = 0; index < _changedAbilityListUnitIds.Count; index++)
			{
				int changedUnitId = _changedAbilityListUnitIds[index];
				ForgetToldAbilityStateSlotsForUnit(changedUnitId);
				_unitsOwedAbilityRestatement.Add(changedUnitId);
			}
		}

		_publishedAbilityListHandles.Clear();
		foreach (var entry in _abilityListHandles) _publishedAbilityListHandles[entry.Key] = entry.Value;
		if (handlesStarted != 0L) LagProbe.Add(LagStage.AuthorityPrepareAbilityListHandlesMilliseconds, LagProbe.ElapsedMilliseconds(handlesStarted));

		// What a peer is told about an ability is a function of what the map declared and of the ability list it resolves
		// against, and neither of those moves on most ticks: the table is rebuilt only when one of them did. Rebuilding it
		// every tick was the largest single cost a full room paid, five milliseconds of every thirty-three.
		if (!_statesChanged && !abilityListsChanged) return;

		long flagsStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		CollectExceptionFlags(world);
		_statesChanged = false;
		if (flagsStarted != 0L) LagProbe.Add(LagStage.AuthorityPrepareAbilityFlagsMilliseconds, LagProbe.ElapsedMilliseconds(flagsStarted));
	}

	public List<AbilityStateDelta> CollectDeltas(int peerId, EcsWorld world, bool includeAll)
	{
		PrepareTick(world);
		return CollectDeltas(peerId, world, includeAll, _deltas);
	}

	/// <summary>
	///     Fills the caller's list with the ability deltas one peer is owed. The list is the caller's because every peer in
	///     a room is collected in the same tick before any lane reads them: one shared list left every peer holding the last
	///     peer's deltas, so a screen was told about another screen's units and never told about its own.
	/// </summary>
	public List<AbilityStateDelta> CollectDeltas(int peerId, EcsWorld world, bool includeAll, List<AbilityStateDelta> deltas)
	{
		deltas.Clear();

		Dictionary<(int UnitId, short AbilityIndex), byte> told = GetTold(peerId);
		Dictionary<(int UnitId, short AbilityIndex), int> restatements = GetRestatements(peerId);
		bool restateEverything = includeAll;

		foreach (var entry in _currentFlags)
		{
			bool restatedForUnit = !restateEverything && _unitsOwedAbilityRestatement.Contains(entry.Key.UnitId);
			bool alreadyStatedOnce = told.TryGetValue(entry.Key, out byte toldFlags) && toldFlags == entry.Value;
			if (!restateEverything && !restatedForUnit && alreadyStatedOnce && !IsOwedAnotherStatement(restatements, entry.Key)) continue;

			deltas.Add(new AbilityStateDelta
			{
				ServerEntityId = entry.Key.UnitId,
				AbilityIndex = entry.Key.AbilityIndex,
				Flags = entry.Value
			});

			told[entry.Key] = entry.Value;

			// Only a statement the peer does not already hold, or one a restatement asked for, arms the repeat count. An
			// emission that is spending the count leaves it to be spent: refilling it there stated the same exception in
			// every delta for as long as it existed, since the count it reset was never allowed to run out.
			if (restateEverything || restatedForUnit || !alreadyStatedOnce) restatements[entry.Key] = RestatedDescriptionCount;
		}

		_clearedKeys.Clear();
		foreach (var entry in told)
		{
			if (_currentFlags.ContainsKey(entry.Key)) continue;

			_clearedKeys.Add(entry.Key);
			deltas.Add(new AbilityStateDelta
			{
				ServerEntityId = entry.Key.UnitId,
				AbilityIndex = entry.Key.AbilityIndex,
				Flags = 0
			});
		}

		for (int index = 0; index < _clearedKeys.Count; index++) told.Remove(_clearedKeys[index]);

		CollectNamedDeltas(peerId, includeAll, deltas);

		SpendRestatements(peerId);
		SpendNamedRestatements(peerId);

		deltas.Sort(static (left, right) =>
		{
			int byEntity = left.ServerEntityId.CompareTo(right.ServerEntityId);
			return byEntity != 0 ? byEntity : left.AbilityIndex.CompareTo(right.AbilityIndex);
		});

		return deltas;
	}

	private void CollectNamedDeltas(int peerId, bool restateEverything, List<AbilityStateDelta> deltas)
	{
		if (!_toldIdFlagsByPeer.TryGetValue(peerId, out Dictionary<(int UnitId, string AbilityId), byte>? told))
		{
			told = new Dictionary<(int UnitId, string AbilityId), byte>();
			_toldIdFlagsByPeer[peerId] = told;
		}

		Dictionary<(int UnitId, string AbilityId), int> restatements = GetNamedRestatements(peerId);

		foreach (var entry in _currentIdFlags)
		{
			bool alreadyStatedOnce = told.TryGetValue(entry.Key, out byte toldFlags) && toldFlags == entry.Value;
			if (!restateEverything && alreadyStatedOnce && !IsOwedAnotherStatement(restatements, entry.Key)) continue;

			deltas.Add(new AbilityStateDelta
			{
				ServerEntityId = entry.Key.UnitId,
				AbilityIndex = -1,
				Flags = entry.Value,
				AbilityId = entry.Key.AbilityId
			});

			told[entry.Key] = entry.Value;
			if (restateEverything || !alreadyStatedOnce) restatements[entry.Key] = RestatedDescriptionCount;
		}

		_clearedNamedKeys.Clear();
		foreach (var entry in told)
		{
			if (_currentIdFlags.ContainsKey(entry.Key)) continue;

			_clearedNamedKeys.Add(entry.Key);
			deltas.Add(new AbilityStateDelta
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

	private Dictionary<(int UnitId, short AbilityIndex), int> GetRestatements(int peerId)
	{
		if (_restatementsByPeer.TryGetValue(peerId, out Dictionary<(int UnitId, short AbilityIndex), int>? restatements)) return restatements;

		restatements = new Dictionary<(int UnitId, short AbilityIndex), int>();
		_restatementsByPeer[peerId] = restatements;
		return restatements;
	}

	private Dictionary<(int UnitId, string AbilityId), int> GetNamedRestatements(int peerId)
	{
		if (_namedRestatementsByPeer.TryGetValue(peerId, out Dictionary<(int UnitId, string AbilityId), int>? restatements)) return restatements;

		restatements = new Dictionary<(int UnitId, string AbilityId), int>();
		_namedRestatementsByPeer[peerId] = restatements;
		return restatements;
	}

	private static bool IsOwedAnotherStatement<TKey>(Dictionary<TKey, int> restatements, TKey key) where TKey : notnull
	{
		return restatements.TryGetValue(key, out int remainingStatements) && remainingStatements > 0;
	}

	private void SpendRestatements(int peerId)
	{
		Dictionary<(int UnitId, short AbilityIndex), int> restatements = GetRestatements(peerId);
		if (restatements.Count == 0) return;

		_spentRestatementKeys.Clear();
		foreach (var entry in restatements)
		{
			if (entry.Value <= 1)
			{
				_spentRestatementKeys.Add(entry.Key);
				continue;
			}

			restatements[entry.Key] = entry.Value - 1;
		}

		for (int index = 0; index < _spentRestatementKeys.Count; index++) restatements.Remove(_spentRestatementKeys[index]);
		_spentRestatementKeys.Clear();
	}

	private void SpendNamedRestatements(int peerId)
	{
		Dictionary<(int UnitId, string AbilityId), int> restatements = GetNamedRestatements(peerId);
		if (restatements.Count == 0) return;

		_spentNamedRestatementKeys.Clear();
		foreach (var entry in restatements)
		{
			if (entry.Value <= 1)
			{
				_spentNamedRestatementKeys.Add(entry.Key);
				continue;
			}

			restatements[entry.Key] = entry.Value - 1;
		}

		for (int index = 0; index < _spentNamedRestatementKeys.Count; index++) restatements.Remove(_spentNamedRestatementKeys[index]);
		_spentNamedRestatementKeys.Clear();
	}

	private readonly List<(int UnitId, short AbilityIndex)> _spentRestatementKeys = new();
	private readonly List<(int UnitId, string AbilityId)> _spentNamedRestatementKeys = new();

	public void ForgetPeer(int peerId)
	{
		_toldByPeer.Remove(peerId);
		_toldIdFlagsByPeer.Remove(peerId);
		_restatementsByPeer.Remove(peerId);
		_namedRestatementsByPeer.Remove(peerId);
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
		_statesChanged = true;

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

		ForgetToldAbilityStateForUnit(unitId);
	}

	/// <summary>
	///     Drops what every peer is remembered as having been told about one unit's exceptions, by slot and by name, so the
	///     next description states them again. Nobody is left to hold them: the unit that held them is gone, and the
	///     identifier it held now belongs to another unit.
	/// </summary>
	private void ForgetToldAbilityStateForUnit(int unitId)
	{
		ForgetToldAbilityStateSlotsForUnit(unitId);

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

	/// <summary>
	///     Drops what every peer is remembered as having been told about one unit's slot-addressed exceptions. A slot only
	///     names an ability while the unit's list is the list it was numbered against, so a list that moved makes those
	///     records meaningless, while the records addressed by name - which no renumbering can touch - are the only
	///     statement of what a screen holds for an ability the unit does not list.
	/// </summary>
	private void ForgetToldAbilityStateSlotsForUnit(int unitId)
	{
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

	/// <summary>
	///     Names the units whose ability list is no longer the one this process described, and answers whether there are
	///     any. The list is the key its exceptions were addressed against, so a list that moved renumbers every slot they
	///     named: these are the units whose recorded state stopped meaning what it said.
	/// </summary>
	private bool CollectChangedAbilityListUnits()
	{
		_changedAbilityListUnitIds.Clear();
		if (!_abilityListsPublished) return false;

		foreach (var entry in _abilityListHandles)
		{
			if (!_publishedAbilityListHandles.TryGetValue(entry.Key, out int publishedHandle)) _changedAbilityListUnitIds.Add(entry.Key);
			else if (publishedHandle != entry.Value) _changedAbilityListUnitIds.Add(entry.Key);
		}

		foreach (var entry in _publishedAbilityListHandles)
		{
			if (!_abilityListHandles.ContainsKey(entry.Key)) _changedAbilityListUnitIds.Add(entry.Key);
		}

		return _changedAbilityListUnitIds.Count > 0;
	}

	/// <summary>
	///     Ends a tick's collection. Every seated peer has been handed the state it was owed, so a restatement owed for a
	///     list that moved is not asked for again. A peer the tick told nothing is not collected at all, but it is owed a
	///     description built from scratch instead, and that states every exception its units hold.
	/// </summary>
	public void CompleteTick() => _unitsOwedAbilityRestatement.Clear();

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
