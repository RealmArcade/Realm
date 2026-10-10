using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;
using Realm.Client.Core;

namespace Realm.Client.UI;

public static class MinimapHelper
{
	public static (float physicalWidth, float physicalDepth, float quadSize) GetTerrainDimensions()
	{
		var terrain = Realm.Client.Core.GameHost.Instance?.GroundTerrain;
		if (terrain == null) return (250.0f, 250.0f, 2.0f);

		float quadSize = terrain.QuadSize;
		float physicalWidth = (terrain.Width - 1) * quadSize;
		float physicalDepth = (terrain.Depth - 1) * quadSize;

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

		if (Realm.Client.Core.GameHost.Instance != null)
		{
			float minX = Realm.Client.Core.GameHost.Instance.EditorCameraBoundsLeft;
			float maxX = Realm.Client.Core.GameHost.Instance.EditorCameraBoundsRight;
			float minZ = Realm.Client.Core.GameHost.Instance.EditorCameraBoundsTop;
			float maxZ = Realm.Client.Core.GameHost.Instance.EditorCameraBoundsBottom;

			worldX = Mathf.Clamp(worldX, Mathf.Min(minX, maxX), Mathf.Max(minX, maxX));
			worldZ = Mathf.Clamp(worldZ, Mathf.Min(minZ, maxZ), Mathf.Max(minZ, maxZ));
		}

		float height = 0.0f;
		if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			Realm.Client.Core.GameHost.Instance.GroundTerrain.GetHeightAndNormal(worldX, worldZ, out height, out _);
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

	private static readonly System.Threading.SemaphoreSlim _captureLock = new(1, 1);

	private class OverlayState
	{
		public MeshInstance3D ShroudMesh;
		public bool WasShroudVisible;
		public bool WasBrushVisible;
		public Realm.Client.Core.GameHost.GridOverlayMode WasGridMode;
		public bool WasPathingVisible;
		public MeshInstance3D PathingMesh;
		public bool WasPathingMeshVisible;
		public List<(Realm.Client.Unit3D unit, bool visible)> UnitVisibility = new();
	}

	private static bool IsNodeValid(Node node)
	{
		return node != null && GodotObject.IsInstanceValid(node);
	}

	private static void HideShroudMesh(OverlayState state, GameHost gameHost)
	{
		state.ShroudMesh = gameHost?.MainNode?.GetNodeOrNull<MeshInstance3D>("3DShroudMesh")
		                   ?? gameHost?.MainNode?.GetNodeOrNull<MeshInstance3D>("3DFogMesh");

		if (!IsNodeValid(state.ShroudMesh)) return;

		state.WasShroudVisible = state.ShroudMesh.Visible;
		state.ShroudMesh.Visible = false;
	}

	private static void HideBrushIndicator(OverlayState state, GameHost gameHost)
	{
		if (!IsNodeValid(gameHost?.BrushIndicatorMesh)) return;

		state.WasBrushVisible = gameHost.BrushIndicatorMesh.Visible;
		gameHost.BrushIndicatorMesh.Visible = false;
	}

	private static void HideGridAndPathing(OverlayState state, GameHost gameHost)
	{
		state.WasGridMode = Realm.Client.Core.GameHost.GridOverlayMode.Off;
		if (gameHost == null) return;

		state.WasGridMode = gameHost.EditorGridMode;
		state.WasPathingVisible = gameHost.PathingOverlayVisible;
		gameHost.EditorGridMode = Realm.Client.Core.GameHost.GridOverlayMode.Off;
		gameHost.PathingOverlayVisible = false;
		gameHost.UpdateGridOverlayVisibility();
		gameHost.UpdatePathingOverlay();
	}

	private static void HidePathingMesh(OverlayState state, GameHost gameHost)
	{
		state.PathingMesh = gameHost?.PathingOverlayMesh;
		if (!IsNodeValid(state.PathingMesh)) return;

		state.WasPathingMeshVisible = state.PathingMesh.Visible;
		state.PathingMesh.Visible = false;
	}

	private static void HideUnits(OverlayState state, GameHost gameHost)
	{
		var unitsList = gameHost?.AllUnits;
		if (unitsList == null) return;

		foreach (var u in unitsList)
		{
			if (IsNodeValid(u))
			{
				state.UnitVisibility.Add((u, u.Visible));
				u.Visible = false;
			}
		}
	}

	private static OverlayState HideOverlays()
	{
		var state = new OverlayState();
		var gameHost = Realm.Client.Core.GameHost.Instance;

		HideShroudMesh(state, gameHost);
		HideBrushIndicator(state, gameHost);
		HideGridAndPathing(state, gameHost);
		HidePathingMesh(state, gameHost);
		HideUnits(state, gameHost);

		return state;
	}

	private static void RestoreOverlays(OverlayState state)
	{
		if (IsNodeValid(state.ShroudMesh))
		{
			state.ShroudMesh.Visible = state.WasShroudVisible;
		}

		if (IsNodeValid(state.PathingMesh))
		{
			state.PathingMesh.Visible = state.WasPathingMeshVisible;
		}

		var gameHost = Realm.Client.Core.GameHost.Instance;
		if (IsNodeValid(gameHost?.BrushIndicatorMesh))
		{
			gameHost.BrushIndicatorMesh.Visible = state.WasBrushVisible;
		}

		if (gameHost != null)
		{
			gameHost.EditorGridMode = state.WasGridMode;
			gameHost.PathingOverlayVisible = state.WasPathingVisible;
			gameHost.UpdateGridOverlayVisibility();
			gameHost.UpdatePathingOverlay();
		}

		foreach (var (u, vis) in state.UnitVisibility)
		{
			if (IsNodeValid(u))
			{
				u.Visible = vis || (gameHost?.IsMapEditorMode == true && gameHost.AllUnits.Contains(u));
			}
		}
	}

	private static (int width, int height) CalculateViewportSize(int resolution, float physicalWidth, float physicalDepth)
	{
		int width = resolution;
		int height = resolution;

		if (physicalWidth >= physicalDepth && physicalWidth > 0.0f)
		{
			height = Mathf.Max(16, Mathf.RoundToInt(resolution * physicalDepth / physicalWidth));
		}
		else if (physicalDepth > physicalWidth && physicalDepth > 0.0f)
		{
			width = Mathf.Max(16, Mathf.RoundToInt(resolution * physicalWidth / physicalDepth));
		}

		return (width, height);
	}

	private static SubViewport SetupMinimapViewport(Node parentNode, int viewportWidth, int viewportHeight, float physicalDepth, float quadSize)
	{
		var viewport = new SubViewport();
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

		return viewport;
	}

	private static Image ExtractViewportImage(SubViewport viewport)
	{
		var texture = viewport.GetTexture();
		if (texture == null) return null;

		var img = texture.GetImage();
		if (img == null || img.IsEmpty()) return null;

		return (Image)img.Duplicate();
	}

	private static async Task<Image?> PerformCaptureWithViewportAsync(Node parentNode, SceneTree tree, SubViewport viewport)
	{
		Realm.Client.RuntimeTerrain.IsMinimapRendering = true;
		Realm.Client.RuntimeTerrain.Instance?.BeginMinimapCapture();
		Realm.Client.Core.GameHost.Instance?.BeginMinimapCapture();
		Realm.Client.PropMultiMeshManager.Instance?.SetAllNodesVisible(true);

		try
		{
			await parentNode.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			return ExtractViewportImage(viewport);
		}
		finally
		{
			Realm.Client.RuntimeTerrain.IsMinimapRendering = false;
			Realm.Client.Core.GameHost.Instance?.EndMinimapCapture();
			Realm.Client.RuntimeTerrain.Instance?.EndMinimapCapture();
		}
	}

	private static async Task<Image?> PerformCaptureUnderLockAsync(Node parentNode, SceneTree tree, int resolution)
	{
		var (physicalWidth, physicalDepth, quadSize) = GetTerrainDimensions();
		var (viewportWidth, viewportHeight) = CalculateViewportSize(resolution, physicalWidth, physicalDepth);

		var overlayState = HideOverlays();
		SubViewport viewport = null;

		try
		{
			viewport = SetupMinimapViewport(parentNode, viewportWidth, viewportHeight, physicalDepth, quadSize);
			return await PerformCaptureWithViewportAsync(parentNode, tree, viewport);
		}
		finally
		{
			if (IsNodeValid(viewport)) viewport.QueueFree();
			RestoreOverlays(overlayState);
		}
	}

	public static async Task<Image?> CaptureTerrainMinimapImageAsync(Node parentNode, int resolution = 256)
	{
		if (!IsNodeValid(parentNode)) return null;
		var tree = parentNode.GetTree();
		if (tree == null) return null;

		await _captureLock.WaitAsync();
		try
		{
			return await PerformCaptureUnderLockAsync(parentNode, tree, resolution);
		}
		finally
		{
			_captureLock.Release();
		}
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