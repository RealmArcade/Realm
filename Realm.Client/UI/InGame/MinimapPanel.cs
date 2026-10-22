using Realm.Client.Services;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;

namespace Realm.Client.UI.InGame;

public class MinimapPanel
{
	private Control _minimapArea;
	private MinimapCameraIndicator _cameraIndicator;
	private Camera3D _camera3D;
	private PanelContainer _minimapFrame;
	private bool _isRightClickPanning = false;
	private bool _isLeftClickDragging = false;
	private Vector3 _lastCameraPos;
	private Vector3 _lastCameraRot;
	private readonly Vector2[] _cachedMinimapPoints = new Vector2[4];

	public MinimapPanel(PanelContainer minimapFrame, Control minimapArea, MinimapCameraIndicator cameraIndicator, Camera3D camera3D)
	{
		_minimapFrame = minimapFrame;
		_minimapArea = minimapArea;
		_cameraIndicator = cameraIndicator;
		_camera3D = camera3D;

		SetupMinimap();
	}

	public void SetCamera(Camera3D camera)
	{
		_camera3D = camera;
	}

	private void SetupMinimap()
	{
		_minimapArea.GuiInput += OnMinimapGuiInput;
	}

	private void OnMinimapGuiInput(InputEvent @event)
	{
		bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;

		if (GameContext.ReplayPlayback.IsPlayingReplay || isSpectator)
		{
			HandleReplayOrSpectatorInput(@event);
			return;
		}

		HandleActivePlayerInput(@event);
	}

	private void HandleReplayOrSpectatorInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton rMouseBtn)
		{
			HandleReplayMouseButton(rMouseBtn);
		}
		else if (@event is InputEventMouseMotion rMouseMotion)
		{
			HandleMouseMotion(rMouseMotion);
		}
	}

	private void HandleReplayMouseButton(InputEventMouseButton mouseBtn)
	{
		if (mouseBtn.ButtonIndex == MouseButton.Left)
		{
			_isLeftClickDragging = mouseBtn.Pressed;
			if (mouseBtn.Pressed) TeleportCameraToMinimapPos(mouseBtn.Position);
		}
		else if (mouseBtn.ButtonIndex == MouseButton.Right)
		{
			_isRightClickPanning = mouseBtn.Pressed;
			if (mouseBtn.Pressed) TeleportCameraToMinimapPos(mouseBtn.Position);
		}
	}

	private void HandleActivePlayerInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseBtn)
		{
			HandleActivePlayerMouseButton(mouseBtn);
		}
		else if (@event is InputEventMouseMotion mouseMotion)
		{
			HandleMouseMotion(mouseMotion);
		}
	}

	private void HandleActivePlayerMouseButton(InputEventMouseButton mouseBtn)
	{
		if (mouseBtn.ButtonIndex == MouseButton.Left)
		{
			HandleActivePlayerLeftClick(mouseBtn);
		}
		else if (mouseBtn.ButtonIndex == MouseButton.Right)
		{
			if (!mouseBtn.Pressed) return;
			var minimapWorldPos = MinimapHelper.MinimapToWorld(mouseBtn.Position, _minimapArea.Size);
			GameContext.Host?.HandleMinimapRightClick(minimapWorldPos);
		}
	}

	private void HandleActivePlayerLeftClick(InputEventMouseButton mouseBtn)
	{
		if (!mouseBtn.Pressed)
		{
			_isLeftClickDragging = false;
			return;
		}

		var minimapWorldPos = MinimapHelper.MinimapToWorld(mouseBtn.Position, _minimapArea.Size);

		if (mouseBtn.AltPressed)
		{
			HandleAltLeftClick(minimapWorldPos);
			return;
		}

		HandleGameHostLeftClick(mouseBtn.Position, minimapWorldPos);
	}

	private void HandleAltLeftClick(Vector3 minimapWorldPos)
	{
		if (GameContext.Host == null) return;

		if (GameContext.Host.Multiplayer.MultiplayerPeer != null)
		{
			GameContext.Host.Rpc("NetworkPingMinimap", minimapWorldPos);
		}
		else
		{
			GameContext.Host.AddMinimapPing(minimapWorldPos);
		}
	}

	private void HandleGameHostLeftClick(Vector2 mousePosition, Vector3 minimapWorldPos)
	{
		if (GameContext.Host == null) return;

		if (GameContext.Host.ActivePingMode)
		{
			GameContext.Host.AddMinimapPing(minimapWorldPos);
			GameContext.Host.ActivePingMode = false;
			return;
		}

		if (GameContext.Host.ActiveCommandTargeting != null)
		{
			HandleCommandTargeting(minimapWorldPos);
			return;
		}

		if (GameContext.Host.ActiveSpellTargeting != null)
		{
			GameContext.Host.CastSpellAt(GameContext.Host.ActiveSpellTargeting, minimapWorldPos);
			GameContext.Host.ClearTargetingModes();
			return;
		}

		if (GameContext.Host.ActiveBuildingPlacementType != null)
		{
			GameContext.Host.PlaceBuildingAt(GameContext.Host.ActiveBuildingPlacementType, minimapWorldPos);
			GameContext.Host.ClearTargetingModes();
			return;
		}

		TeleportCameraToMinimapPos(mousePosition);
		_isLeftClickDragging = true;
	}

	private void HandleCommandTargeting(Vector3 minimapWorldPos)
	{
		string cmd = GameContext.Host.ActiveCommandTargeting;
		switch (cmd)
		{
			case "attack":
				GameContext.Host.IssueAttackMoveCommand(minimapWorldPos);
				break;
			case "move":
				GameContext.Host.IssueMoveCommand(minimapWorldPos, Input.IsKeyPressed(Key.Shift));
				break;
			case "patrol":
				GameContext.Host.IssuePatrolCommand(minimapWorldPos);
				break;
			case "rally":
				HandleRallyCommand(minimapWorldPos);
				break;
		}

		GameContext.Host.ClearTargetingModes();
	}

	private void HandleRallyCommand(Vector3 minimapWorldPos)
	{
		if (GameContext.Host == null || GameContext.PerspectiveFilter.IsSpectating) return;
		if (GameContext.Host.SelectedUnits.Count != 1) return;
		var unit = GameContext.Host.SelectedUnits[0];
		int localPlayer = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		if (unit.Player == localPlayer && unit.IsBuilding)
		{
			GameContext.Host.SetRallyPoint(unit, minimapWorldPos);
		}
	}

	private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
	{
		if (_isLeftClickDragging || _isRightClickPanning)
		{
			TeleportCameraToMinimapPos(mouseMotion.Position);
		}
	}

	public void TeleportCameraToMinimapPos(Vector2 clickPos)
	{
		if (_minimapArea == null) return;
		var minimapWorldPos = MinimapHelper.MinimapToWorld(clickPos, _minimapArea.Size);

		if (_camera3D != null && GodotObject.IsInstanceValid(_camera3D))
		{
			_camera3D.GlobalPosition = new Vector3(minimapWorldPos.X, _camera3D.GlobalPosition.Y, minimapWorldPos.Z);
			GameContext.InGameHud?.ShowFeedbackText(string.Format(TranslationServer.Translate("Panned Camera on Minimap to: {0:F0}, {1:F0}"), minimapWorldPos.X, minimapWorldPos.Z), new Color(1, 0.85f, 0.5f));
		}
	}

	public void UpdateMinimapIndicator()
	{
		if (_camera3D == null || !GodotObject.IsInstanceValid(_camera3D) || _cameraIndicator == null || _minimapArea == null) return;

		Vector3 camPos = _camera3D.GlobalPosition;
		Vector3 camRot = _camera3D.GlobalRotation;
		if ((camPos - _lastCameraPos).LengthSquared() < 0.0001f && (camRot - _lastCameraRot).LengthSquared() < 0.0001f)
		{
			return;
		}

		_lastCameraPos = camPos;
		_lastCameraRot = camRot;

		MinimapHelper.CalculateCameraFrustumMinimapPoints(_camera3D, _minimapArea.Size, _cachedMinimapPoints);
		_cameraIndicator.SetPoints(_cachedMinimapPoints);
	}
}