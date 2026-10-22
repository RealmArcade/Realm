using System;
using Godot;

namespace Realm.Client.Utils;

public static class ModelExtensions
{
	public static Vector3 ToGodotVector3(this Vector3Data v) => new(v.X, v.Y, v.Z);
	public static Vector3 ToGodotVector3(this Vector3Data? v, Vector3 defaultValue = default) => v.HasValue ? new(v.Value.X, v.Value.Y, v.Value.Z) : defaultValue;
	public static Vector3Data ToVector3Data(this Vector3 v) => new(v.X, v.Y, v.Z);

	public static Vector2 ToGodotVector2(this Vector2Data v) => new(v.X, v.Y);
	public static Vector2 ToGodotVector2(this Vector2Data? v, Vector2 defaultValue = default) => v.HasValue ? new(v.Value.X, v.Value.Y) : defaultValue;
	public static Vector2Data ToVector2Data(this Vector2 v) => new(v.X, v.Y);

	public static Color GetSunColor(this EnvironmentPresetConfig config)
	{
		return Color.HtmlIsValid(config.SunColorHex) ? Color.FromHtml(config.SunColorHex) : new Color(1.000f, 0.980f, 0.940f);
	}

	public static Color GetAmbientColor(this EnvironmentPresetConfig config)
	{
		return Color.HtmlIsValid(config.AmbientColorHex) ? Color.FromHtml(config.AmbientColorHex) : new Color(0.480f, 0.580f, 0.740f);
	}

	public static Color GetFogColor(this EnvironmentPresetConfig config)
	{
		return Color.HtmlIsValid(config.FogColorHex) ? Color.FromHtml(config.FogColorHex) : new Color(0.550f, 0.650f, 0.750f);
	}

	public static Aabb CalculateLocalBounds(this Node3D root)
	{
		Aabb combinedBounds = new Aabb();
		bool hasBounds = false;

		if (root != null)
		{
			CollectLocalBounds(root, root, ref combinedBounds, ref hasBounds);
		}

		if (!hasBounds)
		{
			combinedBounds = new Aabb(new Vector3(-0.1f, -0.5f, -0.1f), new Vector3(0.2f, 1.0f, 0.2f));
		}

		return combinedBounds;
	}

	private static void CollectLocalBounds(Node current, Node3D root, ref Aabb combinedBounds, ref bool hasBounds)
	{
		if (current is MeshInstance3D meshInstance && meshInstance.Mesh != null && meshInstance.Visible && !IsAttachmentHelperNode(meshInstance))
		{
			Transform3D relativeTransform = CalculateBoundsRelativeTransform(meshInstance, root);
			if (Mathf.Abs(relativeTransform.Basis.Determinant()) > 0.0001f)
			{
				MathUtils.UpdateAabbWithMeshCorners(ref combinedBounds, ref hasBounds, meshInstance, relativeTransform);
			}
		}

		foreach (Node child in current.GetChildren())
		{
			if (child is BoneAttachment3D) continue;
			CollectLocalBounds(child, root, ref combinedBounds, ref hasBounds);
		}
	}

	private static Transform3D CalculateBoundsRelativeTransform(Node node, Node3D root)
	{
		Transform3D relativeTransform = Transform3D.Identity;
		Node? current = node;
		while (current != null && current != root)
		{
			if (current is Node3D node3D)
			{
				relativeTransform = node3D.Transform * relativeTransform;
			}

			current = current.GetParent();
		}

		return relativeTransform;
	}

	private static bool IsAttachmentHelperNode(Node node)
	{
		string nodeName = node.Name.ToString();
		return nodeName.StartsWith("PseudoSocket_", StringComparison.OrdinalIgnoreCase) ||
		       nodeName.StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase) ||
		       nodeName.StartsWith("Att_", StringComparison.OrdinalIgnoreCase) ||
		       nodeName.StartsWith("AttVisual_", StringComparison.OrdinalIgnoreCase) ||
		       nodeName.StartsWith("PlayerColorGlow", StringComparison.OrdinalIgnoreCase);
	}

	public static void ApplySlopeAlignment(Godot.Node3D mainNode, Godot.Node3D? visualNode, bool isSlopeAligned)
	{
		if (isSlopeAligned && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null && visualNode != null && Godot.GodotObject.IsInstanceValid(visualNode))
		{
			Realm.Client.Core.GameHost.Instance.GroundTerrain.GetHeightAndNormal(mainNode.GlobalPosition.X, mainNode.GlobalPosition.Z, out _, out Godot.Vector3 normal);
			if (normal.LengthSquared() > 0.01f)
			{
				normal = normal.Normalized();
				Godot.Vector3 up = normal;
				Godot.Vector3 forward = System.MathF.Abs(up.Y) < 0.99f ? Godot.Vector3.Up.Cross(up).Cross(up).Normalized() : -Godot.Vector3.Forward;
				Godot.Vector3 right = up.Cross(forward).Normalized();
				Godot.Vector3 visualScale = visualNode.Scale;
				visualNode.Basis = new Godot.Basis(right * visualScale.X, up * visualScale.Y, -forward * visualScale.Z);
			}
		}
	}
}