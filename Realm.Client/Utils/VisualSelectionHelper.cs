using Godot;

namespace Realm.Client.Utils;

public static class VisualSelectionHelper
{
	public static void CalculateSelectionRingParams(Vector3 scale, string assetKey, float baseRadius, out float sx, out float ratio, out float worldRadius)
	{
		sx = Mathf.Abs(scale.X) > 0.001f ? scale.X : 1.0f;
		ratio = Core.GameHost.Instance != null ? Core.GameHost.Instance.GetModelCollisionCircleRatio(assetKey) : 1.0f;
		if (ratio <= 0.001f) ratio = 1.0f;

		worldRadius = baseRadius * ratio * Mathf.Abs(sx);
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
