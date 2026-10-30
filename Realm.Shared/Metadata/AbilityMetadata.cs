using System.Collections.Generic;

namespace Realm.Shared.Metadata;

public class AbilityMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;

	public string? Role { get; set; }

	public string AbilityType { get; set; } = "target_spell";
	public string? IconPath { get; set; }
	public float ManaCost { get; set; }
	public float Cooldown { get; set; }
	public float TargetRange { get; set; }
	public string? VisualEffect { get; set; }
	public string? CastSound { get; set; }

	public AutoCastMetadata? AutoCast { get; set; }

	public float AreaOfEffectRadius { get; set; }

	public List<AbilityEffectMetadata>? Effects { get; set; }

	public int GridX { get; set; } = -1;
	public int GridY { get; set; } = -1;
	public string? Hotkey { get; set; }

	public string? Attribution_ImportedFromMap { get; set; }
}
