using Godot;

namespace Realm.Client.UI.MapEditor;

public static class ModelPreviewFraming
{
	public const float DefaultYawDegrees = 45.0f;
	public const float DefaultPitchDegrees = 25.0f;
	public const float MinimumCameraDistance = 0.5f;
	public const float MaximumCameraDistance = 50.0f;
	public const float DefaultFallbackDistance = 3.0f;
	private const float FitMargin = 1.08f;
	private const float MinimumBoundsExtent = 0.001f;

	public static Aabb ComputeVisibleBounds(Node root)
	{
		Aabb totalBounds = new Aabb();
		bool hasBounds = false;
		CollectNodeBounds(root, Transform3D.Identity, ref totalBounds, ref hasBounds);
		return hasBounds ? totalBounds : new Aabb(Vector3.Zero, Vector3.Zero);
	}

	public static bool HasUsableBounds(Aabb bounds)
	{
		return bounds.Size.LengthSquared() > MinimumBoundsExtent * MinimumBoundsExtent;
	}

	public static Vector3 ComputeFramedCenter(Aabb bounds)
	{
		return bounds.Position + bounds.Size * 0.5f;
	}

	public static float ComputeCameraDistance(Aabb bounds, float verticalFovDegrees, float aspectRatio)
	{
		float radius = bounds.Size.Length() * 0.5f;
		if (radius < MinimumBoundsExtent)
		{
			return DefaultFallbackDistance;
		}

		float safeVerticalFov = Mathf.Clamp(verticalFovDegrees, 1.0f, 179.0f);
		float halfVerticalFov = Mathf.DegToRad(safeVerticalFov) * 0.5f;
		float safeAspectRatio = aspectRatio > 0.01f ? aspectRatio : 1.0f;
		float halfHorizontalFov = Mathf.Atan(Mathf.Tan(halfVerticalFov) * safeAspectRatio);
		float limitingHalfFov = Mathf.Min(halfVerticalFov, halfHorizontalFov);

		float distance = radius / Mathf.Sin(limitingHalfFov);
		return Mathf.Clamp(distance * FitMargin, MinimumCameraDistance, MaximumCameraDistance);
	}

	public static Vector3 ComputeCameraOffset(float distance, float yawRadians, float pitchRadians)
	{
		float cosPitch = Mathf.Cos(pitchRadians);
		return new Vector3(
			distance * cosPitch * Mathf.Sin(yawRadians),
			distance * Mathf.Sin(pitchRadians),
			distance * cosPitch * Mathf.Cos(yawRadians)
		);
	}

	public static Vector3 ComputeFramedEyePosition(Vector3 center, float distance)
	{
		return center + ComputeCameraOffset(
			distance,
			Mathf.DegToRad(DefaultYawDegrees),
			Mathf.DegToRad(DefaultPitchDegrees)
		);
	}

	private static void CollectNodeBounds(Node node, Transform3D parentTransform, ref Aabb totalBounds, ref bool hasBounds)
	{
		Transform3D nodeTransform = parentTransform;
		if (node is Node3D node3D)
		{
			nodeTransform = parentTransform * node3D.Transform;
		}

		if (node is VisualInstance3D visualInstance && node is not Decal)
		{
			Aabb localBounds = visualInstance.GetAabb();
			if (localBounds.Size.LengthSquared() > MinimumBoundsExtent * MinimumBoundsExtent)
			{
				Aabb transformedBounds = nodeTransform * localBounds;
				totalBounds = hasBounds ? totalBounds.Merge(transformedBounds) : transformedBounds;
				hasBounds = true;
			}
		}

		int childCount = node.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			CollectNodeBounds(node.GetChild(i), nodeTransform, ref totalBounds, ref hasBounds);
		}
	}
}
