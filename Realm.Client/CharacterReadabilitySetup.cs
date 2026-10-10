using Godot;

namespace Realm.Client;

/// <summary>
/// Attach this node as a direct child of a Unit3D scene root.
/// On _Ready it recursively patches every StandardMaterial3D found in the
/// character hierarchy so that models remain visually readable under all
/// Day/Night lighting conditions without modifying source assets.
///
/// Visual Layer Setup
///   • Sets every MeshInstance3D to render on BOTH Layer 1 (world) and
///     Layer 2 (character-only fill light).
///   • CharacterFillLight (OmniLight3D, CullMask = Layer 2) in Main.tscn
///     drives energy dynamically via EnvironmentService.UpdateDayNightVisuals,
///     peaking at 0.42 during Night so characters outshine the terrain.
/// </summary>
public partial class CharacterReadabilitySetup : Node3D
{
	/// <summary>
	/// The unit's primary accent colour. Used as the subtle emission tint so
	/// the model retains identity colour even in total darkness. Ally units
	/// typically use a warm gold; enemy units a deep crimson.
	/// </summary>
	[Export] public Color AccentColor { get; set; } = new Color(0.95f, 0.82f, 0.55f);

	/// <summary>
	/// Roughness target for all model surfaces. Balanced at 0.65 so armour
	/// does not mirror-reflect a dark skybox into total black, while still
	/// allowing sharp specular glints from the high-energy directional sun.
	/// </summary>
	[Export] public float TargetRoughness { get; set; } = 0.65f;

	/// <summary>
	/// Specular intensity target. 0.25 allows hard glints on armor poly-edges
	/// without blowing highlights out to flat white under Filmic tonemap.
	/// </summary>
	[Export] public float TargetSpecular { get; set; } = 0.25f;

	/// <summary>
	/// Emission energy. Low enough to be invisible in daylight, but prevents
	/// models from reaching absolute-black under any lighting configuration.
	/// </summary>
	[Export] public float BaseEmissionEnergy { get; set; } = 0.07f;

	public override void _Ready()
	{
		PatchMeshesRecursive(this);
	}

	private void PatchMeshesRecursive(Node node)
	{
		if (node is MeshInstance3D meshInstance)
		{
			AssignCharacterFillLightLayer(meshInstance);
			int surfaceCount = meshInstance.GetSurfaceOverrideMaterialCount();

			for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
			{
				Material sourceMaterial = meshInstance.GetActiveMaterial(surfaceIndex);
				if (sourceMaterial == null) continue;

				if (sourceMaterial is StandardMaterial3D standardMaterial)
				{
					var patchedMaterial = (StandardMaterial3D)standardMaterial.Duplicate();
					ApplyReadabilityPatches(patchedMaterial);
					meshInstance.SetSurfaceOverrideMaterial(surfaceIndex, patchedMaterial);
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			PatchMeshesRecursive(child);
		}
	}

	private void ApplyReadabilityPatches(StandardMaterial3D material)
	{
		// Prevent skybox reflections from turning the surface black at night.
		material.Roughness = TargetRoughness;
		material.MetallicSpecular = TargetSpecular;

		// Constant internal glow: barely perceptible in full daylight, but
		// guarantees the model never reaches 0-luminance at midnight.
		material.EmissionEnabled = true;
		material.Emission = AccentColor;
		material.EmissionEnergyMultiplier = BaseEmissionEnergy;
	}

	private static void AssignCharacterFillLightLayer(MeshInstance3D meshInstance)
	{
		// Godot visual layers are bit-flags.
		// Layer 1 = bit 0 (world geometry, camera default).
		// Layer 2 = bit 1 (character-only fill light target).
		// Setting both keeps the mesh visible to the main camera AND to the
		// dedicated fill light whose cull mask targets layer 2 exclusively.
		meshInstance.Layers = 0b11; // Layer 1 + Layer 2
	}
}