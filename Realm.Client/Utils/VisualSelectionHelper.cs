using Godot;
using Realm.Client.Services;

namespace Realm.Client.Utils;

public static class VisualSelectionHelper
{
	private static readonly System.Collections.Generic.Dictionary<string, TorusMesh> _sharedTorusMeshes = new(System.StringComparer.OrdinalIgnoreCase);
	private static readonly System.Collections.Generic.Dictionary<string, PlaneMesh> _sharedPlayerColorGlowMeshes = new(System.StringComparer.OrdinalIgnoreCase);

	public static TorusMesh GetOrCreateTorusMesh(float radius)
	{
		float r = Mathf.Max(0.4f, (float)System.Math.Round(radius, 2));
		string key = r.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
		if (!_sharedTorusMeshes.TryGetValue(key, out var mesh) || !GodotObject.IsInstanceValid(mesh))
		{
			mesh = new TorusMesh
			{
				InnerRadius = Mathf.Max(0.1f, r - 0.25f),
				OuterRadius = r
			};
			_sharedTorusMeshes[key] = mesh;
		}

		return mesh;
	}

	public static PlaneMesh GetOrCreatePlayerColorGlowMesh(float radius)
	{
		float r = Mathf.Max(0.4f, (float)System.Math.Round(radius, 2));
		string key = r.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
		if (!_sharedPlayerColorGlowMeshes.TryGetValue(key, out var mesh) || !GodotObject.IsInstanceValid(mesh))
		{
			mesh = new PlaneMesh
			{
				Size = new Vector2(r * 2.6f, r * 2.6f),
				Orientation = PlaneMesh.OrientationEnum.Y
			};
			_sharedPlayerColorGlowMeshes[key] = mesh;
		}

		return mesh;
	}

	public static void ClearCache()
	{
		_sharedTorusMeshes.Clear();
		_sharedPlayerColorGlowMeshes.Clear();
		SharedMaterials.ClearCache();
	}

	/// <summary>
	///     How big the ring drawn under a body is, in world units. The body radius is already the ground the body
	///     occupies, so the ring is that radius: multiplying it by the model's circle ratio again drew a ring the size
	///     of the body squared, which is why the rings grew once bodies started taking their size from the map.
	/// </summary>
	public static void CalculateSelectionRingParams(Vector3 scale, string assetKey, float baseRadius, out float sx, out float ratio, out float worldRadius)
	{
		sx = Mathf.Abs(scale.X) > 0.001f ? scale.X : 1.0f;
		ratio = 1.0f;
		worldRadius = baseRadius;
	}

	public static void UpdateSelectionRing(Node3D node, MeshInstance3D? selectionRing, MeshInstance3D? hoverRing, Vector3 scale, float baseRadius)
	{
		if (selectionRing == null && hoverRing == null) return;

		string assetKey = Realm.Client.Services.GameContext.Host?.GetModelAssetKey(node) ?? string.Empty;
		CalculateSelectionRingParams(scale, assetKey, baseRadius, out float sx, out float ratio, out float worldRadius);

		var torusMesh = GetOrCreateTorusMesh(worldRadius);
		UpdateSelectionRingTransform(selectionRing, hoverRing, torusMesh, scale, baseRadius, ratio);
	}

    public static void UpdateSelectionRingTransform(MeshInstance3D? selectionRing, MeshInstance3D? hoverRing, TorusMesh torusMesh, Vector3 scale, float baseRadius, float ratio)
    {
        if (selectionRing == null && hoverRing == null) return;

        float sx = Mathf.Abs(scale.X) > 0.001f ? scale.X : 1.0f;
        float sy = Mathf.Abs(scale.Y) > 0.001f ? scale.Y : 1.0f;
        float sz = Mathf.Abs(scale.Z) > 0.001f ? scale.Z : 1.0f;

        Vector3 ringScale = new Vector3(1.0f / sx, 1.0f / sy, 1.0f / sz);
        Vector3 ringPos = new Vector3(0, 0.05f / sy, 0);

        if (ratio <= 0.001f) ratio = 1.0f;

        if (selectionRing != null)
        {
            selectionRing.Mesh = torusMesh;
            selectionRing.Scale = ringScale;
            selectionRing.Position = ringPos;
        }

        if (hoverRing != null)
        {
            hoverRing.Mesh = torusMesh;
            hoverRing.Scale = ringScale;
            hoverRing.Position = ringPos;
        }
    }
}
