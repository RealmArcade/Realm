namespace Realm.Tools.BalanceOptimizer;

public enum BalanceSource
{
	Metadata,
	ElixirTable
}

public class BalanceTuningTarget
{
	public BalanceSource Source { get; init; }
	public string Owner { get; init; } = string.Empty;
	public string Field { get; init; } = string.Empty;
	public double BaseValue { get; init; }
	public double MinimumMultiplier { get; init; }
	public double MaximumMultiplier { get; init; }
	public bool IsInteger { get; init; }
	public bool IsTunable { get; init; }

	public string Describe() => $"{Source}:{Owner}:{Field}";
}
