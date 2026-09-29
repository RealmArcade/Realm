using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class MinimapHelper
{
	public static (float physicalWidth, float physicalDepth, float quadSize) GetTerrainDimensions()
	{
		float quadSize = GameHost.Instance?.GroundTerrain?.QuadSize ?? 2.0f;
		float physicalWidth = (GameHost.Instance?.GroundTerrain?.Width - 1 ?? 125) * quadSize;
		float physicalDepth = (GameHost.Instance?.GroundTerrain?.Depth - 1 ?? 125) * quadSize;
		if (physicalWidth <= 0.0f) physicalWidth = 250.0f;
		if (physicalDepth <= 0.0f) physicalDepth = 250.0f;
		return (physicalWidth, physicalDepth, quadSize);
	}

	public static Vector2 WorldToMinimap(Vector3 worldPosition, Vector2 minimapSize)
	{
		var (physicalWidth, physicalDepth, _) = GetTerrainDimensions();
		if (physicalWidth <= 0.0f || physicalDepth <= 0.0f) return Vector2.Zero;

		float xRatio = Mathf.Clamp((worldPosition.X / physicalWidth) + 0.5f, 0.0f, 1.0f);
		float yRatio = Mathf.Clamp((worldPosition.Z / physicalDepth) + 0.5f, 0.0f, 1.0f);

		return new Vector2(xRatio * minimapSize.X, yRatio * minimapSize.Y);
	}

	public static Vector3 MinimapToWorld(Vector2 minimapPosition, Vector2 minimapSize)
	{
		var (physicalWidth, physicalDepth, _) = GetTerrainDimensions();
		float xRatio = minimapSize.X > 0.0f ? minimapPosition.X / minimapSize.X : 0.5f;
		float yRatio = minimapSize.Y > 0.0f ? minimapPosition.Y / minimapSize.Y : 0.5f;

		float worldX = (xRatio - 0.5f) * physicalWidth;
		float worldZ = (yRatio - 0.5f) * physicalDepth;

		if (GameHost.Instance != null)
		{
			float minX = GameHost.Instance.EditorCameraBoundsLeft;
			float maxX = GameHost.Instance.EditorCameraBoundsRight;
			float minZ = GameHost.Instance.EditorCameraBoundsTop;
			float maxZ = GameHost.Instance.EditorCameraBoundsBottom;

			worldX = Mathf.Clamp(worldX, Mathf.Min(minX, maxX), Mathf.Max(minX, maxX));
			worldZ = Mathf.Clamp(worldZ, Mathf.Min(minZ, maxZ), Mathf.Max(minZ, maxZ));
		}

		float height = 0.0f;
		if (GameHost.Instance?.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.GetHeightAndNormal(worldX, worldZ, out height, out _);
		}

		return new Vector3(worldX, height, worldZ);
	}

	public static Vector3 ProjectRayToGround(Camera3D camera, Vector2 screenPosition)
	{
		if (camera == null) return Vector3.Zero;
		Vector3 rayOrigin = camera.ProjectRayOrigin(screenPosition);
		Vector3 rayNormal = camera.ProjectRayNormal(screenPosition);

		if (Mathf.IsZeroApprox(rayNormal.Y))
		{
			return rayOrigin + rayNormal * 1000.0f;
		}

		float t = -rayOrigin.Y / rayNormal.Y;
		if (t < 0.0f || t > 1000.0f)
		{
			t = 1000.0f;
		}

		return rayOrigin + t * rayNormal;
	}

	public static void CalculateCameraFrustumMinimapPoints(Camera3D camera, Vector2 minimapSize, Vector2[] outPoints)
	{
		if (camera == null || outPoints == null || outPoints.Length < 4) return;
		var viewport = camera.GetViewport();
		if (viewport == null) return;
		Vector2 viewportSize = viewport.GetVisibleRect().Size;

		Vector2 topLeftScreen = new Vector2(0.0f, 0.0f);
		Vector2 topRightScreen = new Vector2(viewportSize.X, 0.0f);
		Vector2 bottomRightScreen = new Vector2(viewportSize.X, viewportSize.Y);
		Vector2 bottomLeftScreen = new Vector2(0.0f, viewportSize.Y);

		Vector3 pTL = ProjectRayToGround(camera, topLeftScreen);
		Vector3 pTR = ProjectRayToGround(camera, topRightScreen);
		Vector3 pBR = ProjectRayToGround(camera, bottomRightScreen);
		Vector3 pBL = ProjectRayToGround(camera, bottomLeftScreen);

		outPoints[0] = WorldToMinimap(pTL, minimapSize);
		outPoints[1] = WorldToMinimap(pTR, minimapSize);
		outPoints[2] = WorldToMinimap(pBR, minimapSize);
		outPoints[3] = WorldToMinimap(pBL, minimapSize);
	}

	public static TextureRect SetupMinimapBackground(Control minimapArea)
	{
		if (minimapArea == null) return null;
		var existing = minimapArea.GetNodeOrNull<TextureRect>("MinimapBg");
		if (existing != null)
		{
			existing.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			existing.StretchMode = TextureRect.StretchModeEnum.Scale;
			existing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			existing.MouseFilter = Control.MouseFilterEnum.Ignore;
			return existing;
		}

		var minimapBg = new TextureRect();
		minimapBg.Name = "MinimapBg";
		minimapBg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		minimapBg.StretchMode = TextureRect.StretchModeEnum.Scale;
		minimapBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		minimapBg.MouseFilter = Control.MouseFilterEnum.Ignore;
		minimapArea.AddChild(minimapBg);
		minimapArea.MoveChild(minimapBg, 0);
		return minimapBg;
	}

	public static async Task<Image?> CaptureTerrainMinimapImageAsync(Node parentNode, int resolution = 256)
	{
		if (parentNode == null || !GodotObject.IsInstanceValid(parentNode)) return null;
		var tree = parentNode.GetTree();
		if (tree == null) return null;

		var (physicalWidth, physicalDepth, quadSize) = GetTerrainDimensions();

		int viewportWidth = resolution;
		int viewportHeight = resolution;

		if (physicalWidth >= physicalDepth && physicalWidth > 0.0f)
		{
			viewportHeight = Mathf.Max(16, Mathf.RoundToInt(resolution * physicalDepth / physicalWidth));
		}
		else if (physicalDepth > physicalWidth && physicalDepth > 0.0f)
		{
			viewportWidth = Mathf.Max(16, Mathf.RoundToInt(resolution * physicalWidth / physicalDepth));
		}

		var shroudMesh = GameHost.Instance?.MainNode?.GetNodeOrNull<MeshInstance3D>("3DShroudMesh")
		              ?? GameHost.Instance?.MainNode?.GetNodeOrNull<MeshInstance3D>("3DFogMesh");
		bool wasShroudVisible = false;
		if (shroudMesh != null && GodotObject.IsInstanceValid(shroudMesh))
		{
			wasShroudVisible = shroudMesh.Visible;
			shroudMesh.Visible = false;
		}

		bool wasBrushVisible = false;
		if (GameHost.Instance?.BrushIndicatorMesh != null && GodotObject.IsInstanceValid(GameHost.Instance.BrushIndicatorMesh))
		{
			wasBrushVisible = GameHost.Instance.BrushIndicatorMesh.Visible;
			GameHost.Instance.BrushIndicatorMesh.Visible = false;
		}

		var wasGridMode = GameHost.GridOverlayMode.Off;
		bool wasPathingVisible = false;
		if (GameHost.Instance != null)
		{
			wasGridMode = GameHost.Instance.EditorGridMode;
			wasPathingVisible = GameHost.Instance.PathingOverlayVisible;
			GameHost.Instance.EditorGridMode = GameHost.GridOverlayMode.Off;
			GameHost.Instance.PathingOverlayVisible = false;
			GameHost.Instance.UpdateGridOverlayVisibility();
			GameHost.Instance.UpdatePathingOverlay();
		}

		var pathingMesh = GameHost.Instance?.PathingOverlayMesh;
		bool wasPathingMeshVisible = false;
		if (pathingMesh != null && GodotObject.IsInstanceValid(pathingMesh))
		{
			wasPathingMeshVisible = pathingMesh.Visible;
			pathingMesh.Visible = false;
		}

		var unitsList = GameHost.Instance?.AllUnits;
		var unitVisibility = new List<(Unit3D unit, bool visible)>();
		if (unitsList != null)
		{
			foreach (var u in unitsList)
			{
				if (u != null && GodotObject.IsInstanceValid(u))
				{
					unitVisibility.Add((u, u.Visible));
					u.Visible = false;
				}
			}
		}

		SubViewport viewport = null;
		try
		{
			viewport = new SubViewport();
			viewport.TransparentBg = true;
			viewport.Size = new Vector2I(viewportWidth, viewportHeight);
			viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
			viewport.DebugDraw = Viewport.DebugDrawEnum.Unshaded;
			parentNode.AddChild(viewport);

			var camera = new Camera3D();
			camera.Projection = Camera3D.ProjectionType.Orthogonal;
			camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
			camera.Size = physicalDepth;
			camera.Far = 200.0f;
			camera.Position = new Vector3(-0.5f * quadSize, 100.0f, -0.5f * quadSize);
			camera.RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f);
			viewport.AddChild(camera);

			RuntimeTerrain.IsMinimapRendering = true;
			RuntimeTerrain.Instance?.BeginMinimapCapture();
			PropMultiMeshManager.Instance?.SetAllNodesVisible(true);

			try
			{
				await parentNode.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

				var texture = viewport.GetTexture();
				if (texture != null)
				{
					var img = texture.GetImage();
					if (img != null && !img.IsEmpty())
					{
						return (Image)img.Duplicate();
					}
				}
			}
			finally
			{
				RuntimeTerrain.Instance?.EndMinimapCapture();
				RuntimeTerrain.IsMinimapRendering = false;
			}
		}
		finally
		{
			if (viewport != null && GodotObject.IsInstanceValid(viewport))
			{
				viewport.QueueFree();
			}
			if (shroudMesh != null && GodotObject.IsInstanceValid(shroudMesh))
			{
				shroudMesh.Visible = wasShroudVisible;
			}
			if (pathingMesh != null && GodotObject.IsInstanceValid(pathingMesh))
			{
				pathingMesh.Visible = wasPathingMeshVisible;
			}
			if (GameHost.Instance?.BrushIndicatorMesh != null && GodotObject.IsInstanceValid(GameHost.Instance.BrushIndicatorMesh))
			{
				GameHost.Instance.BrushIndicatorMesh.Visible = wasBrushVisible;
			}
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorGridMode = wasGridMode;
				GameHost.Instance.PathingOverlayVisible = wasPathingVisible;
				GameHost.Instance.UpdateGridOverlayVisibility();
				GameHost.Instance.UpdatePathingOverlay();
			}
			foreach (var (u, vis) in unitVisibility)
			{
				if (u != null && GodotObject.IsInstanceValid(u))
				{
					u.Visible = vis;
				}
			}
		}

		return null;
	}

	public static async Task<ImageTexture?> CaptureTerrainMinimapTextureAsync(Node parentNode, int resolution = 256)
	{
		var img = await CaptureTerrainMinimapImageAsync(parentNode, resolution);
		if (img != null)
		{
			return ImageTexture.CreateFromImage(img);
		}
		return null;
	}
}
