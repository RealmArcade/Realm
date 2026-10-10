using Godot;
using System;

namespace Realm.Client.UI.MapEditor;

public class MapEditorMinimap
{
	private PanelContainer _minimapFrame;
	private Control _minimapArea;
	private MapEditorCameraIndicator _cameraIndicator;
	private Node _hudNode;
	private bool _isDragging;

	public bool IsDragging => _isDragging && Input.IsMouseButtonPressed(MouseButton.Left);

	public MapEditorMinimap(PanelContainer minimapFrame, Control minimapArea, MapEditorCameraIndicator cameraIndicator, Node hudNode)
	{
		_minimapFrame = minimapFrame;
		_minimapArea = minimapArea;
		_cameraIndicator = cameraIndicator;
		_hudNode = hudNode;

		_minimapArea.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mouseBtn && mouseBtn.ButtonIndex == MouseButton.Left)
			{
				_isDragging = mouseBtn.Pressed;
				if (mouseBtn.Pressed)
				{
					TeleportCameraToMinimapPos(mouseBtn.Position);
				}
			}
			else if (@event is InputEventMouseMotion mouseMotion && mouseMotion.ButtonMask == MouseButtonMask.Left)
			{
				TeleportCameraToMinimapPos(mouseMotion.Position);
			}
		};
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		UpdateMinimapIndicator();
	}

	private void TeleportCameraToMinimapPos(Vector2 clickPos)
	{
		if (_minimapArea == null || Realm.Client.Core.GameHost.Instance == null) return;
		var worldPos = MinimapHelper.MinimapToWorld(clickPos, _minimapArea.Size);
		var camera = Realm.Client.Core.GameHost.Instance.GetViewport()?.GetCamera3D();
		if (camera != null)
		{
			camera.GlobalPosition = new Vector3(worldPos.X, camera.GlobalPosition.Y, worldPos.Z);
		}
	}

	private Vector3 _lastCameraPos;
	private Vector3 _lastCameraRot;
	private readonly Vector2[] _cachedMinimapPoints = new Vector2[4];

	private void UpdateMinimapIndicator()
	{
		if (_cameraIndicator == null || _minimapArea == null || Realm.Client.Core.GameHost.Instance == null) return;
		var camera = Realm.Client.Core.GameHost.Instance.GetViewport()?.GetCamera3D();
		if (camera == null) return;

		Vector3 camPos = camera.GlobalPosition;
		Vector3 camRot = camera.GlobalRotation;
		if ((camPos - _lastCameraPos).LengthSquared() < 0.0001f && (camRot - _lastCameraRot).LengthSquared() < 0.0001f)
		{
			return;
		}
		_lastCameraPos = camPos;
		_lastCameraRot = camRot;

		MinimapHelper.CalculateCameraFrustumMinimapPoints(camera, _minimapArea.Size, _cachedMinimapPoints);
		_cameraIndicator.SetPoints(_cachedMinimapPoints);
	}

	private bool _isGeneratingMinimap = false;
	private bool _needsRegen = false;

	public void RegenerateMinimap()
	{
		if (_isGeneratingMinimap)
		{
			_needsRegen = true;
			return;
		}
		GenerateDynamicMinimap();
	}

	private async void GenerateDynamicMinimap()
	{
		if (_isGeneratingMinimap)
		{
			_needsRegen = true;
			return;
		}
		_isGeneratingMinimap = true;

		try
		{
			do
			{
				_needsRegen = false;
				bool success = await TryGenerateSingleMinimapPassAsync();
				if (!success) break;
			} while (_needsRegen);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to dynamically capture terrain minimap: {ex.Message}");
		}
		finally
		{
			_isGeneratingMinimap = false;
		}
	}

	private async System.Threading.Tasks.Task<bool> TryGenerateSingleMinimapPassAsync()
	{
		if (_hudNode == null || !GodotObject.IsInstanceValid(_hudNode)) return false;
		var tree = _hudNode.GetTree();
		if (tree == null) return false;

		await _hudNode.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		await _hudNode.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

		if (_minimapArea == null) return false;
		var minimapBg = _minimapArea.GetChildCount() > 0 ? _minimapArea.GetChild<TextureRect>(0) : null;
		if (minimapBg == null) return false;

		var imgTexture = await MinimapHelper.CaptureTerrainMinimapTextureAsync(_hudNode, 256);
		if (imgTexture != null)
		{
			minimapBg.Texture = imgTexture;
		}
		return true;
	}

	public async System.Threading.Tasks.Task<bool> GenerateAndSaveMinimapThumbnailAsync(string workspacePath)
	{
		if (string.IsNullOrWhiteSpace(workspacePath) || !System.IO.Directory.Exists(workspacePath)) return false;
		if (_hudNode == null || !GodotObject.IsInstanceValid(_hudNode)) return false;

		try
		{
			var img = await MinimapHelper.CaptureTerrainMinimapImageAsync(_hudNode, 512);
			if (img == null || img.IsEmpty()) return false;

			return ProcessAndSaveThumbnailImage(img, workspacePath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to capture minimap thumbnail.png: {ex.Message}");
		}

		return false;
	}

	private bool ProcessAndSaveThumbnailImage(Image img, string workspacePath)
	{
		if (img.GetFormat() != Image.Format.Rgba8)
		{
			img.Convert(Image.Format.Rgba8);
		}
		if (img.GetWidth() != 512 || img.GetHeight() != 512)
		{
			img.Resize(512, 512, Image.Interpolation.Bilinear);
		}

		string destinationPngPath = System.IO.Path.Combine(workspacePath, "thumbnail.png");
		Realm.Shared.Textures.IndexedPngHelper.SaveAs256ColorPng(
			img.GetData(),
			img.GetWidth(),
			img.GetHeight(),
			destinationPngPath);

		return true;
	}
}