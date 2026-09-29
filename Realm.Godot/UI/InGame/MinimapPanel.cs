using Godot;
using System;
using DotRecast.Detour;
using DotRecast.Core.Numerics;
using Realm.Godot.ReplaySystem;

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
		bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
		_minimapArea.GuiInput += (@event) =>
		{
			if (ReplayPlaybackManager.Instance.IsPlayingReplay || isSpectator)
			{
				if (@event is InputEventMouseButton rMouseBtn)
				{
					if (rMouseBtn.ButtonIndex == MouseButton.Left)
					{
						if (rMouseBtn.Pressed)
						{
							TeleportCameraToMinimapPos(rMouseBtn.Position);
							_isLeftClickDragging = true;
						}
						else
						{
							_isLeftClickDragging = false;
						}
					}
					else if (rMouseBtn.ButtonIndex == MouseButton.Right)
					{
						if (rMouseBtn.Pressed)
						{
							TeleportCameraToMinimapPos(rMouseBtn.Position);
							_isRightClickPanning = true;
						}
						else
						{
							_isRightClickPanning = false;
						}
					}
				}
				else if (@event is InputEventMouseMotion rMouseMotion)
				{
					if (_isLeftClickDragging || _isRightClickPanning)
					{
						TeleportCameraToMinimapPos(rMouseMotion.Position);
					}
				}
				return;
			}

			if (@event is InputEventMouseButton mouseBtn)
			{
				if (mouseBtn.ButtonIndex == MouseButton.Left)
				{
					if (mouseBtn.Pressed)
					{
						var minimapWorldPos = MinimapHelper.MinimapToWorld(mouseBtn.Position, _minimapArea.Size);

						if (mouseBtn.AltPressed)
						{
							if (GameHost.Instance != null)
							{
								if (GameHost.Instance.Multiplayer.MultiplayerPeer != null)
								{
									GameHost.Instance.Rpc("NetworkPingMinimap", minimapWorldPos);
								}
								else
								{
									GameHost.Instance.AddMinimapPing(minimapWorldPos);
								}
							}
							return;
						}

						if (GameHost.Instance != null)
						{
							if (GameHost.Instance.ActivePingMode)
							{
								GameHost.Instance.AddMinimapPing(minimapWorldPos);
								GameHost.Instance.ActivePingMode = false;
							}
							else if (GameHost.Instance.ActiveCommandTargeting != null)
							{
								string cmd = GameHost.Instance.ActiveCommandTargeting;
								if (cmd == "attack")
								{
									GameHost.Instance.IssueAttackMoveCommand(minimapWorldPos);
								}
								else if (cmd == "move")
								{
									if (Input.IsKeyPressed(Key.Shift))
										GameHost.Instance.IssueMoveCommand(minimapWorldPos, true);
									else
										GameHost.Instance.IssueMoveCommand(minimapWorldPos);
								}
								else if (cmd == "patrol")
								{
									GameHost.Instance.IssuePatrolCommand(minimapWorldPos);
								}
								else if (cmd == "rally")
								{
									if (GameHost.Instance.SelectedUnits.Count == 1 && 
										!GameHost.Instance.SelectedUnits[0].IsEnemy && 
										GameHost.Instance.SelectedUnits[0].IsBuilding)
									{
										GameHost.Instance.SetRallyPoint(GameHost.Instance.SelectedUnits[0], minimapWorldPos);
									}
								}
								GameHost.Instance.ClearTargetingModes();
							}
							else if (GameHost.Instance.ActiveSpellTargeting != null)
							{
								GameHost.Instance.CastSpellAt(GameHost.Instance.ActiveSpellTargeting, minimapWorldPos);
								GameHost.Instance.ClearTargetingModes();
							}
							else if (GameHost.Instance.ActiveBuildingPlacementType != null)
							{
								GameHost.Instance.PlaceBuildingAt(GameHost.Instance.ActiveBuildingPlacementType, minimapWorldPos);
								GameHost.Instance.ClearTargetingModes();
							}
							else
							{
								TeleportCameraToMinimapPos(mouseBtn.Position);
								_isLeftClickDragging = true;
							}
						}
					}
					else
					{
						_isLeftClickDragging = false;
					}
				}
				else if (mouseBtn.ButtonIndex == MouseButton.Right)
				{
					if (mouseBtn.Pressed)
					{
						var minimapWorldPos = MinimapHelper.MinimapToWorld(mouseBtn.Position, _minimapArea.Size);

						if (GameHost.Instance != null)
						{
							GameHost.Instance.HandleMinimapRightClick(minimapWorldPos);
						}
					}
				}
			}
			else if (@event is InputEventMouseMotion mouseMotion)
			{
				if (_isLeftClickDragging || _isRightClickPanning)
				{
					TeleportCameraToMinimapPos(mouseMotion.Position);
				}
			}
		};
	}

	public void TeleportCameraToMinimapPos(Vector2 clickPos)
	{
		if (_minimapArea == null) return;
		var minimapWorldPos = MinimapHelper.MinimapToWorld(clickPos, _minimapArea.Size);

		if (_camera3D != null && GodotObject.IsInstanceValid(_camera3D))
		{
			_camera3D.GlobalPosition = new Vector3(minimapWorldPos.X, _camera3D.GlobalPosition.Y, minimapWorldPos.Z);
			InGameHUD.Instance?.ShowFeedbackText(string.Format(TranslationServer.Translate("Panned Camera on Minimap to: {0:F0}, {1:F0}"), minimapWorldPos.X, minimapWorldPos.Z), new Color(1, 0.85f, 0.5f));
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
