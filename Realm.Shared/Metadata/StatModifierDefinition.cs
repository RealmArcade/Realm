namespace Realm.Shared.Metadata;

public class StatModifierDefinition
{
	public string StatKey { get; set; } = string.Empty;
	public string Type { get; set; } = "Flat";
	public float Value { get; set; }
	public string Formula { get; set; } = string.Empty;
	public float PerLevelScaling { get; set; }
}
