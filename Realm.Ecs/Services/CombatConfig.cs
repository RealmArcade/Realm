namespace Realm.Ecs.Services;

/// <summary>
///     Provides global combat resolution configuration and UGC-editable damage/armor type multiplier matrix.
/// </summary>
public class CombatConfig
{
	/// <summary>
	///     Globally configurable tuning constant K for the rated armor asymptotic curve.
	/// </summary>
	public float TuningConstantK { get; set; } = 16.67f;

	/// <summary>
	///     Minimum damage floor for mitigated attacks.
	/// </summary>
	public float MinDamageFloor { get; set; } = 1.0f;

	private readonly Dictionary<(string DamageType, string ArmorType), float> _matrix = new();

	/// <summary>
	///     Gets the damage multiplier for a given damage type and armor type pair.
	///     Defaults to 1.0 if either key is null/empty or the pair is undefined in the matrix.
	/// </summary>
	public float GetMultiplier(string? damageType, string? armorType)
	{
		if (string.IsNullOrEmpty(damageType) || string.IsNullOrEmpty(armorType))
		{
			return 1.0f;
		}

		var key = (damageType.Trim().ToLowerInvariant(), armorType.Trim().ToLowerInvariant());
		if (_matrix.TryGetValue(key, out float multiplier))
		{
			return multiplier;
		}

		return 1.0f;
	}

	/// <summary>
	///     Sets the damage multiplier for a specific damage type and armor type pair.
	/// </summary>
	public void SetMultiplier(string damageType, string armorType, float multiplier)
	{
		var key = (damageType.Trim().ToLowerInvariant(), armorType.Trim().ToLowerInvariant());
		_matrix[key] = multiplier;
	}

	/// <summary>
	///     Clears all entries in the damage matrix.
	/// </summary>
	public void ClearMatrix()
	{
		_matrix.Clear();
	}
}
