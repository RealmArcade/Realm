using Godot;

namespace Realm.Client;

[GlobalClass]
public partial class CharacterReadabilitySetup : Node3D
{
	[Export]
	public Color AccentColor { get; set; } = new Color(0.95f, 0.82f, 0.55f);

	[Export]
	public float TargetRoughness { get; set; } = 0.65f;

	[Export]
	public float TargetSpecular { get; set; } = 0.25f;

	[Export]
	public float BaseEmissionEnergy { get; set; } = 0.07f;

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
		material.Roughness = TargetRoughness;
		material.MetallicSpecular = TargetSpecular;

		material.EmissionEnabled = true;
		material.Emission = AccentColor;
		material.EmissionEnergyMultiplier = BaseEmissionEnergy;
	}

	private static void AssignCharacterFillLightLayer(MeshInstance3D meshInstance)
	{
		meshInstance.Layers = 0b11;
	}
}