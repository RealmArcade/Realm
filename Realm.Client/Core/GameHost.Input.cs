using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Realm.Ecs;
using Godot;
using Godot.Collections;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Array = System.Array;
using Realm.Shared.Abilities;

namespace Realm.Client.Core;

public partial class GameHost
{
	/// <summary>
	/// The HUD this host reports through and, for the resource deductions below, reads and
	/// writes game state through. It exists for the lifetime of a match, so reaching this
	/// without one is a programming error rather than a state to survive.
	/// </summary>
	private static InGameHUD Hud => GameContext.InGameHud
		?? throw new InvalidOperationException("GameHost used the HUD before it existed.");

	private PhysicsRayQueryParameters3D? _cachedRaycastQuery { get; set; }
	private bool _leftClickInitiatedOverUI { get; set; } = false;
	private bool _is3DLeftClickDown { get; set; } = false;
	private Vector2 _leftClick3DStartPos { get; set; } = Vector2.Zero;
	private bool _is3DDragOperationActive { get; set; } = false;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (GameContext.IsSettingsMenuOpen)
		{
			if (@event is InputEventKey settingsToggleEvent && settingsToggleEvent.Pressed && !settingsToggleEvent.Echo && settingsToggleEvent.Keycode == Key.F12)
			{
				GameContext.Settings?.CancelSettings();
			}

			GetViewport().SetInputAsHandled();
			return;
		}

		HandleGlobalMouseLeftClick(@event);

		if (GameContext.InGameHud != null && Hud.IsChatActive) return;

		if (GameContext.SessionState.IsMapEditorMode)
		{
			HandleMapEditorInput(@event);
		}
		else
		{
			HandleGameplayInput(@event);
		}
	}

	private void HandleGlobalMouseLeftClick(InputEvent @event)
	{
		if (@event is InputEventMouseButton globalMb && globalMb.ButtonIndex == MouseButton.Left)
		{
			if (globalMb.Pressed)
			{
				if (IsMouseOverUI())
				{
					_leftClickInitiatedOverUI = true;
				}
				else
				{
					_leftClickInitiatedOverUI = false;
					if (GameContext.SessionState.IsMapEditorMode && !FloatingDialogBase.HasAnyDialogOpen)
					{
						_is3DLeftClickDown = true;
						_leftClick3DStartPos = globalMb.Position;
					}
				}
			}
			else
			{
				_leftClickInitiatedOverUI = false;
				_is3DLeftClickDown = false;
				if (GameContext.SessionState.IsMapEditorMode && _is3DDragOperationActive)
				{
					_is3DDragOperationActive = false;
					GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(false);
				}
			}
		}
	}

	private void HandleMapEditorInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion editorMm)
		{
			if (_is3DLeftClickDown && !_is3DDragOperationActive)
			{
				if (editorMm.Position.DistanceTo(_leftClick3DStartPos) > 3.0f)
				{
					_is3DDragOperationActive = true;
					GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(true);
				}
			}

			if (ActiveEditorTool == EditorTool.Ramp
			    && _rampPressActive
			    && editorMm.Position.DistanceTo(_rampPressScreenPosition) > RampDragScreenThreshold) _rampPressDragging = true;

			UpdateRampHoverPreview(editorMm.Position);

			if (_is3DDragOperationActive
			    && ActiveEditorTool == EditorTool.SelectMove
			    && !_isDraggingObject
			    && GameContext.EditorService.Interaction.ObjectMarqueeStart != null
			    && !GameContext.EditorService.Interaction.IsObjectMarqueeSelecting)
			{
				Vector2I marqueeStart = GameContext.EditorService.Interaction.ObjectMarqueeStart.Value;
				GameContext.EditorService.Interaction.SetObjectMarquee(marqueeStart, marqueeStart, true);
				UpdateEditorObjectMarqueeVisual();
			}
		}

		if (@event is InputEventKey editorKeyEvent && editorKeyEvent.Pressed && !editorKeyEvent.Echo)
			HandleEditorKeyInput(editorKeyEvent);

		if (@event is InputEventMouseButton wheelBtn && wheelBtn.Pressed && (wheelBtn.ButtonIndex == MouseButton.WheelUp || wheelBtn.ButtonIndex == MouseButton.WheelDown))
		{
			bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
			bool shiftPressed = Input.IsKeyPressed(Key.Shift);
			bool altPressed = Input.IsKeyPressed(Key.Alt);
			bool isUp = wheelBtn.ButtonIndex == MouseButton.WheelUp;

			bool isTerrainTool = ActiveEditorTool == EditorTool.Raise ||
			                     ActiveEditorTool == EditorTool.Lower ||
			                     ActiveEditorTool == EditorTool.Height ||
			                     ActiveEditorTool == EditorTool.Smooth ||
			                     ActiveEditorTool == EditorTool.Plateau ||
			                     ActiveEditorTool == EditorTool.PaintTexture ||
			                     ActiveEditorTool == EditorTool.Noise ||
			                     ActiveEditorTool == EditorTool.Ramp ||
			                     ActiveEditorTool == EditorTool.PlacePropClump ||
			                     ActiveEditorTool == EditorTool.PaintPathing;

			if (isTerrainTool)
			{
				if (shiftPressed)
				{
					float deltaSize = isUp ? 1.0f : -1.0f;
					EditorBrushRadius = Mathf.Clamp(EditorBrushRadius + deltaSize, EditorToolSettings.MinBrushRadius, EditorToolSettings.MaxBrushRadius);
					GameContext.MapEditorHud?.UpdateBrushSizeExternal(EditorBrushRadius);
					GetViewport().SetInputAsHandled();
					return;
				}

				if (ctrlPressed)
				{
					float deltaStr = isUp ? 0.5f : -0.5f;
					EditorBrushStrength = Mathf.Clamp(EditorBrushStrength + deltaStr, EditorToolSettings.MinBrushStrength, EditorToolSettings.MaxBrushStrength);
					GameContext.MapEditorHud?.UpdateBrushStrengthExternal(EditorBrushStrength);
					GetViewport().SetInputAsHandled();
					return;
				}

				if (altPressed && (ActiveEditorTool == EditorTool.Raise || ActiveEditorTool == EditorTool.Lower))
				{
					GameContext.MapEditorHud?.StepBlockLevelHeightExternal(isUp ? 1 : -1);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (shiftPressed)
			{
				float rotDelta = isUp ? 15.0f : -15.0f;
				if (ActiveEditorTool == EditorTool.SelectMove && SelectedEditorObjectCount > 0)
				{
					RotateSelectedObject(rotDelta);
					GameContext.MapEditorHud?.UpdateSelectedObjectInfo();
				}
				else
				{
					GameContext.EditorService.Tools.PlacementRotation = (GameContext.EditorService.Tools.PlacementRotation + rotDelta + 360.0f) % 360.0f;
					GameContext.MapEditorHud?.UpdateRotationExternal(GameContext.EditorService.Tools.PlacementRotation);
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			if (altPressed)
			{
				float scaleDelta = isUp ? 0.1f : -0.1f;
				if (ActiveEditorTool == EditorTool.SelectMove && SelectedEditorObjectCount > 0)
				{
					ScaleSelectedObject(scaleDelta);
					GameContext.MapEditorHud?.UpdateSelectedObjectInfo();
				}
				else
				{
					EditorPlacementScale = Mathf.Clamp(EditorPlacementScale + scaleDelta, EditorToolSettings.MinPlacementScale, EditorToolSettings.MaxPlacementScale);
					GameContext.MapEditorHud?.UpdateScaleExternal(EditorPlacementScale);
				}

				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventMouseButton editorRightMouseBtn && editorRightMouseBtn.Pressed && editorRightMouseBtn.ButtonIndex == MouseButton.Right)
		{
			if (IsMouseOverUI()) return;
			if (MainCamera is CameraControl camCtrl && camCtrl.IsFreeCamera)
			{
				return;
			}

			if (GroundTerrain != null && (EditorMirrorMode != MirrorMode.None || GameContext.EditorService.Tools.PolarOverlayVisible))
			{
				var terrainHit = RaycastTerrainFromMouse(editorRightMouseBtn.Position);
				if (terrainHit != null && terrainHit.ContainsKey("position"))
				{
					Vector3 hitPos = terrainHit["position"].AsVector3();
					var pivot = new Vector2(hitPos.X, hitPos.Z);
					EditorSymmetryPivot = pivot;
					GroundTerrain.SetPolarCenter(pivot);
					UpdateSymmetryPivotVisuals();
					InvalidateSelectionHighlightMesh();
					var (cx, cz) = EditorGridSnapping.WorldPosToCellCoords(hitPos);
					GameContext.MapEditorHud?.ShowFeedbackExternal($"Pivot set to tile ({cx}, {cz})");
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (GameContext.EditorService.RampStartPos != null)
			{
				GameContext.EditorService.SetRampStartPos(null);
				ClearRampPreviewVisuals();
				GameContext.MapEditorHud?.ShowFeedbackExternal("Ramp Cancelled");
				GetViewport().SetInputAsHandled();
				return;
			}

			if (SelectedEditorObject != null)
			{
				SelectedEditorObject = null;
				GameContext.MapEditorHud?.ShowFeedbackExternal("Deselected Object");
				GetViewport().SetInputAsHandled();
				return;
			}

			if (ActiveEditorTool == EditorTool.Measure)
			{
				ClearMeasureVisuals();
				GameContext.MapEditorHud?.ClearMeasureTelemetry();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (ActiveEditorTool == EditorTool.PasteArea)
			{
				ActiveEditorTool = EditorTool.SelectArea;
				GameContext.MapEditorHud?.SelectToolFromHotkey(EditorTool.SelectArea);
				HideSelectionHighlight();
				GameContext.MapEditorHud?.ClearPasteTelemetry();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventMouseButton releaseEvent && !releaseEvent.Pressed && releaseEvent.ButtonIndex == MouseButton.Left)
		{
			if (ActiveEditorTool == EditorTool.Ramp && _rampPressActive)
			{
				FinishRampInteraction(releaseEvent.Position);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (_is3DDragOperationActive)
			{
				_is3DDragOperationActive = false;
				_is3DLeftClickDown = false;
				GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(false);
			}

			if (FloatingDialogBase.HasAnyDialogOpen)
			{
				return;
			}

			if (GameContext.EditorService.Interaction.IsObjectMarqueeSelecting)
			{
				FinalizeEditorObjectMarquee();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (GameContext.EditorService.Interaction.ObjectMarqueeStart != null)
			{
				GameContext.EditorService.Interaction.ClearObjectMarquee();
				HideEditorObjectMarquee();
			}

			if (GameContext.EditorService.Interaction.IsSelectingArea)
			{
				GameContext.EditorService.Interaction.SetIsSelectingArea(false);
				if (ActiveEditorTool == EditorTool.DrawCoordinate && GameContext.EditorService.Interaction.SelectionStart != null && GameContext.EditorService.Interaction.SelectionEnd != null)
				{
					int rMinX = Mathf.Min(GameContext.EditorService.Interaction.SelectionStart.Value.X, GameContext.EditorService.Interaction.SelectionEnd.Value.X);
					int rMinZ = Mathf.Min(GameContext.EditorService.Interaction.SelectionStart.Value.Y, GameContext.EditorService.Interaction.SelectionEnd.Value.Y);
					int rMaxX = Mathf.Max(GameContext.EditorService.Interaction.SelectionStart.Value.X, GameContext.EditorService.Interaction.SelectionEnd.Value.X);
					int rMaxZ = Mathf.Max(GameContext.EditorService.Interaction.SelectionStart.Value.Y, GameContext.EditorService.Interaction.SelectionEnd.Value.Y);
					GameContext.MapEditorHud?.OpenCoordinateNamingPanel(rMinX, rMinZ, rMaxX, rMaxZ);
				}

				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventMouseButton editorMouseBtn && editorMouseBtn.Pressed && editorMouseBtn.ButtonIndex == MouseButton.Left)
			HandleEditorLeftClick(editorMouseBtn);

		return;
	}

	private bool TryCancelActiveConfirmationOverlay()
	{
		var userInterface = GameContext.UserInterface;
		if (userInterface == null) return false;

		for (int index = userInterface.GetChildCount() - 1; index >= 0; index--)
		{
			if (userInterface.GetChild(index) is not Control overlay || overlay.Name != "ConfirmationOverlay" || overlay.IsQueuedForDeletion()) continue;
			if (!overlay.HasMeta("CancelAction")) return false;

			overlay.GetMeta("CancelAction").AsCallable().Call();
			return true;
		}

		return false;
	}
}
