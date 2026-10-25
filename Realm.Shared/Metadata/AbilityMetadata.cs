using System.Collections.Generic;

namespace Realm.Shared.Metadata;

public class AbilityMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string AbilityType { get; set; } = "target_spell";
	public string? IconPath { get; set; }
	public float ManaCost { get; set; }
	public float Cooldown { get; set; }
	public float TargetRange { get; set; }
	public string? VisualEffect { get; set; }
	public string? CastSound { get; set; }

	/// <summary>Optional autocast policy. Absent means castable whenever affordable and ready.</summary>
	public AutoCastMetadata? AutoCast { get; set; }

	/// <summary>Visual and targeting size, and the default radius for effects that do not set one.</summary>
	public float AreaOfEffectRadius { get; set; }

	/// <summary>
	///     What the cast does, in order. An ability with no effects still fires its cast callback, which
	///     is how scripted abilities work; an ability with effects never reaches the script.
	/// </summary>
	public List<AbilityEffectMetadata>? Effects { get; set; }

	public int GridX { get; set; } = -1;
	public int GridY { get; set; } = -1;
	public string? Hotkey { get; set; }

	public string? Attribution_ImportedFromMap { get; set; }
}
