namespace Realm.Client.Core;

public class AbilityDefinition
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string Tooltip { get; set; } = "";
	public string IconPath { get; set; } = "";
	public bool IsInstant { get; set; }
	public string Hotkey { get; set; } = "";
	public int GridX { get; set; } = -1;
	public int GridY { get; set; } = -1;
	public float ManaCost { get; set; } = 0f;
	public float Cooldown { get; set; } = 0f;
	public float TargetRange { get; set; } = 0f;
	public float AreaOfEffectRadius { get; set; } = 0f;
	public float Damage { get; set; } = 0f;
	public float Healing { get; set; } = 0f;
	public string? VisualEffect { get; set; }
	public string? CastSound { get; set; }
}