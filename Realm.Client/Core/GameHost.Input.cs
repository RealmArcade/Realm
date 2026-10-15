using System;
using System.Collections.Generic;
using Arch.Core;
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
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.MapAPI;
using Array = System.Array;

namespace Realm.Client.Core;

public partial class GameHost
{
	private bool TryGetPlacementParams(Vector3 hitPos, out float placementRot, out float scaleVal, out Vector3 spawnPos, out float radius)
	{
		placementRot = 0f;
		scaleVal = 1f;
		spawnPos = Vector3.Zero;
		radius = 0f;
		if (EditorClumpMode) return false;
		if (!GameContext.EditorService.HasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
		placementRot = (GameContext.EditorService.EditorRandomRotation && !GameContext.EditorService.IsPastingObject) ? GameContext.EditorService.CachedRandomRotation : GameContext.EditorService.EditorPlacementRotation;
		scaleVal = (GameContext.EditorService.EditorRandomScale && !GameContext.EditorService.IsPastingObject) ? GameContext.EditorService.CachedRandomScale : EditorPlacementScale;
		
		var finalPos = CalculatePlacementPosition(hitPos, scaleVal, out radius);
		if (finalPos == null) return false;
		spawnPos = finalPos.Value;
		return true;
	}

	private Godot.Vector3? CalculatePlacementPosition(Godot.Vector3 hitPos, float scaleVal, out float radius)
	{
		Godot.Vector3 spawnPos = hitPos;
		spawnPos.Y = GetTerrainHeightAt(spawnPos);
		radius = GameContext.InputService.GetPlacementRadius(ActivePlaceId, scaleVal);
		var finalPos = FindNearestFreePosition(spawnPos, radius);
		if (finalPos == null)
		{
			if (scaleVal > 1.5f)
			{
				MapEditorHUD.Instance?.ShowFeedbackExternal($"Invalid location: The object (Scale: {scaleVal:F1}x) is too large to fit here.");
			}
			else
			{
				MapEditorHUD.Instance?.ShowFeedbackExternal("invalid location");
			}

			UIManager.Instance?.PlayWarningSound();
			GetViewport().SetInputAsHandled();
			return null;
		}
		return finalPos;
	}
	private PhysicsRayQueryParameters3D? _cachedRaycastQuery { get; set; }
	private bool _leftClickInitiatedOverUI { get; set; } = false;
	private bool _is3DLeftClickDown { get; set; } = false;
	private Vector2 _leftClick3DStartPos { get; set; } = Vector2.Zero;
	private bool _is3DDragOperationActive { get; set; } = false;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (SettingsMenu.IsOpen)
		{
			GetViewport().SetInputAsHandled();
			return;
		}

		HandleGlobalMouseLeftClick(@event);

		if (InGameHUD.Instance != null && InGameHUD.Instance.IsChatActive) return;

		if (GameContext.EditorService.IsMapEditorMode)
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
					if (GameContext.EditorService.IsMapEditorMode && !FloatingDialogBase.HasAnyDialogOpen)
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
				if (GameContext.EditorService.IsMapEditorMode && _is3DDragOperationActive)
				{
					_is3DDragOperationActive = false;
					MapEditorHUD.Instance?.Set3DInteractionActive_ViewLogic(false);
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
					MapEditorHUD.Instance?.Set3DInteractionActive_ViewLogic(true);
				}
			}
		}

		if (@event is InputEventKey editorKeyEvent && editorKeyEvent.Pressed && !editorKeyEvent.Echo)
		{
			bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
			bool shiftPressed = Input.IsKeyPressed(Key.Shift);

			if (editorKeyEvent.Keycode == Key.Escape)
			{
				if (ActiveEditorTool == EditorTool.Measure)
				{
					ClearMeasureVisuals();
					MapEditorHUD.Instance?.ClearMeasureTelemetry();
					GetViewport().SetInputAsHandled();
					return;
				}

				if (ActiveEditorTool == EditorTool.PasteArea)
				{
					ActiveEditorTool = EditorTool.SelectArea;
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectArea);
					HideSelectionHighlight();
					MapEditorHUD.Instance?.ClearPasteTelemetry();
					GetViewport().SetInputAsHandled();
					return;
				}

				if (_is3DDragOperationActive)
				{
					_is3DDragOperationActive = false;
					_is3DLeftClickDown = false;
					MapEditorHUD.Instance?.Set3DInteractionActive_ViewLogic(false);
				}

				if (GameContext.EditorService.RampStartPos != null)
				{
					GameContext.EditorService.SetRampStartPos(null);
					MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Cancelled");
					GetViewport().SetInputAsHandled();
					return;
				}

				if (SelectedEditorObject != null)
				{
					SelectedEditorObject = null;
					MapEditorHUD.Instance?.ShowFeedbackExternal("Deselected Object");
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (editorKeyEvent.Keycode == Key.Z && !ctrlPressed && !shiftPressed)
			{
				CycleCameraZoom();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.H && ctrlPressed)
			{
				if (MapEditorHUD.Instance != null)
				{
					MapEditorHUD.Instance.Visible = !MapEditorHUD.Instance.Visible;
					MapEditorHUD.Instance.ShowFeedbackExternal(MapEditorHUD.Instance.Visible ? "HUD: Visible" : "HUD: Hidden");
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.H && !ctrlPressed && !shiftPressed)
			{
				MapEditorHUD.Instance?.ToggleHelpPanelExternal();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.M && !ctrlPressed && !shiftPressed)
			{
				EditorBlockMode = !EditorBlockMode;
				MapEditorHUD.Instance?.UpdateBlockModeExternal(EditorBlockMode);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.Q && !ctrlPressed && !shiftPressed)
			{
				if (MainCamera is CameraControl camCtrl && camCtrl.IsFreeCamera)
				{
					return;
				}

				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectMove);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.I && !ctrlPressed && !shiftPressed)
			{
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.Eyedropper);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.Delete)
			{
				if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					var target = SelectedEditorObject;
					SelectedEditorObject = null;
					var action = DeleteObjectAtWithUndo(target, (target as Node3D).Position);
					if (action != null)
					{
						EditorHistoryManager.RecordAction(action);
						MapEditorHUD.Instance?.ShowFeedbackExternal("Deleted Object");
						EditorHasUnsavedChanges = true;
					}

					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (editorKeyEvent.Keycode == Key.Z && ctrlPressed)
			{
				if (shiftPressed)
				{
					EditorHistoryManager.Redo();
					MapEditorHUD.Instance?.ShowFeedbackExternal("Redo Action performed");
				}
				else
				{
					EditorHistoryManager.Undo();
					MapEditorHUD.Instance?.ShowFeedbackExternal("Undo Action performed");
				}

				EditorHasUnsavedChanges = true;
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.Y && ctrlPressed)
			{
				EditorHistoryManager.Redo();
				MapEditorHUD.Instance?.ShowFeedbackExternal("Redo Action performed");
				EditorHasUnsavedChanges = true;
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.S && ctrlPressed)
			{
				MapEditorHUD.Instance?.SaveMapActionExternal();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.O && ctrlPressed)
			{
				MapEditorHUD.Instance?.LoadMapAction();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.P && ctrlPressed)
			{
				SaveMapToFile();
				MapEditorHUD.Instance?.ShowFeedbackExternal("Map published & compiled!");
				GetViewport().SetInputAsHandled();
				return;
			}

			if ((editorKeyEvent.Keycode == Key.O || editorKeyEvent.Keycode == Key.V) && !ctrlPressed && !shiftPressed)
			{
				GameContext.EditorService.EditorGridMode = GameContext.EditorService.EditorGridMode switch
				{
					GridOverlayMode.Off => GridOverlayMode.Grid,
					GridOverlayMode.Grid => GridOverlayMode.Polar,
					GridOverlayMode.Polar => GridOverlayMode.Both,
					GridOverlayMode.Both => GridOverlayMode.Off,
					_ => GridOverlayMode.Off
				};
				UpdateGridOverlayVisibility();
				MapEditorHUD.Instance?.UpdateGridOverlayExternal(GameContext.EditorService.EditorGridMode);
				string modeName = GameContext.EditorService.EditorGridMode switch
				{
					GridOverlayMode.Off => "OFF",
					GridOverlayMode.Grid => "GRID",
					GridOverlayMode.Polar => "POLAR",
					GridOverlayMode.Both => "GRID + POLAR",
					_ => "OFF"
				};
				MapEditorHUD.Instance?.ShowFeedbackExternal($"Overlay Mode: {modeName}");
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.U && !ctrlPressed && !shiftPressed)
			{
				ActiveEditorTool = ActiveEditorTool == EditorTool.Measure ? EditorTool.None : EditorTool.Measure;
				MapEditorHUD.Instance?.SelectToolFromHotkey(ActiveEditorTool);
				if (ActiveEditorTool != EditorTool.Measure)
				{
					ClearMeasureVisuals();
					MapEditorHUD.Instance?.ClearMeasureTelemetry();
				}
				else
				{
					MapEditorHUD.Instance?.ShowFeedbackExternal("Tape Measure Active - Click Point A then Point B");
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.G && ctrlPressed)
			{
				EditorSnapToGrid = !EditorSnapToGrid;
				MapEditorHUD.Instance?.UpdateGridSnapExternal(EditorSnapToGrid);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.C && !ctrlPressed && !shiftPressed)
			{
				if (MainCamera is CameraControl camCtrl && camCtrl.IsFreeCamera)
				{
					return;
				}

				var cam = MainCamera;
				if (cam != null)
				{
					cam.ToggleTopDown();
					bool topDown = cam.IsTopDown();
					MapEditorHUD.Instance?.UpdateCameraAngleButtonText(topDown);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (editorKeyEvent.Keycode == Key.C && ctrlPressed)
			{
				if (ActiveEditorTool == EditorTool.SelectArea)
				{
					PerformCopyArea();
					GetViewport().SetInputAsHandled();
					return;
				}

				if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					if (SelectedEditorObject is Unit3D unit)
					{
						GameContext.EditorService.SetCopiedObject(new CopiedObjectTemplate
						{
							Type = "unit",
							Id = unit.UnitId,
							Rotation = unit.RotationDegrees.Y,
							Scale = unit.Scale.X,
							IsEnemy = unit.IsEnemy
						});
						MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Unit: {unit.UnitId.ToUpper()}");
					}
					else if (SelectedEditorObject is Prop3D prop)
					{
						GameContext.EditorService.SetCopiedObject(new CopiedObjectTemplate
						{
							Type = "prop",
							Id = prop.PropId,
							Rotation = prop.RotationDegrees.Y,
							Scale = prop.Scale.X,
							IsEnemy = false
						});
						MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Prop: {prop.PropId.ToUpper()}");
					}
					else if (SelectedEditorObject is Decal decal)
					{
						string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
						GameContext.EditorService.SetCopiedObject(new CopiedObjectTemplate
						{
							Type = "decal",
							Id = decalId,
							Rotation = decal.RotationDegrees.Y,
							Scale = decal.Scale.X,
							IsEnemy = false
						});
						MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Decal: {decalId.ToUpper()}");
					}

					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (editorKeyEvent.Keycode == Key.V && ctrlPressed)
			{
				if (ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea)
				{
					if (GameContext.EditorService.HasCopiedArea)
					{
						ActiveEditorTool = EditorTool.PasteArea;
						MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PasteArea);
						MapEditorHUD.Instance?.ShowFeedbackExternal("Paste Mode Active - Click to paste");
						GetViewport().SetInputAsHandled();
						return;
					}
				}

				var copiedObj = GameContext.EditorService.GetCopiedObject();
				if (copiedObj != null)
				{
					var hit = RaycastTerrainFromMouse(GetViewport().GetMousePosition());
					if (hit != null && hit.ContainsKey("position"))
					{
						Vector3 spawnPos = hit["position"].AsVector3();
						if (EditorSnapToGrid && GroundTerrain != null)
						{
							float quadSize = GroundTerrain.QuadSize;
							int width = GroundTerrain.Width;
							int depth = GroundTerrain.Depth;
							float fx = Mathf.Round(spawnPos.X / quadSize + (width - 1) / 2.0f);
							spawnPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * quadSize;
							float fz = Mathf.Round(spawnPos.Z / quadSize + (depth - 1) / 2.0f);
							spawnPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * quadSize;
						}

						spawnPos.Y = GetTerrainHeightAt(spawnPos);
						var cop = copiedObj.Value;
						Node pastedNode = null;
						IEditorAction action = null;
						if (cop.Type == "unit")
						{
							pastedNode = SpawnUnitExternal(cop.Id, spawnPos, cop.IsEnemy, cop.Rotation, cop.Scale);
							if (pastedNode != null)
							{
								action = new ObjectSpawnAction("unit", cop.Id, spawnPos, cop.Rotation, cop.Scale, cop.IsEnemy, pastedNode);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Pasted Unit: {cop.Id.ToUpper()}");
							}
						}
						else if (cop.Type == "prop")
						{
							pastedNode = SpawnPropExternalWithParams(cop.Id, spawnPos, cop.Rotation, cop.Scale);
							if (pastedNode != null)
							{
								action = new ObjectSpawnAction("prop", cop.Id, spawnPos, cop.Rotation, cop.Scale, false, pastedNode);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Pasted Prop: {cop.Id.ToUpper()}");
							}
						}
						else if (cop.Type == "decal")
						{
							pastedNode = SpawnDecalExternalWithParams(cop.Id, spawnPos, cop.Rotation, cop.Scale);
							if (pastedNode != null)
							{
								action = new ObjectSpawnAction("decal", cop.Id, spawnPos, cop.Rotation, cop.Scale, false, pastedNode);
								MapEditorHUD.Instance?.ShowFeedbackExternal($"Pasted Decal: {cop.Id.ToUpper()}");
							}
						}

						if (action != null)
						{
							if (EditorMirrorMode != MirrorMode.None)
							{
								var actionsList = new List<IEditorAction> { action };
								foreach (var t in GetMirroredTransforms(spawnPos, cop.Rotation))
								{
									Vector3 mPos = t.Position;
									mPos.Y = GetTerrainHeightAt(mPos);
									Node mNode = null;
									if (cop.Type == "unit")
									{
										mNode = SpawnUnitExternal(cop.Id, mPos, cop.IsEnemy, t.Rotation, cop.Scale);
										if (mNode != null)
										{
											actionsList.Add(new ObjectSpawnAction("unit", cop.Id, mPos, t.Rotation, cop.Scale, cop.IsEnemy, mNode));
										}
									}
									else if (cop.Type == "prop")
									{
										mNode = SpawnPropExternalWithParams(cop.Id, mPos, t.Rotation, cop.Scale);
										if (mNode != null)
										{
											actionsList.Add(new ObjectSpawnAction("prop", cop.Id, mPos, t.Rotation, cop.Scale, false, mNode));
										}
									}
									else if (cop.Type == "decal")
									{
										mNode = SpawnDecalExternalWithParams(cop.Id, mPos, t.Rotation, cop.Scale);
										if (mNode != null)
										{
											actionsList.Add(new ObjectSpawnAction("decal", cop.Id, mPos, t.Rotation, cop.Scale, false, mNode));
										}
									}
								}

								var composite = new CompositeAction(actionsList);
								EditorHistoryManager.RecordAction(composite);
							}
							else
							{
								EditorHistoryManager.RecordAction(action);
							}

							SelectedEditorObject = pastedNode;
							EditorHasUnsavedChanges = true;
						}
					}

					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (editorKeyEvent.Keycode == Key.D && ctrlPressed)
			{
				if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					Node3D selectedNode = SelectedEditorObject as Node3D;
					Vector3 spawnPos;
					var hit = RaycastTerrainFromMouse(GetViewport().GetMousePosition());
					if (hit != null && hit.ContainsKey("position"))
					{
						spawnPos = hit["position"].AsVector3();
						if (EditorSnapToGrid && GroundTerrain != null)
						{
							float quadSize = GroundTerrain.QuadSize;
							int width = GroundTerrain.Width;
							int depth = GroundTerrain.Depth;
							float fx = Mathf.Round(spawnPos.X / quadSize + (width - 1) / 2.0f);
							spawnPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * quadSize;
							float fz = Mathf.Round(spawnPos.Z / quadSize + (depth - 1) / 2.0f);
							spawnPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * quadSize;
						}
					}
					else
					{
						spawnPos = selectedNode.Position + new Vector3(2.0f, 0.0f, 2.0f);
					}

					spawnPos.Y = GetTerrainHeightAt(spawnPos);
					float rotY = selectedNode.RotationDegrees.Y;
					float scaleVal = selectedNode.Scale.X;
					Node clonedNode = null;
					IEditorAction action = null;
					if (SelectedEditorObject is Unit3D unit)
					{
						clonedNode = SpawnUnitExternal(unit.UnitId, spawnPos, unit.IsEnemy, rotY, scaleVal);
						if (clonedNode != null)
						{
							action = new ObjectSpawnAction("unit", unit.UnitId, spawnPos, rotY, scaleVal, unit.IsEnemy, clonedNode);
							MapEditorHUD.Instance?.ShowFeedbackExternal($"Duplicated Unit: {unit.UnitId.ToUpper()}");
						}
					}
					else if (SelectedEditorObject is Prop3D prop)
					{
						clonedNode = SpawnPropExternalWithParams(prop.PropId, spawnPos, rotY, scaleVal);
						if (clonedNode != null)
						{
							action = new ObjectSpawnAction("prop", prop.PropId, spawnPos, rotY, scaleVal, false, clonedNode);
							MapEditorHUD.Instance?.ShowFeedbackExternal($"Duplicated Prop: {prop.PropId.ToUpper()}");
						}
					}
					else if (SelectedEditorObject is Decal decal)
					{
						string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
						clonedNode = SpawnDecalExternalWithParams(decalId, spawnPos, rotY, scaleVal);
						if (clonedNode != null)
						{
							action = new ObjectSpawnAction("decal", decalId, spawnPos, rotY, scaleVal, false, clonedNode);
							MapEditorHUD.Instance?.ShowFeedbackExternal($"Duplicated Decal: {decalId.ToUpper()}");
						}
					}

					if (action != null)
					{
						EditorHistoryManager.RecordAction(action);
						SelectedEditorObject = clonedNode;
						EditorHasUnsavedChanges = true;
					}

					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (editorKeyEvent.Keycode == Key.Bracketleft)
			{
				EditorBrushRadius = Mathf.Max(EditorService.MIN_BRUSH_RADIUS, EditorBrushRadius - 1.0f);
				MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.Bracketright)
			{
				EditorBrushRadius = Mathf.Min(EditorService.MAX_BRUSH_RADIUS, EditorBrushRadius + 1.0f);
				MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
				GetViewport().SetInputAsHandled();
				return;
			}

			bool isNumpadNudge = editorKeyEvent.Keycode == Key.Kp1 ||
			                     editorKeyEvent.Keycode == Key.Kp2 ||
			                     editorKeyEvent.Keycode == Key.Kp3 ||
			                     editorKeyEvent.Keycode == Key.Kp4 ||
			                     editorKeyEvent.Keycode == Key.Kp6 ||
			                     editorKeyEvent.Keycode == Key.Kp7 ||
			                     editorKeyEvent.Keycode == Key.Kp8 ||
			                     editorKeyEvent.Keycode == Key.Kp9;

			if (isNumpadNudge)
			{
				if (GodotObject.IsInstanceValid(SelectedEditorObject) && SelectedEditorObject is Node3D node3D)
				{
					Vector3 nudgeDir = Vector3.Zero;
					if (editorKeyEvent.Keycode == Key.Kp8) nudgeDir = new Vector3(0, 0, -1);
					else if (editorKeyEvent.Keycode == Key.Kp2) nudgeDir = new Vector3(0, 0, 1);
					else if (editorKeyEvent.Keycode == Key.Kp4) nudgeDir = new Vector3(-1, 0, 0);
					else if (editorKeyEvent.Keycode == Key.Kp6) nudgeDir = new Vector3(1, 0, 0);
					else if (editorKeyEvent.Keycode == Key.Kp7) nudgeDir = new Vector3(-1, 0, -1).Normalized();
					else if (editorKeyEvent.Keycode == Key.Kp9) nudgeDir = new Vector3(1, 0, -1).Normalized();
					else if (editorKeyEvent.Keycode == Key.Kp1) nudgeDir = new Vector3(-1, 0, 1).Normalized();
					else if (editorKeyEvent.Keycode == Key.Kp3) nudgeDir = new Vector3(1, 0, 1).Normalized();

					float nudgeDistance = 1.0f;
					Vector3 targetPos = node3D.Position + nudgeDir * nudgeDistance;

					bool valid = true;
					if (GroundTerrain != null)
					{
						float quadSize = GroundTerrain.QuadSize;
						int width = GroundTerrain.Width;
						int depth = GroundTerrain.Depth;
						float halfW = (width - 1) / 2.0f * quadSize;
						float halfD = (depth - 1) / 2.0f * quadSize;
						if (Mathf.Abs(targetPos.X) > halfW || Mathf.Abs(targetPos.Z) > halfD)
						{
							valid = false;
						}
					}

					if (valid)
					{
						float radius = 1.0f;
						if (node3D is Unit3D u) radius = GameContext.InputService.GetPlacementRadius(u.UnitId, u.Scale.X);
						else if (node3D is Prop3D p) radius = GameContext.InputService.GetPlacementRadius(p.PropId, p.Scale.X);

						if (IsPositionBlocked(targetPos, radius, node3D))
						{
							valid = false;
						}
					}

					if (valid)
					{
						targetPos.Y = GetTerrainHeightAt(targetPos);
						bool isUnit = node3D is Unit3D;
						bool isEnemy = isUnit ? (node3D as Unit3D).IsEnemy : false;
						var action = new ObjectTransformAction(
							node3D,
							node3D.Position, targetPos,
							node3D.RotationDegrees, node3D.RotationDegrees,
							node3D.Scale, node3D.Scale,
							isEnemy, isEnemy
						);
						node3D.Position = targetPos;
						if (node3D is Unit3D unit && GameContext.World.IsAlive(unit.Entity))
						{
							GameContext.InputService.SetEntityPosition(unit.Entity, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
						}
						else if (node3D is Prop3D prop)
						{
							PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
						}

						EditorHistoryManager.RecordAction(action);
						EditorHasUnsavedChanges = true;
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
						MapEditorHUD.Instance?.ShowFeedbackExternal("Object nudged");
					}
					else
					{
						UIManager.Instance?.PlayWarningSound();
					}
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.Minus)
			{
				EditorBrushStrength = Mathf.Max(EditorService.MIN_BRUSH_STRENGTH, EditorBrushStrength - 0.5f);
				MapEditorHUD.Instance?.UpdateBrushStrengthExternal(EditorBrushStrength);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.Equal)
			{
				EditorBrushStrength = Mathf.Min(EditorService.MAX_BRUSH_STRENGTH, EditorBrushStrength + 0.5f);
				MapEditorHUD.Instance?.UpdateBrushStrengthExternal(EditorBrushStrength);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode >= Key.Key1 && editorKeyEvent.Keycode <= Key.Key9)
			{
				int toolIndex = (int)(editorKeyEvent.Keycode - Key.Key1);
				EditorTool targetTool = toolIndex switch
				{
					0 => EditorTool.Raise,
					1 => EditorTool.Lower,
					2 => EditorTool.Height,
					3 => EditorTool.Smooth,
					4 => EditorTool.Plateau,
					5 => EditorTool.Ramp,
					6 => EditorTool.Noise,
					7 => EditorTool.PaintTexture,
					8 => EditorTool.PlaceProp,
					_ => EditorTool.None
				};
				if (targetTool != EditorTool.None)
				{
					MapEditorHUD.Instance?.SelectToolFromHotkey(targetTool);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (editorKeyEvent.Keycode == Key.B && !ctrlPressed && !shiftPressed)
			{
				EditorBrushIsSquare = !EditorBrushIsSquare;
				UpdateBrushMesh();
				MapEditorHUD.Instance?.UpdateBrushShapeExternal(EditorBrushIsSquare);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.T && !ctrlPressed && !shiftPressed)
			{
				GenerateNewRandomPlacementRotationAndScale();
				MapEditorHUD.Instance?.ShowFeedbackExternal("Re-randomized Rotation & Scale");
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.L && !ctrlPressed && !shiftPressed)
			{
				var res = CycleTimeOfDay();
				string timeName = GameContext.EnvironmentService?.GetTimeOfDayName(res.TimeOfDayIndex) ?? "Day";
				string icon = res.TimeOfDayIndex switch
				{
					0 => "☀️",
					1 => "🌅",
					2 => "🌙",
					3 => "🌄",
					_ => "☀️"
				};
				MapEditorHUD.Instance?.UpdateEnvLightingSelection(res.TimeOfDayIndex);
				MapEditorHUD.Instance?.ShowFeedbackExternal(string.Format(TranslationServer.Translate("Lighting: {0} {1}"), icon, TranslationServer.Translate(timeName)));
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.K && !ctrlPressed && !shiftPressed && !editorKeyEvent.AltPressed)
			{
				if (GameContext.EnvironmentService != null)
				{
					string nextWeather = GameContext.EnvironmentService.CycleWeather(this);
					string icon = nextWeather switch
					{
						"rain" => "🌧️",
						"snow" => "❄️",
						"fog" => "🌫️",
						_ => "☀️"
					};
					MapEditorHUD.Instance?.UpdateEnvWeatherSelection(nextWeather);
					MapEditorHUD.Instance?.ShowFeedbackExternal(string.Format(TranslationServer.Translate("Weather: {0} {1}"), icon, TranslationServer.Translate(nextWeather.Capitalize())));
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.F8 || (editorKeyEvent.Keycode == Key.Y && !ctrlPressed && !shiftPressed && !editorKeyEvent.AltPressed))
			{
				MapEditorHUD.Instance?.ToggleFreeCamera();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (editorKeyEvent.Keycode == Key.F9)
			{
				MapEditorHUD.Instance?.ToggleShadows();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

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
					EditorBrushRadius = Mathf.Clamp(EditorBrushRadius + deltaSize, EditorService.MIN_BRUSH_RADIUS, EditorService.MAX_BRUSH_RADIUS);
					MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
					GetViewport().SetInputAsHandled();
					return;
				}

				if (ctrlPressed)
				{
					float deltaStr = isUp ? 0.5f : -0.5f;
					EditorBrushStrength = Mathf.Clamp(EditorBrushStrength + deltaStr, EditorService.MIN_BRUSH_STRENGTH, EditorService.MAX_BRUSH_STRENGTH);
					MapEditorHUD.Instance?.UpdateBrushStrengthExternal(EditorBrushStrength);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (shiftPressed)
			{
				float rotDelta = isUp ? 15.0f : -15.0f;
				if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					var node3D = SelectedEditorObject as Node3D;
					Vector3 oldRot = node3D.RotationDegrees;
					Vector3 newRot = oldRot;
					newRot.Y = (newRot.Y + rotDelta + 360.0f) % 360.0f;
					bool isUnit = SelectedEditorObject is Unit3D;
					bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
					var action = new ObjectTransformAction(
						node3D,
						node3D.Position, node3D.Position,
						oldRot, newRot,
						node3D.Scale, node3D.Scale,
						isEnemy, isEnemy
					);
					node3D.RotationDegrees = newRot;
					EditorHistoryManager.RecordAction(action);
					MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
				}
				else
				{
					GameContext.EditorService.EditorPlacementRotation = (GameContext.EditorService.EditorPlacementRotation + rotDelta + 360.0f) % 360.0f;
					MapEditorHUD.Instance?.UpdateRotationExternal(GameContext.EditorService.EditorPlacementRotation);
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			if (altPressed)
			{
				float scaleDelta = isUp ? 0.1f : -0.1f;
				if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					var node3D = SelectedEditorObject as Node3D;
					Vector3 oldScale = node3D.Scale;
					float newScaleVal = Mathf.Clamp(oldScale.X + scaleDelta, 0.2f, 3.0f);
					Vector3 newScale = Vector3.One * newScaleVal;
					bool isUnit = SelectedEditorObject is Unit3D;
					bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
					var action = new ObjectTransformAction(
						node3D,
						node3D.Position, node3D.Position,
						node3D.RotationDegrees, node3D.RotationDegrees,
						oldScale, newScale,
						isEnemy, isEnemy
					);
					node3D.Scale = newScale;
					EditorHistoryManager.RecordAction(action);
					MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
				}
				else
				{
					EditorPlacementScale = Mathf.Clamp(EditorPlacementScale + scaleDelta, EditorService.MIN_PLACEMENT_SCALE, EditorService.MAX_PLACEMENT_SCALE);
					MapEditorHUD.Instance?.UpdateScaleExternal(EditorPlacementScale);
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

			if (GroundTerrain != null && GameContext.EditorService.EditorPolarOverlayVisible)
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
					var (cx, cz) = GameContext.EditorService.WorldPosToCellCoords(hitPos);
					MapEditorHUD.Instance?.ShowFeedbackExternal($"Pivot set to tile ({cx}, {cz})");
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (GameContext.EditorService.RampStartPos != null)
			{
				GameContext.EditorService.SetRampStartPos(null);
				MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Cancelled");
				GetViewport().SetInputAsHandled();
				return;
			}

			if (SelectedEditorObject != null)
			{
				SelectedEditorObject = null;
				MapEditorHUD.Instance?.ShowFeedbackExternal("Deselected Object");
				GetViewport().SetInputAsHandled();
				return;
			}

			if (ActiveEditorTool == EditorTool.Measure)
			{
				ClearMeasureVisuals();
				MapEditorHUD.Instance?.ClearMeasureTelemetry();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (ActiveEditorTool == EditorTool.PasteArea)
			{
				ActiveEditorTool = EditorTool.SelectArea;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectArea);
				HideSelectionHighlight();
				MapEditorHUD.Instance?.ClearPasteTelemetry();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (ActiveEditorTool != EditorTool.SelectMove)
			{
				ActiveEditorTool = EditorTool.SelectMove;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectMove);
				HideSelectionHighlight();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventMouseButton releaseEvent && !releaseEvent.Pressed && releaseEvent.ButtonIndex == MouseButton.Left)
		{
			if (_is3DDragOperationActive)
			{
				_is3DDragOperationActive = false;
				_is3DLeftClickDown = false;
				MapEditorHUD.Instance?.Set3DInteractionActive_ViewLogic(false);
			}

			if (FloatingDialogBase.HasAnyDialogOpen)
			{
				return;
			}

			if (GameContext.EditorService.IsSelectingArea)
			{
				GameContext.EditorService.SetIsSelectingArea(false);
				if (ActiveEditorTool == EditorTool.DrawCoordinate && GameContext.EditorService.SelectionStart != null && GameContext.EditorService.SelectionEnd != null)
				{
					int rMinX = Mathf.Min(GameContext.EditorService.SelectionStart.Value.X, GameContext.EditorService.SelectionEnd.Value.X);
					int rMinZ = Mathf.Min(GameContext.EditorService.SelectionStart.Value.Y, GameContext.EditorService.SelectionEnd.Value.Y);
					int rMaxX = Mathf.Max(GameContext.EditorService.SelectionStart.Value.X, GameContext.EditorService.SelectionEnd.Value.X);
					int rMaxZ = Mathf.Max(GameContext.EditorService.SelectionStart.Value.Y, GameContext.EditorService.SelectionEnd.Value.Y);
					MapEditorHUD.Instance?.OpenCoordinateNamingPanel(rMinX, rMinZ, rMaxX, rMaxZ);
				}

				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventMouseButton editorMouseBtn && editorMouseBtn.Pressed && editorMouseBtn.ButtonIndex == MouseButton.Left)
		{
			if (_leftClickInitiatedOverUI || IsMouseOverUI() || FloatingDialogBase.HasAnyDialogOpen)
			{
				return;
			}

			var terrainHit = RaycastTerrainFromMouse(editorMouseBtn.Position);
			var hit = RaycastFromMouse(editorMouseBtn.Position);
			if ((terrainHit != null && terrainHit.ContainsKey("position")) || (hit != null && hit.ContainsKey("position")))
			{
				Vector3 hitPos = (terrainHit != null && terrainHit.ContainsKey("position")) ? terrainHit["position"].AsVector3() : hit["position"].AsVector3();

				if (EditorSnapToGrid && GroundTerrain != null)
				{
					float quadSize = GroundTerrain.QuadSize;
					int width = GroundTerrain.Width;
					int depth = GroundTerrain.Depth;
					float fx = Mathf.Round(hitPos.X / quadSize + (width - 1) / 2.0f);
					hitPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * quadSize;
					float fz = Mathf.Round(hitPos.Z / quadSize + (depth - 1) / 2.0f);
					hitPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * quadSize;
				}

				if (ActiveEditorTool == EditorTool.PlaceUnit)
				{
					if (!TryGetPlacementParams(hitPos, out float placementRot, out float scaleVal, out Vector3 spawnPos, out float radius)) return;

					var unit = SpawnUnitExternal(ActivePlaceId, spawnPos, GameContext.EditorService.PlaceUnitIsEnemy, placementRot, scaleVal);
					if (unit != null)
					{
						var actions = new List<IEditorAction>
						{
							new ObjectSpawnAction("unit", ActivePlaceId, spawnPos, placementRot, scaleVal, GameContext.EditorService.PlaceUnitIsEnemy, unit)
						};
						if (EditorMirrorMode != MirrorMode.None)
						{
							foreach (var t in GetMirroredTransforms(spawnPos, placementRot))
							{
								Vector3 mPos = t.Position;
								mPos.Y = GetTerrainHeightAt(mPos);
								if (IsPositionBlocked(mPos, radius)) continue;
								var mUnit = SpawnUnitExternal(ActivePlaceId, mPos, GameContext.EditorService.PlaceUnitIsEnemy, t.Rotation, scaleVal);
								if (mUnit != null)
								{
									actions.Add(new ObjectSpawnAction("unit", ActivePlaceId, mPos, t.Rotation, scaleVal, GameContext.EditorService.PlaceUnitIsEnemy, mUnit));
								}
							}
						}

						var composite = new CompositeAction(actions);
						EditorHistoryManager.RecordAction(composite);
						EditorHasUnsavedChanges = true;
					}

					GenerateNewRandomPlacementRotationAndScale();
					GameContext.EditorService.SetIsPastingObject(false);
					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.PlaceProp)
				{
					if (!TryGetPlacementParams(hitPos, out float placementRot, out float scaleVal, out Vector3 spawnPos, out float radius)) return;

					var prop = SpawnPropExternalWithParams(ActivePlaceId, spawnPos, placementRot, scaleVal);
					if (prop != null)
					{
						var actions = new List<IEditorAction>
						{
							new ObjectSpawnAction("prop", ActivePlaceId, spawnPos, placementRot, scaleVal, false, prop)
						};
						if (EditorMirrorMode != MirrorMode.None)
						{
							foreach (var t in GetMirroredTransforms(spawnPos, placementRot))
							{
								Vector3 mPos = t.Position;
								mPos.Y = GetTerrainHeightAt(mPos);
								if (IsPositionBlocked(mPos, radius)) continue;
								var mProp = SpawnPropExternalWithParams(ActivePlaceId, mPos, t.Rotation, scaleVal);
								if (mProp != null)
								{
									actions.Add(new ObjectSpawnAction("prop", ActivePlaceId, mPos, t.Rotation, scaleVal, false, mProp));
								}
							}
						}

						var composite = new CompositeAction(actions);
						EditorHistoryManager.RecordAction(composite);
						EditorHasUnsavedChanges = true;
					}

					GenerateNewRandomPlacementRotationAndScale();
					GameContext.EditorService.SetIsPastingObject(false);
					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.PlaceDecal)
				{
					if (EditorClumpMode) return;
					if (!GameContext.EditorService.HasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
					float placementRot = (GameContext.EditorService.EditorRandomRotation && !GameContext.EditorService.IsPastingObject) ? GameContext.EditorService.CachedRandomRotation : GameContext.EditorService.EditorPlacementRotation;
					float scaleVal = (GameContext.EditorService.EditorRandomScale && !GameContext.EditorService.IsPastingObject) ? GameContext.EditorService.CachedRandomScale : EditorPlacementScale;

					Vector3 hitNormal = (terrainHit != null && terrainHit.ContainsKey("normal")) ? terrainHit["normal"].AsVector3() : (hit.ContainsKey("normal") ? hit["normal"].AsVector3() : GetTerrainNormalAt(hitPos));
					Basis alignedBasis = CreateAlignedBasis(hitNormal);
					alignedBasis = alignedBasis.Rotated(hitNormal, Mathf.DegToRad(placementRot));
					Vector3 spawnRot = alignedBasis.GetRotationQuaternion().GetEuler() * (180f / MathF.PI);

					var decal = SpawnDecalExternalWithParams(ActivePlaceId, hitPos, spawnRot, scaleVal);
					if (decal != null)
					{
						var actions = new List<IEditorAction>
						{
							new ObjectSpawnAction("decal", ActivePlaceId, hitPos, spawnRot, scaleVal, false, decal)
						};
						if (EditorMirrorMode != MirrorMode.None)
						{
							foreach (var t in GetMirroredTransforms(hitPos, placementRot))
							{
								Vector3 mPos = t.Position;
								Vector3 mNormal = GetTerrainNormalAt(mPos);
								Basis mBasis = CreateAlignedBasis(mNormal).Rotated(mNormal, Mathf.DegToRad(t.Rotation));
								Vector3 mRot = mBasis.GetRotationQuaternion().GetEuler() * (180f / MathF.PI);

								var mDecal = SpawnDecalExternalWithParams(ActivePlaceId, mPos, mRot, scaleVal);
								if (mDecal != null)
								{
									actions.Add(new ObjectSpawnAction("decal", ActivePlaceId, mPos, mRot, scaleVal, false, mDecal));
								}
							}
						}

						var composite = new CompositeAction(actions);
						EditorHistoryManager.RecordAction(composite);
						EditorHasUnsavedChanges = true;
					}

					GenerateNewRandomPlacementRotationAndScale();
					GameContext.EditorService.SetIsPastingObject(false);
					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.PlaceVfx)
				{
					if (EditorClumpMode) return;
					if (!GameContext.EditorService.HasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
					float placementRot = (GameContext.EditorService.EditorRandomRotation && !GameContext.EditorService.IsPastingObject) ? GameContext.EditorService.CachedRandomRotation : GameContext.EditorService.EditorPlacementRotation;
					float scaleVal = (GameContext.EditorService.EditorRandomScale && !GameContext.EditorService.IsPastingObject) ? GameContext.EditorService.CachedRandomScale : EditorPlacementScale;
					float safeScale = scaleVal <= 0.001f ? 1.0f : scaleVal;

					VfxAttachmentConfig config;
					if (GameContext.RegistryService.VfxRegistry.TryGetValue(ActivePlaceId, out var regCfg))
					{
						config = regCfg.Clone();
					}
					else if (Enum.TryParse<VfxPrimitiveType>(ActivePlaceId, true, out var primType))
					{
						config = new VfxAttachmentConfig { VfxId = ActivePlaceId, PrimitiveType = primType };
					}
					else
					{
						config = new VfxAttachmentConfig { VfxId = ActivePlaceId, Name = ActivePlaceId };
					}

					Vector3 spawnPos = hitPos;
					Vector3 spawnRot = new Vector3(0f, placementRot, 0f);
					float normalOffset = config.SurfaceNormalOffset;

					if (config.PlacementMode == VfxPlacementMode.SurfaceSnap)
					{
						Vector3 hitNormal = hit.ContainsKey("normal") ? hit["normal"].AsVector3() : GetTerrainNormalAt(hitPos);
						Basis alignedBasis = CreateAlignedBasis(hitNormal);
						alignedBasis = alignedBasis.Rotated(hitNormal, Mathf.DegToRad(placementRot));
						spawnRot = alignedBasis.GetEuler() * (180f / MathF.PI);
						spawnPos = hitPos + hitNormal * normalOffset;
					}
					else
					{
						spawnPos = hitPos + Vector3.Up * normalOffset;
					}

					var vfx = SpawnVfxExternalWithParams(ActivePlaceId, spawnPos, spawnRot, Vector3.One * safeScale, normalOffset, config);
					if (vfx != null)
					{
						var actions = new List<IEditorAction>
						{
							new ObjectSpawnAction("vfx", ActivePlaceId, spawnPos, spawnRot.Y, safeScale, false, vfx)
						};
						if (EditorMirrorMode != MirrorMode.None)
						{
							foreach (var t in GetMirroredTransforms(hitPos, placementRot))
							{
								Vector3 mPos = t.Position;
								mPos.Y = GetTerrainHeightAt(mPos);
								Vector3 mRot = new Vector3(0f, t.Rotation, 0f);
								if (config.PlacementMode == VfxPlacementMode.SurfaceSnap)
								{
									Vector3 mNormal = GetTerrainNormalAt(mPos);
									Basis mBasis = CreateAlignedBasis(mNormal).Rotated(mNormal, Mathf.DegToRad(t.Rotation));
									mRot = mBasis.GetEuler() * (180f / MathF.PI);
									mPos += mNormal * normalOffset;
								}
								else
								{
									mPos += Vector3.Up * normalOffset;
								}

								var mVfx = SpawnVfxExternalWithParams(ActivePlaceId, mPos, mRot, Vector3.One * safeScale, normalOffset, config);
								if (mVfx != null)
								{
									actions.Add(new ObjectSpawnAction("vfx", ActivePlaceId, mPos, mRot.Y, safeScale, false, mVfx));
								}
							}
						}

						var composite = new CompositeAction(actions);
						EditorHistoryManager.RecordAction(composite);
						EditorHasUnsavedChanges = true;
					}

					GenerateNewRandomPlacementRotationAndScale();
					GameContext.EditorService.SetIsPastingObject(false);
					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.DeleteObject)
				{
					var collider = hit["collider"].As<Node>();
					var action = DeleteObjectAtWithUndo(collider, hitPos);
					if (action != null)
					{
						var actions = new List<IEditorAction> { action };
						if (EditorMirrorMode != MirrorMode.None)
						{
							foreach (var t in GetMirroredTransforms(hitPos, 0.0f))
							{
								var nearObj = FindObjectNearPosition(t.Position);
								if (nearObj != null)
								{
									var mAction = DeleteObjectAtWithUndo(nearObj, t.Position);
									if (mAction != null)
									{
										actions.Add(mAction);
									}
								}
							}
						}

						var composite = new CompositeAction(actions);
						EditorHistoryManager.RecordAction(composite);
						EditorHasUnsavedChanges = true;
					}

					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.Eyedropper)
				{
					string mode = MapEditorHUD.Instance != null ? MapEditorHUD.Instance.GetEyedropperMode() : "all";
					var collider = hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
					Node clickedNode = null;

					if (mode == "all" || mode == "3d")
					{
						if (collider != null)
						{
							clickedNode = FindUnit3DInParentChain(collider);
							if (clickedNode == null)
							{
								clickedNode = FindProp3DInParentChain(collider);
							}
						}

						if (clickedNode == null && GameContext.World != null)
						{
							string closestStaticPropId = null;
							float closestStaticDist = 3.0f;
							var propQuery = QueryCache.AllPropIdentityAndPositionQuery;
							GameContext.World.Query(in propQuery, (Entity entity, ref PropIdentity pId, ref Position pPos) =>
							{
								if (EntityToProp3D.ContainsKey(entity)) return;
								Vector3 wPos = new Vector3(pPos.Value.X, pPos.Value.Y, pPos.Value.Z);
								float d = wPos.DistanceTo(hitPos);
								if (d < closestStaticDist)
								{
									closestStaticDist = d;
									closestStaticPropId = pId.PropId;
								}
							});

							if (!string.IsNullOrEmpty(closestStaticPropId))
							{
								if (MapEditorHUD.Instance != null)
								{
									MapEditorHUD.Instance.SelectPickedUnitOrProp(closestStaticPropId, false);
								}
								else
								{
									ActivePlaceId = closestStaticPropId;
									ActiveEditorTool = EditorTool.PlaceProp;
								}

								GetViewport().SetInputAsHandled();
								return;
							}
						}
					}

					if (clickedNode == null && (mode == "all" || mode == "decal"))
					{
						Decal closestDecal = null;
						float closestDist = 3.0f;
						foreach (var child in GetChildren())
						{
							if (child is Decal dec && GodotObject.IsInstanceValid(dec))
							{
								float d = dec.GlobalPosition.DistanceTo(hitPos);
								if (d < closestDist)
								{
									closestDist = d;
									closestDecal = dec;
								}
							}
						}

						if (closestDecal != null)
						{
							clickedNode = closestDecal;
						}
					}

					if (clickedNode != null)
					{
						if (clickedNode is Unit3D unit)
						{
							if (MapEditorHUD.Instance != null)
							{
								MapEditorHUD.Instance.SetSpawnAsEnemy(unit.IsEnemy);
								MapEditorHUD.Instance.SelectPickedUnitOrProp(unit.UnitId, unit.IsBuilding);
							}
							else
							{
								ActivePlaceId = unit.UnitId;
								GameContext.EditorService.PlaceUnitIsEnemy = unit.IsEnemy;
								ActiveEditorTool = EditorTool.PlaceUnit;
							}
						}
						else if (clickedNode is Prop3D prop)
						{
							if (MapEditorHUD.Instance != null)
							{
								MapEditorHUD.Instance.SelectPickedUnitOrProp(prop.PropId, false);
							}
							else
							{
								ActivePlaceId = prop.PropId;
								ActiveEditorTool = EditorTool.PlaceProp;
							}
						}
						else if (clickedNode is Decal decal)
						{
							string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
							if (MapEditorHUD.Instance != null)
							{
								MapEditorHUD.Instance.SelectPickedDecalView(decalId);
							}
							else
							{
								ActivePlaceId = decalId;
								ActiveEditorTool = EditorTool.PlaceDecal;
							}
						}
						else if (clickedNode is ProceduralVfxInstance3D vfxInst)
						{
							ActivePlaceId = vfxInst.Config?.VfxId ?? "vfx";
							ActiveEditorTool = EditorTool.PlaceVfx;
						}
					}
					else
					{
						bool wantHeight = (mode == "height") || (mode == "all" && Input.IsKeyPressed(Key.Shift));
						bool wantTerrain = (mode == "terrain") || (mode == "all" && !Input.IsKeyPressed(Key.Shift));

						if (wantHeight)
						{
							float sampledHeight = GetTerrainHeightAt(hitPos);
							EditorExactHeight = sampledHeight;
							MapEditorHUD.Instance?.UpdateExactHeightExternal(sampledHeight);
							EditorTool targetTool = EditorTool.Height;
							if (MapEditorHUD.Instance != null)
							{
								MapEditorHUD.Instance.SelectToolFromHotkey(targetTool);
							}
							else
							{
								ActiveEditorTool = targetTool;
							}

							MapEditorHUD.Instance?.ShowFeedbackExternal($"Picked Height: {sampledHeight:F1}m");
						}
						else if (wantTerrain && GroundTerrain != null)
						{
							int w = GroundTerrain.Width;
							int d = GroundTerrain.Depth;
							float quadSize = GroundTerrain.QuadSize;
							float fx = hitPos.X / quadSize + w / 2.0f;
							float fz = hitPos.Z / quadSize + d / 2.0f;
							int splatW = GroundTerrain.SplatMap != null ? GroundTerrain.SplatMap.GetLength(0) : w + 1;
							int splatD = GroundTerrain.SplatMap != null ? GroundTerrain.SplatMap.GetLength(1) : d + 1;
							int x = Mathf.Clamp((int)Math.Round(fx), 0, splatW - 1);
							int z = Mathf.Clamp((int)Math.Round(fz), 0, splatD - 1);
							int sampledIndex = GroundTerrain.SplatMap != null ? GroundTerrain.SplatMap[x, z].GetDominantIndex() : 0;
							GameContext.EditorService.EditorPaintTextureIndex = sampledIndex;
							if (MapEditorHUD.Instance != null)
							{
								MapEditorHUD.Instance.SelectPaintSwatchByIndexView(sampledIndex);
							}
							else
							{
								ActiveEditorTool = EditorTool.PaintTexture;
							}

							MapEditorHUD.Instance?.ShowFeedbackExternal($"Picked Texture: #{sampledIndex}");
						}
					}

					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.SelectMove)
				{
					var collider = hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
					Node clickedNode = null;
					if (collider != null)
					{
						clickedNode = FindUnit3DInParentChain(collider);
						if (clickedNode == null)
						{
							clickedNode = FindProp3DInParentChain(collider);
						}

						if (clickedNode == null)
						{
							clickedNode = FindDecalInParentChain(collider);
						}

						if (clickedNode == null)
						{
							clickedNode = FindVfxInParentChain(collider);
						}
					}

					if (clickedNode == null)
					{
						Decal closestDecal = null;
						float closestDist = 3.0f;
						foreach (var child in GetChildren())
						{
							if (child is Decal dec && GodotObject.IsInstanceValid(dec))
							{
								float d = dec.GlobalPosition.DistanceTo(hitPos);
								if (d < closestDist)
								{
									closestDist = d;
									closestDecal = dec;
								}
							}
						}

						if (closestDecal != null)
						{
							clickedNode = closestDecal;
						}
					}

					if (clickedNode == null)
					{
						ProceduralVfxInstance3D closestVfx = null;
						float closestDist = 3.0f;
						foreach (var vfx in AllVfx)
						{
							if (vfx != null && GodotObject.IsInstanceValid(vfx))
							{
								float d = vfx.GlobalPosition.DistanceTo(hitPos);
								if (d < closestDist)
								{
									closestDist = d;
									closestVfx = vfx;
								}
							}
						}

						if (closestVfx != null)
						{
							clickedNode = closestVfx;
						}
					}

					if (clickedNode != null)
					{
						SelectedEditorObject = clickedNode;
						_isDraggingObject = true;
						_dragObjectStartPos = (SelectedEditorObject as Node3D).Position;
						_dragObjectStartRot = (SelectedEditorObject as Node3D).RotationDegrees;
						_dragObjectStartScale = (SelectedEditorObject as Node3D).Scale;
						GameContext.EditorService.DragStartMousePos = editorMouseBtn.Position;
						Vector3 terrainHitPos = (terrainHit != null && terrainHit.ContainsKey("position")) ? terrainHit["position"].AsVector3() : hitPos;
						GameContext.EditorService.DragStartGroundPos = terrainHitPos;
						GameContext.EditorService.DragObjectStartHitPos = terrainHitPos;
						GameContext.EditorService.DragObjectHasMoved = false;
					}
					else
					{
						SelectedEditorObject = null;
					}

					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.Ramp)
				{
					if (GameContext.EditorService.RampStartPos == null)
					{
						GameContext.EditorService.SetRampStartPos(hitPos);
						MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Start Point Set!");
					}
					else
					{
						Vector3 start = GameContext.EditorService.RampStartPos.Value;
						Vector3 end = hitPos;
						if (GroundTerrain != null && GroundTerrain.Cells != null && GroundTerrain.SplatMap != null && GroundTerrain.PathingCodes != null)
						{
							var cellsBefore = (TerrainCell[,])GroundTerrain.Cells.Clone();
							var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
							var cliffBefore = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
							var pathingBefore = (int[,])GroundTerrain.PathingCodes.Clone();
							bool modified = ApplyRampInternal(start, end);
							if (EditorMirrorMode != MirrorMode.None)
							{
								var startMirrored = GetMirroredTransforms(start, 0.0f);
								var endMirrored = GetMirroredTransforms(end, 0.0f);
								for (int i = 0; i < startMirrored.Count; i++)
								{
									bool mResult = ApplyRampInternal(startMirrored[i].Position, endMirrored[i].Position);
									if (mResult) modified = true;
								}
							}

							if (modified)
							{
								float minX = Mathf.Min(start.X, end.X);
								float maxX = Mathf.Max(start.X, end.X);
								float minZ = Mathf.Min(start.Z, end.Z);
								float maxZ = Mathf.Max(start.Z, end.Z);

								if (EditorMirrorMode != MirrorMode.None)
								{
									var startMirrored = GetMirroredTransforms(start, 0.0f);
									var endMirrored = GetMirroredTransforms(end, 0.0f);
									for (int i = 0; i < startMirrored.Count; i++)
									{
										minX = Mathf.Min(minX, Mathf.Min(startMirrored[i].Position.X, endMirrored[i].Position.X));
										maxX = Mathf.Max(maxX, Mathf.Max(startMirrored[i].Position.X, endMirrored[i].Position.X));
										minZ = Mathf.Min(minZ, Mathf.Min(startMirrored[i].Position.Z, endMirrored[i].Position.Z));
										maxZ = Mathf.Max(maxZ, Mathf.Max(startMirrored[i].Position.Z, endMirrored[i].Position.Z));
									}
								}

								float brushRadius = EditorBrushRadius;
								float quadSize = GroundTerrain.QuadSize;
								int width = GroundTerrain.Width;
								int depth = GroundTerrain.Depth;

								int minGridX = Mathf.Clamp(Mathf.FloorToInt((minX - brushRadius) / quadSize + (width - 1) / 2.0f), 0, width - 1);
								int maxGridX = Mathf.Clamp(Mathf.CeilToInt((maxX + brushRadius) / quadSize + (width - 1) / 2.0f), 0, width - 1);
								int minGridZ = Mathf.Clamp(Mathf.FloorToInt((minZ - brushRadius) / quadSize + (depth - 1) / 2.0f), 0, depth - 1);
								int maxGridZ = Mathf.Clamp(Mathf.CeilToInt((maxZ + brushRadius) / quadSize + (depth - 1) / 2.0f), 0, depth - 1);

								Rect2I affected = new Rect2I(minGridX - 2, minGridZ - 2, maxGridX - minGridX + 4, maxGridZ - minGridZ + 4);

								GroundTerrain.UpdateMeshAndPhysics(true, false, affected);
								AlignAllEntitiesToTerrain(affected);
								var cellsAfter = (TerrainCell[,])GroundTerrain.Cells.Clone();
								var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
								var cliffAfter = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
								var pathingAfter = (int[,])GroundTerrain.PathingCodes.Clone();
								var action = new TerrainModifyAction(cellsBefore, cellsAfter, splatBefore, splatAfter, pathingBefore, pathingAfter, cliffBefore, cliffAfter);
								EditorHistoryManager.RecordAction(action);
								EditorHasUnsavedChanges = true;
								UpdatePathingOverlay();
							}
						}

						GameContext.EditorService.SetRampStartPos(null);
						MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Created!");
					}

					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.FloodFill)
				{
					bool isCliff = false;
					if (hit != null && hit.ContainsKey("normal"))
					{
						Vector3 normal = hit["normal"].AsVector3();
						if (Mathf.Abs(normal.Y) < 0.6f)
						{
							isCliff = true;
						}
					}

					if (!isCliff && GroundTerrain != null && GroundTerrain.Cells != null)
					{
						int w = GroundTerrain.Width;
						int d = GroundTerrain.Depth;
						float q = GroundTerrain.QuadSize;
						int cx = Mathf.Clamp((int)Math.Floor(hitPos.X / q + w / 2.0f), 0, w - 1);
						int cz = Mathf.Clamp((int)Math.Floor(hitPos.Z / q + d / 2.0f), 0, d - 1);
						var c = GroundTerrain.Cells[cx, cz];
						float maxH = Mathf.Max(Mathf.Max(c.Y_NW, c.Y_NE), Mathf.Max(c.Y_SW, c.Y_SE));
						float minH = Mathf.Min(Mathf.Min(c.Y_NW, c.Y_NE), Mathf.Min(c.Y_SW, c.Y_SE));
						if (maxH - minH > 1.5f)
						{
							isCliff = true;
						}
					}

					PerformFloodFill(hitPos, GameContext.EditorService.EditorPaintTextureIndex, isCliff);
					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.FloodFillPathing)
				{
					int pathingMask = 0;
					bool pathingAdd = true;
					if (MapEditorHUD.Instance != null)
					{
						pathingMask = MapEditorHUD.Instance.GetSelectedPathingMask();
						pathingAdd = MapEditorHUD.Instance.IsPathingAddMode();
					}

					PerformFloodFillPathing(hitPos, pathingMask, pathingAdd);
					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.Water)
				{
					bool isRemove = MapEditorHUD.Instance != null && MapEditorHUD.Instance.IsWaterRemoveAction();
					if (MapEditorHUD.Instance != null)
					{
						EditorWaterMode = MapEditorHUD.Instance.GetSelectedWaterMode();
						ActiveWaterProfileIndex = MapEditorHUD.Instance.GetSelectedWaterProfileIndex();
						GameContext.EditorService.EditorWaterHeight = MapEditorHUD.Instance.GetSelectedWaterHeight();
					}

					PerformWaterFloodFill(hitPos, isRemove);
					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.SelectArea)
				{
					if (GroundTerrain != null)
					{
						float fx = hitPos.X / GroundTerrain.QuadSize + (GroundTerrain.Width - 1) / 2.0f;
						float fz = hitPos.Z / GroundTerrain.QuadSize + (GroundTerrain.Depth - 1) / 2.0f;
						int cx = Mathf.Clamp((int)Math.Round(fx), 0, GroundTerrain.Width - 1);
						int cz = Mathf.Clamp((int)Math.Round(fz), 0, GroundTerrain.Depth - 1);
						GameContext.EditorService.SetSelectionStart(new Vector2I(cx, cz));
						GameContext.EditorService.SetSelectionEnd(new Vector2I(cx, cz));
						GameContext.EditorService.SetIsSelectingArea(true);
						CreateSelectionHighlight();
						RebuildSelectionHighlightMesh(cx, cz, cx, cz);
					}

					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.DrawCoordinate)
				{
					if (GroundTerrain != null)
					{
						float rfx = hitPos.X / GroundTerrain.QuadSize + (GroundTerrain.Width - 1) / 2.0f;
						float rfz = hitPos.Z / GroundTerrain.QuadSize + (GroundTerrain.Depth - 1) / 2.0f;
						int rcx = Mathf.Clamp((int)Math.Round(rfx), 0, GroundTerrain.Width - 1);
						int rcz = Mathf.Clamp((int)Math.Round(rfz), 0, GroundTerrain.Depth - 1);
						GameContext.EditorService.SetSelectionStart(new Vector2I(rcx, rcz));
						GameContext.EditorService.SetSelectionEnd(new Vector2I(rcx, rcz));
						GameContext.EditorService.SetIsSelectingArea(true);
						HideCoordinatePreviewMesh();
					}

					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.PasteArea)
				{
					if (GroundTerrain != null && GameContext.EditorService.HasCopiedArea)
					{
						var (cx, cz) = GameContext.EditorService.WorldPosToCellCoords(hitPos);
						var (startX, startZ, targetWidth, targetDepth) = GameContext.EditorService.GetAnchoredPasteBounds(cx, cz, GameContext.EditorService.EditorPasteRotation, GameContext.EditorService.EditorPasteReflection);
						PerformPasteArea(startX, startZ, GameContext.EditorService.EditorPasteRotation, GameContext.EditorService.EditorPasteReflection);
						MapEditorHUD.Instance?.ShowFeedbackExternal("Pasted clipboard contents");
					}

					GetViewport().SetInputAsHandled();
				}
				else if (ActiveEditorTool == EditorTool.Measure)
				{
					if (GroundTerrain != null)
					{
						if (!EditorTapeMeasureActive)
						{
							GameContext.EditorService.EditorTapeMeasureStart = hitPos;
							EditorTapeMeasureEnd = hitPos;
							EditorTapeMeasureActive = true;
							UpdateMeasureVisuals(hitPos, hitPos);
						}
						else if (GameContext.EditorService.EditorTapeMeasureStart.HasValue)
						{
							EditorTapeMeasureEnd = hitPos;
							UpdateMeasureVisuals(GameContext.EditorService.EditorTapeMeasureStart.Value, hitPos);
							EditorTapeMeasureActive = false;
						}
					}

					GetViewport().SetInputAsHandled();
				}
			}
		}

		return;
	}

	private void HandleGameplayInput(InputEvent @event)
	{
		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			return;
		}

		bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
		if (isSpectator)
		{
			if (@event is InputEventKey specKeyEvent && specKeyEvent.Pressed && !specKeyEvent.Echo)
			{
				if (specKeyEvent.Keycode != Key.Escape && specKeyEvent.Keycode != Key.Space && specKeyEvent.Keycode != Key.Z && specKeyEvent.Keycode != Key.Tab)
				{
					GetViewport().SetInputAsHandled();
					return;
				}
			}
			else if (@event is InputEventMouseButton specMouseBtn)
			{
				if (specMouseBtn.ButtonIndex == MouseButton.Right)
				{
					GetViewport().SetInputAsHandled();
					return;
				}

				if (specMouseBtn.ButtonIndex == MouseButton.Left)
				{
					if (ActiveSpellTargeting != null || ActiveCommandTargeting != null || ActiveBuildingPlacementType != null)
					{
						ActiveSpellTargeting = null;
						ActiveCommandTargeting = null;
						CancelBuildingPlacement();
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
			}
		}

		if (@event is InputEventKey escapeEvent && escapeEvent.Pressed && escapeEvent.Keycode == Key.Escape)
		{
			if (ActiveSpellTargeting != null || ActiveCommandTargeting != null)
			{
				ActiveSpellTargeting = null;
				ActiveCommandTargeting = null;
				Input.SetCustomMouseCursor(null);
				Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
				if (InGameHUD.Instance != null)
					InGameHUD.Instance.ShowFeedbackText("Targeting Cancelled", new Color(0.8f, 0.8f, 0.8f));
				GetViewport().SetInputAsHandled();
				return;
			}

			if (ActiveBuildingPlacementType != null)
			{
				CancelBuildingPlacement();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (ActivePingMode)
			{
				ActivePingMode = false;
				if (InGameHUD.Instance != null)
					InGameHUD.Instance.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
				GetViewport().SetInputAsHandled();
				return;
			}

			if (InGameHUD.Instance != null && InGameHUD.Instance.IsBuildSubMenuOpen)
			{
				InGameHUD.Instance.ExitBuildSubMenu();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (SelectedUnits.Count > 0)
			{
				if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && SelectedUnits[0].UnitId == "castle")
				{
					var castle = SelectedUnits[0];
					if (GameContext.World.Has<ProductionQueue>(castle.Entity) && GameContext.World.Get<ProductionQueue>(castle.Entity).UnitIds.Count > 0)
					{
						CancelLastQueuedUnit(castle.Entity);
						GetViewport().SetInputAsHandled();
						return;
					}
				}

				ClearSelection();
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
				GetViewport().SetInputAsHandled();
				return;
			}

			GetViewport().SetInputAsHandled();
			UIManager.Instance.OpenSettingsOverlay();
			return;
		}

		if (@event is InputEventKey keyReleaseEvent && !keyReleaseEvent.Pressed && keyReleaseEvent.Keycode == Key.Shift)
		{
			if (ActiveBuildingPlacementType != null)
			{
				CancelBuildingPlacement();
				InGameHUD.Instance?.ExitBuildSubMenu();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode >= Key.Key0 && keyEvent.Keycode <= Key.Key9)
			{
				int groupIdx = (int)(keyEvent.Keycode - Key.Key0);
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				if (ctrlPressed)
				{
					AssignControlGroup(groupIdx);
					GetViewport().SetInputAsHandled();
					return;
				}
				else
				{
					RecallControlGroup(groupIdx);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (keyEvent.Keycode == Key.F1)
			{
				SelectAllIdleUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.F2)
			{
				SelectAllMilitaryUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.F4)
			{
				if (InGameHUD.Instance != null)
				{
					InGameHUD.Instance.ToggleMinimapTerrain();
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.G && Input.IsKeyPressed(Key.Alt))
			{
				ActivePingMode = !ActivePingMode;
				if (InGameHUD.Instance != null)
				{
					if (ActivePingMode)
						InGameHUD.Instance.ShowFeedbackText("Ping Mode: Click Minimap or Ground to ping", new Color(1.0f, 0.1f, 0.2f));
					else
						InGameHUD.Instance.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.A && Input.IsKeyPressed(Key.Ctrl))
			{
				SelectAllMilitaryUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.Z)
			{
				CycleCameraZoom();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.Space)
			{
				CenterCameraOnCastle();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (InGameHUD.Instance != null && InGameHUD.Instance.HandleCommandCardHotkey(keyEvent.Keycode))
			{
				GetViewport().SetInputAsHandled();
				return;
			}


			if (keyEvent.Keycode == Key.Tab)
			{
				bool reverse = keyEvent.ShiftPressed || Input.IsKeyPressed(Key.Shift);
				CycleSelectionFocus(reverse);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.F3)
			{
				SelectAllBuildings();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode >= Key.F5 && keyEvent.Keycode <= Key.F8)
			{
				int slot = (int)(keyEvent.Keycode - Key.F5) + 1;
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				if (ctrlPressed)
				{
					SaveCameraLocation(slot);
					GetViewport().SetInputAsHandled();
					return;
				}
				else
				{
					RecallCameraLocation(slot);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (keyEvent.Keycode == Key.Quoteleft)
			{
				if (WasmConsoleWindow.IsSinglePlayerOrTestMode())
				{
					WasmConsoleWindow.Instance.ToggleVisibility();
					GetViewport().SetInputAsHandled();
					return;
				}

				CycleThroughBuildings();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventMouseButton rightBtn && rightBtn.ButtonIndex == MouseButton.Right && rightBtn.Pressed && !IsMouseOverUI())
		{
			if (ActiveSpellTargeting != null || ActiveCommandTargeting != null || ActiveBuildingPlacementType != null)
			{
				ActiveSpellTargeting = null;
				ActiveCommandTargeting = null;
				CancelBuildingPlacement();
				Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (SelectedUnits.Count == 0) goto SkipRightClick;

			bool anyFriendlySelected = false;
			foreach (var su in SelectedUnits)
			{
				if (!su.IsEnemy)
				{
					anyFriendlySelected = true;
					break;
				}
			}

			if (!anyFriendlySelected) goto SkipRightClick;

			if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && CanProduceUnits(SelectedUnits[0]))
			{
				var hit = RaycastFromMouse(rightBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					SetRallyPoint(SelectedUnits[0], hit["position"].AsVector3());
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			{
				var hit = RaycastFromMouse(rightBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					var hitPos = hit["position"].AsVector3();
					var collider = hit["collider"].As<Node>();
					var clickedUnit = FindUnit3DInParentChain(collider);
					var clickedProp = FindProp3DInParentChain(collider);
					bool shiftHeld = Input.IsKeyPressed(Key.Shift);

					if (clickedUnit != null && clickedUnit.IsEnemy && clickedUnit.Visible)
					{
						IssueAttackCommand(clickedUnit, shiftHeld);
					}
					else if (clickedUnit != null && !clickedUnit.IsEnemy && clickedUnit != SelectedUnits.Find(u => !u.IsEnemy))
					{
						if (clickedUnit.IsBuilding && GameContext.World.Has<UnderConstruction>(clickedUnit.Entity))
						{
							bool workerSelected = false;
							foreach (var u in SelectedUnits)
							{
								if (u.UnitId == "worker")
								{
									workerSelected = true;
									break;
								}
							}

							if (workerSelected)
							{
								IssueResumeConstructionCommand(clickedUnit, shiftHeld);
							}
							else
							{
								IssueFollowCommand(clickedUnit, shiftHeld);
							}
						}
						else
						{
							IssueFollowCommand(clickedUnit, shiftHeld);
						}
					}
					else if (clickedProp != null && clickedProp.Visible && clickedProp.IsResource)
					{
						IssueGatherCommand(clickedProp, shiftHeld);
					}
					else
					{
						IssueMoveCommand(hitPos, shiftHeld);
					}

					GetViewport().SetInputAsHandled();
					return;
				}
			}
		}

		SkipRightClick:

		if (@event is InputEventMouseButton mouseBtn)
		{
			if (mouseBtn.ButtonIndex == MouseButton.Left)
			{
				if (mouseBtn.Pressed)
				{
					GD.Print($"[GameHost] Unhandled left-click press at position: {mouseBtn.Position}");

					if (mouseBtn.DoubleClick)
					{
						PerformDoubleClickSelection(mouseBtn.Position);
						GetViewport().SetInputAsHandled();
						return;
					}

					if (ActivePingMode)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							AddMinimapPing(hit["position"].AsVector3());
						}

						ActivePingMode = false;
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (ActiveBuildingPlacementType != null)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							var hitPos = hit["position"].AsVector3();
							hitPos.Y = 0f;
							ExecuteBuildingPlacement(ActiveBuildingPlacementType, hitPos);
						}

						GetViewport().SetInputAsHandled();
						return;
					}
					else if (ActiveSpellTargeting != null)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							ExecuteSpellCast(ActiveSpellTargeting, hit["position"].AsVector3());
						}

						ActiveSpellTargeting = null;
						Input.SetCustomMouseCursor(null);
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					}
					else if (ActiveCommandTargeting != null)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							var hitPos = hit["position"].AsVector3();
							var collider = hit["collider"].As<Node>();
							var clickedUnit = FindUnit3DInParentChain(collider);

							bool shiftHeld = Input.IsKeyPressed(Key.Shift);
							if (ActiveCommandTargeting == "attack")
							{
								if (clickedUnit != null && clickedUnit.Entity != Entity.Null)
								{
									IssueAttackCommand(clickedUnit, shiftHeld);
								}
								else
								{
									IssueAttackMoveCommand(hitPos, shiftHeld);
								}
							}
							else if (ActiveCommandTargeting == "move")
							{
								IssueMoveCommand(hitPos, shiftHeld);
							}
							else if (ActiveCommandTargeting == "patrol")
							{
								IssuePatrolCommand(hitPos, shiftHeld);
							}
							else if (ActiveCommandTargeting == "rally")
							{
								if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && CanProduceUnits(SelectedUnits[0]))
								{
									SetRallyPoint(SelectedUnits[0], hitPos);
								}
							}
						}

						ActiveCommandTargeting = null;
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					}
					else
					{
						if (IsMouseOverUI()) return;

						_isDragging = true;
						_dragStart = mouseBtn.Position;
						_dragEnd = mouseBtn.Position;
					}
				}
				else if (_isDragging)
				{
					GD.Print($"[GameHost] Unhandled left-click release at position: {mouseBtn.Position}");

					_isDragging = false;
					if (InGameHUD.Instance != null)
					{
						InGameHUD.Instance.UpdateDragBox(Vector2.Zero, Vector2.Zero, false);
					}

					ClearTemporarySelection();

					float dragDist = _dragStart.DistanceTo(_dragEnd);
					if (dragDist > DragThreshold)
					{
						PerformBoxSelection(_dragStart, _dragEnd);
					}
					else
					{
						PerformSingleClickSelection(_dragStart);
					}
				}
			}
			else if (mouseBtn.ButtonIndex == MouseButton.Right && mouseBtn.Pressed)
			{
				GD.Print($"[GameHost] Unhandled right-click press at position: {mouseBtn.Position}");

				if (ActiveBuildingPlacementType != null)
				{
					CancelBuildingPlacement();
					GetViewport().SetInputAsHandled();
					return;
				}

				if (ActivePingMode)
				{
					ActivePingMode = false;
					if (InGameHUD.Instance != null)
						InGameHUD.Instance.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
					GetViewport().SetInputAsHandled();
					return;
				}

				if (ActiveSpellTargeting != null || ActiveCommandTargeting != null)
				{
					ActiveSpellTargeting = null;
					ActiveCommandTargeting = null;
					Input.SetCustomMouseCursor(null);
					Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					if (InGameHUD.Instance != null)
						InGameHUD.Instance.ShowFeedbackText("Targeting Cancelled", new Color(0.8f, 0.8f, 0.8f));
					return;
				}

				var hit = RaycastFromMouse(mouseBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					var hitPos = hit["position"].AsVector3();
					var collider = hit["collider"].As<Node>();
					var clickedUnit = FindUnit3DInParentChain(collider);
					var clickedProp = FindProp3DInParentChain(collider);

					if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && CanProduceUnits(SelectedUnits[0]))
					{
						SetRallyPoint(SelectedUnits[0], hitPos);
						GetViewport().SetInputAsHandled();
						return;
					}

					if (clickedUnit != null && clickedUnit.Entity != Entity.Null)
					{
						if (clickedUnit.IsEnemy)
						{
							IssueAttackCommand(clickedUnit);
						}
						else
						{
							IssueFollowCommand(clickedUnit);
						}
					}
					else if (clickedProp != null && clickedProp.Visible && clickedProp.IsResource)
					{
						IssueGatherCommand(clickedProp);
					}
					else
					{
						IssueMoveCommand(hitPos);
					}
				}
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isDragging)
		{
			_dragEnd = mouseMotion.Position;
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.UpdateDragBox(_dragStart, _dragEnd, true);
			}

			UpdateTemporarySelection(_dragStart, _dragEnd);
		}
	}


	private void PerformSingleClickSelection(Vector2 clickPos)
	{
		GD.Print($"[GameHost] PerformSingleClickSelection at screen coordinate: {clickPos}");
		ClearSelectedProp();

		var hit = RaycastFromMouse(clickPos);
		if (hit == null || !hit.ContainsKey("collider"))
		{
			ClearSelection();
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
			return;
		}

		var collider = hit["collider"].As<Node>();
		var clickedUnit = FindUnit3DInParentChain(collider);

		if (clickedUnit != null)
		{
			HandleSingleUnitSelection(clickedUnit, clickPos);
		}
		else
		{
			HandleSinglePropSelection(collider);
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void ClearSelectedProp()
	{
		if (SelectedProp != null && GodotObject.IsInstanceValid(SelectedProp))
		{
			SelectedProp.IsSelected = false;
		}

		SelectedProp = null;
	}

	private void HandleSingleUnitSelection(Unit3D clickedUnit, Vector2 clickPos)
	{
		if (!clickedUnit.Visible)
		{
			ClearSelection();
			return;
		}

		if (clickedUnit.IsEnemy)
		{
			ClearSelection();
			SelectUnit(clickedUnit);
			return;
		}

		if (Input.IsKeyPressed(Key.Ctrl))
		{
			PerformDoubleClickSelection(clickPos);
			return;
		}

		bool shiftPressed = Input.IsKeyPressed(Key.Shift);
		bool selectingEnemy = SelectedUnits.Count > 0 && SelectedUnits[0].IsEnemy;

		if (selectingEnemy || !shiftPressed)
		{
			ClearSelection();
			SelectUnit(clickedUnit);
		}
		else
		{
			if (SelectedUnits.Contains(clickedUnit)) DeselectUnit(clickedUnit);
			else SelectUnit(clickedUnit);
		}
	}

	private void HandleSinglePropSelection(Node collider)
	{
		var clickedProp = FindProp3DInParentChain(collider);
		if (clickedProp != null && clickedProp.Visible && clickedProp.IsResource)
		{
			ClearSelection();
			SelectedProp = clickedProp;
			SelectedProp.IsSelected = true;
		}
		else
		{
			ClearSelection();
		}
	}

	private void PerformBoxSelection(Vector2 start, Vector2 end)
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		var dragRect = CreateDragRect(start, end);
		HandleBoxSelectionState();

		var friendlyUnits = new List<Unit3D>();
		var enemyUnits = new List<Unit3D>();
		CollectUnitsInRect(camera, dragRect, friendlyUnits, enemyUnits);

		if (friendlyUnits.Count > 0)
		{
			SelectUnits(friendlyUnits);
		}
		else if (enemyUnits.Count > 0)
		{
			SelectUnits(enemyUnits);
		}
		else
		{
			SelectResourcePropInRect(camera, dragRect);
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private Rect2 CreateDragRect(Vector2 start, Vector2 end)
	{
		Vector2 min = new Vector2(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
		Vector2 max = new Vector2(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y));
		return new Rect2(min, max - min);
	}

	private void HandleBoxSelectionState()
	{
		bool shiftPressed = Input.IsKeyPressed(Key.Shift);
		bool selectingEnemy = SelectedUnits.Count > 0 && SelectedUnits[0].IsEnemy;

		if (selectingEnemy || !shiftPressed)
		{
			ClearSelection();
		}
	}

	private void CollectUnitsInRect(Camera3D camera, Rect2 dragRect, List<Unit3D> friendlyUnits, List<Unit3D> enemyUnits)
	{
		foreach (var unit in AllUnits)
		{
			if (!IsValidVisibleUnit(unit)) continue;

			var screenPos = camera.UnprojectPosition(unit.GlobalPosition);
			if (!dragRect.HasPoint(screenPos)) continue;

			if (unit.IsEnemy)
				enemyUnits.Add(unit);
			else
				friendlyUnits.Add(unit);
		}
	}

	private bool IsValidVisibleUnit(Unit3D unit)
	{
		return unit != null && GodotObject.IsInstanceValid(unit) && unit.Visible;
	}

	private void SelectUnits(List<Unit3D> units)
	{
		foreach (var unit in units)
		{
			SelectUnit(unit);
		}
	}

	private void SelectResourcePropInRect(Camera3D camera, Rect2 dragRect)
	{
		foreach (var prop in AllProps)
		{
			if (prop == null || !GodotObject.IsInstanceValid(prop) || !prop.Visible || !prop.IsResource) continue;

			var screenPos = camera.UnprojectPosition(prop.GlobalPosition);
			if (dragRect.HasPoint(screenPos))
			{
				ClearSelection();
				SelectedProp = prop;
				SelectedProp.IsSelected = true;
				return;
			}
		}
	}

	private void UpdateTemporarySelection(Vector2 start, Vector2 end)
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		var dragRect = CreateDragRect(start, end);

		var friendlyUnits = new List<Unit3D>();
		var enemyUnits = new List<Unit3D>();
		var resourceProps = new List<Prop3D>();

		CollectUnitsForTemporarySelection(camera, dragRect, friendlyUnits, enemyUnits);
		CollectPropsForTemporarySelection(camera, dragRect, resourceProps);

		ApplyTemporarySelectionHighlights(friendlyUnits, enemyUnits, resourceProps);
	}

	private void CollectUnitsForTemporarySelection(Camera3D camera, Rect2 dragRect, List<Unit3D> friendlyUnits, List<Unit3D> enemyUnits)
	{
		foreach (var unit in AllUnits)
		{
			if (!IsValidVisibleUnit(unit)) continue;

			if (dragRect.HasPoint(camera.UnprojectPosition(unit.GlobalPosition)))
			{
				(unit.IsEnemy ? enemyUnits : friendlyUnits).Add(unit);
			}

			unit.SetTemporarySelectionHighlight(false);
		}
	}

	private void CollectPropsForTemporarySelection(Camera3D camera, Rect2 dragRect, List<Prop3D> resourceProps)
	{
		foreach (var prop in AllProps)
		{
			if (prop == null || !GodotObject.IsInstanceValid(prop) || !prop.Visible) continue;

			if (prop.IsResource)
			{
				if (dragRect.HasPoint(camera.UnprojectPosition(prop.GlobalPosition)))
				{
					resourceProps.Add(prop);
				}

				prop.SetTemporarySelectionHighlight(false);
			}
		}
	}

	private void ApplyTemporarySelectionHighlights(List<Unit3D> friendlyUnits, List<Unit3D> enemyUnits, List<Prop3D> resourceProps)
	{
		if (friendlyUnits.Count > 0)
		{
			foreach (var unit in friendlyUnits) unit.SetTemporarySelectionHighlight(true);
		}
		else if (enemyUnits.Count > 0)
		{
			foreach (var unit in enemyUnits) unit.SetTemporarySelectionHighlight(true);
		}
		else if (resourceProps.Count > 0)
		{
			resourceProps[0].SetTemporarySelectionHighlight(true);
		}
	}

	private void ClearTemporarySelection()
	{
		foreach (var unit in AllUnits)
		{
			if (unit != null && GodotObject.IsInstanceValid(unit))
			{
				unit.SetTemporarySelectionHighlight(false);
			}
		}

		foreach (var prop in AllProps)
		{
			if (prop != null && GodotObject.IsInstanceValid(prop) && prop.IsResource)
			{
				prop.SetTemporarySelectionHighlight(false);
			}
		}
	}

	private void SelectUnit(Unit3D unit)
	{
		if (!SelectedUnits.Contains(unit))
		{
			SelectedUnits.Add(unit);
			unit.IsSelected = true;
			GameContext.AudioService?.PlayUnitSound(unit.UnitId, UnitSoundEvent.Select, unit.GlobalPosition);
			OnUnitSelected?.Invoke(GameContext.SimulationService.GetUnitWrapper(unit.Entity));
		}
	}

	private void ClearSelection()
	{
		if (SelectedProp != null && GodotObject.IsInstanceValid(SelectedProp))
		{
			SelectedProp.IsSelected = false;
		}

		SelectedProp = null;

		foreach (var u in SelectedUnits)
		{
			u.IsSelected = false;
		}

		SelectedUnits.Clear();
		CycleSelectionIndex = 0;
	}

	private Unit3D FindUnit3DInParentChain(Node node)
	{
		while (node != null)
		{
			if (node is Unit3D unit)
			{
				return unit;
			}

			node = node.GetParent();
		}

		return null;
	}

	public Decal FindDecalInParentChain(Node node)
	{
		if (!GameContext.EditorService.IsMapEditorMode) return null;
		while (node != null)
		{
			if (node is Decal decal)
			{
				return decal;
			}

			node = node.GetParent();
		}

		return null;
	}

	public ProceduralVfxInstance3D FindVfxInParentChain(Node node)
	{
		if (!GameContext.EditorService.IsMapEditorMode) return null;
		while (node != null)
		{
			if (node is ProceduralVfxInstance3D vfx)
			{
				return vfx;
			}

			node = node.GetParent();
		}

		return null;
	}

	public bool TryRaycastTerrainFromMousePosition(Vector2 mousePos, out Vector3 position)
	{
		return TryRaycastFromMousePosition(mousePos, out position, EditableTerrain.TerrainCollisionLayer);
	}

	public bool TryRaycastFromMousePosition(Vector2 mousePos, out Vector3 position, uint collisionMask = uint.MaxValue)
	{
		position = Vector3.Zero;
		var camera = GetViewport()?.GetCamera3D();
		if (camera == null) return false;

		var from = camera.ProjectRayOrigin(mousePos);
		var to = from + camera.ProjectRayNormal(mousePos) * 1000f;

		var spaceState = GetWorld3D()?.DirectSpaceState;
		if (spaceState == null) return false;

		_cachedRaycastQuery ??= new PhysicsRayQueryParameters3D();
		_cachedRaycastQuery.From = from;
		_cachedRaycastQuery.To = to;
		_cachedRaycastQuery.CollisionMask = collisionMask;
		var result = spaceState.IntersectRay(_cachedRaycastQuery);

		if (result.Count == 0 || !result.ContainsKey("position")) return false;
		position = result["position"].AsVector3();
		return true;
	}

	public Dictionary RaycastTerrainFromMouse(Vector2 mousePos)
	{
		return RaycastFromMouse(mousePos, EditableTerrain.TerrainCollisionLayer);
	}

	public Dictionary RaycastFromMouse(Vector2 mousePos, uint collisionMask = uint.MaxValue)
	{
		var camera = GetViewport()?.GetCamera3D();
		if (camera == null) return null;

		var from = camera.ProjectRayOrigin(mousePos);
		var to = from + camera.ProjectRayNormal(mousePos) * 1000f;

		var spaceState = GetWorld3D()?.DirectSpaceState;
		if (spaceState == null) return null;

		_cachedRaycastQuery ??= new PhysicsRayQueryParameters3D();
		_cachedRaycastQuery.From = from;
		_cachedRaycastQuery.To = to;
		_cachedRaycastQuery.CollisionMask = collisionMask;
		var result = spaceState.IntersectRay(_cachedRaycastQuery);

		if (result.Count == 0) return null;
		return result;
	}

	public void EnterCommandTargeting(string mode)
	{
		ActiveCommandTargeting = mode;
		ActiveSpellTargeting = null;
		Input.SetDefaultCursorShape(Input.CursorShape.Cross);

		if (InGameHUD.Instance != null)
		{
			if (mode == "attack")
			{
				InGameHUD.Instance.ShowFeedbackText("Attack Command: Click enemy to attack, or ground to Attack-Move", new Color(0.9f, 0.4f, 0.1f));
			}
			else if (mode == "move")
			{
				InGameHUD.Instance.ShowFeedbackText("Move Command: Click ground to move", new Color(0.2f, 0.9f, 0.3f));
			}
			else if (mode == "patrol")
			{
				InGameHUD.Instance.ShowFeedbackText("Patrol Command: Click ground to set patrol endpoint", new Color(0.7f, 0.4f, 1.0f));
			}
			else if (mode == "rally")
			{
				InGameHUD.Instance.ShowFeedbackText("Rally Command: Click ground to set building Rally Point", new Color(1.0f, 0.85f, 0.5f));
			}
		}
	}

	public void IssueMoveCommand(Vector3 targetPos, bool isQueued = false)
	{
		if (SelectedUnits.Count == 0) return;

		ViewShowMoveCommandFeedback(targetPos, isQueued);

		var selectedEntities = new List<Entity>();
		var targetIds = new List<int>();
		ViewCollectMoveCommandTargets(selectedEntities, targetIds);

		if (isQueued)
		{
			GameContext.InputService.IssueMoveCommandQueued(selectedEntities, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
		}
		else
		{
			GameContext.InputService.IssueMoveCommand(selectedEntities, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand(isQueued ? "move_queued" : "move", targetIds, targetPos, 0, "");
		}

		ViewPlayMoveCommandSound(selectedEntities);
	}

	private void ViewShowMoveCommandFeedback(Vector3 targetPos, bool isQueued)
	{
		if (isQueued)
		{
			SpawnTargetIndicator(targetPos, new Color(0.2f, 0.7f, 1.0f));
			InGameHUD.Instance?.ShowFeedbackText("Command: Queued Move (Shift+Click)", new Color(0.2f, 0.7f, 1.0f));
		}
		else
		{
			SpawnTargetIndicator(targetPos, new Color(0.22f, 0.54f, 0.26f));
			InGameHUD.Instance?.ShowFeedbackText("Command: Move to position", new Color(0.22f, 0.54f, 0.26f));
		}
	}

	private void ViewCollectMoveCommandTargets(List<Entity> selectedEntities, List<int> targetIds)
	{
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}
	}

	private void ViewPlayMoveCommandSound(List<Entity> selectedEntities)
	{
		if (selectedEntities.Count > 0 && SelectedUnits.Count > 0)
		{
			var primaryUnit = SelectedUnits[0];
			GameContext.AudioService?.PlayUnitSound(primaryUnit.UnitId, UnitSoundEvent.MoveOrder, primaryUnit.GlobalPosition);
		}
	}

	public void IssueAttackCommand(Unit3D target, bool isQueued = false)
	{
		if (SelectedUnits.Count == 0) return;

		ViewShowAttackCommandFeedback(target);

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		ViewCollectAttackCommandTargets(selectedEntities, targetIds);

		GameContext.InputService.IssueAttackCommand(selectedEntities, target.Entity, isQueued);

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("attack", targetIds, target.GlobalPosition, GetServerEntityId(target.Entity), "");
		}

		ViewPlayAttackCommandSound(selectedEntities);
	}

	private void ViewShowAttackCommandFeedback(Unit3D target)
	{
		SpawnTargetIndicator(target.GlobalPosition, new Color(0.9f, 0.1f, 0.1f));

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Command: Attack {target.UnitId.ToUpper()}", new Color(0.9f, 0.2f, 0.2f));
		}
	}

	private void ViewCollectAttackCommandTargets(List<Entity> selectedEntities, List<int> targetIds)
	{
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}
	}

	private void ViewPlayAttackCommandSound(List<Entity> selectedEntities)
	{
		if (selectedEntities.Count > 0 && SelectedUnits.Count > 0)
		{
			var primaryUnit = SelectedUnits[0];
			GameContext.AudioService?.PlayUnitSound(primaryUnit.UnitId, UnitSoundEvent.AttackOrder, primaryUnit.GlobalPosition);
		}
	}

	public void IssueFollowCommand(Unit3D target, bool isQueued = false)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(target.GlobalPosition, new Color(0.2f, 0.6f, 1.0f));
		ShowFollowCommandFeedback(target);

		var selectedEntities = new List<Entity>();
		var targetIds = new List<int>();
		CollectFollowCommandTargets(target, selectedEntities, targetIds);

		GameContext.InputService.IssueFollowCommand(selectedEntities, target.Entity, isQueued);
		QueueMultiplayerFollowCommand(target, targetIds);
	}

	private void ShowFollowCommandFeedback(Unit3D target)
	{
		if (InGameHUD.Instance == null) return;

		bool hasPriest = SelectedUnits.Exists(u => GameContext.World.Has<DefinitionId>(u.Entity) && GameContext.World.Get<DefinitionId>(u.Entity).Value == "priest");
		if (hasPriest)
		{
			InGameHUD.Instance.ShowFeedbackText($"Priest: Healing support target {target.UnitId.ToUpper()}", new Color(0.2f, 0.9f, 0.3f));
		}
		else
		{
			InGameHUD.Instance.ShowFeedbackText($"Command: Follow {target.UnitId.ToUpper()}", new Color(0.2f, 0.6f, 1.0f));
		}
	}

	private void CollectFollowCommandTargets(Unit3D target, List<Entity> selectedEntities, List<int> targetIds)
	{
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy || unit.Entity == target.Entity) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}
	}

	private void QueueMultiplayerFollowCommand(Unit3D target, List<int> targetIds)
	{
		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("follow", targetIds, target.GlobalPosition, GetServerEntityId(target.Entity), "");
		}
	}

	private void SelectAllBuildings()
	{
		ClearSelection();
		var entities = GameContext.InputService.GetBuildingEntities(_playerEntity);
		int count = 0;
		foreach (var ent in entities)
		{
			var unit = AllUnits.Find(u => u.Entity == ent);
			if (unit != null)
			{
				SelectUnit(unit);
				count++;
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		InGameHUD.Instance?.ShowFeedbackText($"Selected {count} Buildings", new Color(0.9f, 0.7f, 0.2f));
	}

	public void CycleSelectionFocus(bool reverse = false)
	{
		if (SelectedUnits.Count <= 1) return;
		var unitIds = new List<string>();
		foreach (var u in SelectedUnits)
		{
			unitIds.Add(u.UnitId);
		}

		int index = GameContext.InputService.CycleSelectionFocus(ECSContext.WorldEntity, unitIds, reverse);
		var focusUnit = SelectedUnits[index];

		var camera = GetViewport().GetCamera3D();
		if (camera != null)
		{
			camera.GlobalPosition = new Vector3(focusUnit.GlobalPosition.X, camera.GlobalPosition.Y, focusUnit.GlobalPosition.Z);
		}

		InGameHUD.Instance?.ShowFeedbackText($"Focused: {focusUnit.UnitId.ToUpper()} ({index + 1}/{SelectedUnits.Count})", new Color(0.5f, 1.0f, 0.5f));
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void CycleThroughBuildings()
	{
		Entity buildingEntity = GameContext.InputService.CycleThroughBuildings(_playerEntity);
		if (buildingEntity == Entity.Null) return;

		var building = AllUnits.Find(u => u.Entity == buildingEntity);
		if (building == null) return;

		var camera = GetViewport().GetCamera3D();
		if (camera != null)
			camera.GlobalPosition = new Vector3(building.GlobalPosition.X, camera.GlobalPosition.Y, building.GlobalPosition.Z);
		SelectOnlyUnit(building);
		InGameHUD.Instance?.ShowFeedbackText($"Jumped to: {building.UnitId.ToUpper()}", new Color(0.9f, 0.8f, 0.3f));
	}

	private void DeleteSelectedUnits()
	{
		if (SelectedUnits.Count == 0) return;

		var toDelete = new List<Unit3D>(SelectedUnits.FindAll(u => !u.IsEnemy));
		var entities = new List<Entity>();
		foreach (var unit in toDelete)
		{
			entities.Add(unit.Entity);
		}

		GameContext.InputService.MarkEntitiesAsDead(entities);

		foreach (var unit in toDelete)
		{
			Callable.From(() => KillUnitDeferred(unit)).CallDeferred();
		}

		InGameHUD.Instance?.ShowFeedbackText($"Removed {toDelete.Count} unit(s)", new Color(0.9f, 0.3f, 0.3f));
	}

	private void KillUnitDeferred(Unit3D unit)
	{
		if (AllUnits.Contains(unit))
			KillUnit(unit);
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void SelectAllIdleUnits()
	{
		ClearSelection();
		var entities = GameContext.InputService.GetIdleUnitEntities(_playerEntity);
		int selectedCount = 0;
		foreach (var ent in entities)
		{
			var unit = AllUnits.Find(u => u.Entity == ent);
			if (unit != null)
			{
				SelectUnit(unit);
				selectedCount++;
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Selected {selectedCount} Idle Units", new Color(0.5f, 1.0f, 0.5f));
		}
	}

	public void SelectAllMilitaryUnits()
	{
		ClearSelection();
		var entities = GameContext.InputService.GetMilitaryUnitEntities(_playerEntity);
		int selectedCount = 0;
		foreach (var ent in entities)
		{
			var unit = AllUnits.Find(u => u.Entity == ent);
			if (unit != null)
			{
				SelectUnit(unit);
				selectedCount++;
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Selected All Army ({selectedCount} Units)", new Color(0.5f, 1.0f, 0.5f));
		}
	}

	private void PerformDoubleClickSelection(Vector2 clickPos)
	{
		var hit = RaycastFromMouse(clickPos);
		if (hit == null || !hit.ContainsKey("collider")) return;

		var clickedUnit = FindUnit3DInParentChain(hit["collider"].As<Node>());
		if (clickedUnit == null || clickedUnit.IsEnemy) return;

		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		SelectSimilarVisibleUnits(clickedUnit.UnitId, camera, GetViewport().GetVisibleRect());
	}

	private void SelectSimilarVisibleUnits(string type, Camera3D camera, Rect2 viewportRect)
	{
		var visibleUnits = AllUnits.FindAll(u => !u.IsEnemy && !u.IsBuilding && u.UnitId == type && viewportRect.HasPoint(camera.UnprojectPosition(u.GlobalPosition)));

		foreach (var u in visibleUnits)
		{
			SelectUnit(u);
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		InGameHUD.Instance?.ShowFeedbackText($"Selected {visibleUnits.Count} {type.ToUpper()}(s)", new Color(0.5f, 1.0f, 0.5f));
	}

	public void CenterCameraOnCastle()
	{
		Unit3D castle = null;
		foreach (var unit in AllUnits)
		{
			if (!unit.IsEnemy && unit.UnitId == "castle")
			{
				castle = unit;
				break;
			}
		}

		if (castle != null)
		{
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				camera.GlobalPosition = new Vector3(castle.GlobalPosition.X, castle.GlobalPosition.Y, castle.GlobalPosition.Z + 25f);
			}
		}
	}

	public void CenterCameraOnSelectedOrCastle()
	{
		if (SelectedUnits.Count > 0)
		{
			var unit = SelectedUnits[0];
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				camera.GlobalPosition = new Vector3(unit.GlobalPosition.X, unit.GlobalPosition.Y, unit.GlobalPosition.Z + 25f);
			}
		}
		else
		{
			CenterCameraOnCastle();
		}
	}

	public void TriggerCopyFromUI()
	{
		if (ActiveEditorTool == EditorTool.SelectArea)
		{
			PerformCopyArea();
			return;
		}

		if (ActiveEditorTool != EditorTool.SelectMove || !GodotObject.IsInstanceValid(SelectedEditorObject))
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Copy (select an object or area first)");
			return;
		}

		if (SelectedEditorObject is Unit3D unit)
		{
			CopyEditorUnit(unit);
			return;
		}

		if (SelectedEditorObject is Prop3D prop)
		{
			CopyEditorProp(prop);
			return;
		}

		if (SelectedEditorObject is Decal decal)
		{
			CopyEditorDecal(decal);
			return;
		}
	}

	private void CopyEditorUnit(Unit3D unit)
	{
		GameContext.EditorService.SetCopiedObject(new CopiedObjectTemplate
		{
			Type = "unit",
			Id = unit.UnitId,
			Rotation = unit.RotationDegrees.Y,
			Scale = unit.Scale.X,
			IsEnemy = unit.IsEnemy
		});
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Unit: {unit.UnitId.ToUpper()}");
	}

	private void CopyEditorProp(Prop3D prop)
	{
		GameContext.EditorService.CopyEditorPropDomain(prop.PropId, prop.RotationDegrees.Y, prop.Scale.X);
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Prop: {prop.PropId.ToUpper()}");
	}

	private void CopyEditorDecal(Decal decal)
	{
		string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
		GameContext.EditorService.CopyEditorDecalDomain(decalId, decal.RotationDegrees.Y, decal.Scale.X);
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Decal: {decalId.ToUpper()}");
	}

	public void TriggerPasteFromUI()
	{
		if (TryActivateAreaPasteMode()) return;

		var copiedObjOpt = GameContext.EditorService.GetCopiedObject();
		if (copiedObjOpt == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Paste (copy an object or area first)");
			return;
		}

		var copiedObj = copiedObjOpt.Value;
		ApplyCopiedObjectState(copiedObj);
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Paste Mode Active - Placing {copiedObj.Id.ToUpper()}");
	}

	private bool TryActivateAreaPasteMode()
	{
		if ((ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea) && GameContext.EditorService.HasCopiedArea)
		{
			ActiveEditorTool = EditorTool.PasteArea;
			MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PasteArea);
			MapEditorHUD.Instance?.ShowFeedbackExternal("Paste Mode Active - Click to paste");
			return true;
		}

		return false;
	}

	private void ApplyCopiedObjectState(CopiedObjectTemplate copiedObj)
	{
		GameContext.EditorService.ApplyCopiedObjectStateDomain(copiedObj, out var newTool);
		ActivePlaceId = copiedObj.Id;
		EditorPlacementScale = copiedObj.Scale;
		ActiveEditorTool = newTool;
		MapEditorHUD.Instance?.SelectToolFromHotkey(newTool);
	}

	private void AssignControlGroup(int index)
	{
		var groupUnits = new List<Unit3D>();
		foreach (var u in SelectedUnits)
		{
			if (!u.IsEnemy)
			{
				groupUnits.Add(u);
			}
		}

		_controlGroups[index] = groupUnits;
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Assigned {groupUnits.Count} units to Control Group {index}", new Color(0.5f, 0.8f, 1.0f));
			InGameHUD.Instance.RefreshUI(SelectedUnits);
		}
	}

	public void RecallControlGroup(int index)
	{
		var group = _controlGroups[index];
		if (group == null || group.Count == 0) return;

		group.RemoveAll(u => !GodotObject.IsInstanceValid(u) || !AllUnits.Contains(u));
		if (group.Count == 0) return;

		ClearSelection();
		foreach (var u in group)
		{
			SelectUnit(u);
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);

		double now = Time.GetTicksMsec() / 1000.0;
		if (now - _lastGroupPressTime[index] < 0.3)
		{
			Vector3 sumPos = Vector3.Zero;
			foreach (var u in group)
			{
				sumPos += u.GlobalPosition;
			}

			Vector3 avgPos = sumPos / group.Count;
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				camera.GlobalPosition = new Vector3(avgPos.X, camera.GlobalPosition.Y, avgPos.Z + 25f);
			}
		}

		_lastGroupPressTime[index] = now;
	}

	public void ClearTargetingModes()
	{
		GameContext.InputService.ClearTargetingModes();
		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
	}

	public void CastSpellAt(string spellId, Vector3 position)
	{
		ExecuteSpellCast(spellId, position);
	}

	public void PlaceBuildingAt(string type, Vector3 position)
	{
		ExecuteBuildingPlacement(type, position);
	}

	public void EnterSpellTargeting(string spellId)
	{
		ActiveSpellTargeting = spellId;
		Input.SetDefaultCursorShape(Input.CursorShape.Cross);

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Casting: Select Location for {spellId.ToUpper()}", new Color(1f, 0.7f, 0.1f));
		}
	}

	private void ExecuteSpellCast(string spellId, Vector3 position)
	{
		var def = GetAbilityDefinition(spellId);

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			ExecuteClientMultiplayerSpellCast(spellId, def, position);
			return;
		}

		ExecuteLocalSpellCast(spellId, def, position);
	}

	private void ExecuteClientMultiplayerSpellCast(string spellId, AbilityDefinition def, Vector3 position)
	{
		if (def != null && def.Cooldown > 0f)
		{
			GameContext.InputService.SetSpellCooldown(_playerEntity, spellId, def.Cooldown);
		}

		if (def != null)
		{
			GameContext.FXService.SpawnAbilityEffect(this, def, position);
		}

		var targetIds = new List<int>();
		if (SelectedUnits.Count > 0 && !SelectedUnits[0].IsEnemy)
		{
			targetIds.Add(GetServerEntityId(SelectedUnits[0].Entity));
		}

		QueueClientCommand("spell", targetIds, position, 0, spellId);
	}

	private void ExecuteLocalSpellCast(string spellId, AbilityDefinition def, Vector3 position)
	{
		int focusedIdx = Math.Clamp(CycleSelectionIndex, 0, Math.Max(0, SelectedUnits.Count - 1));
		Unit3D focusedUnit = SelectedUnits.Count > focusedIdx ? SelectedUnits[focusedIdx] : null;

		IUnit caster = null;
		if (focusedUnit != null && GameContext.World.IsAlive(focusedUnit.Entity))
		{
			caster = GameContext.SimulationService.GetUnitWrapper(focusedUnit.Entity);
			GameContext.AudioService?.PlayUnitSound(focusedUnit.UnitId, UnitSoundEvent.SpellCast, position);
		}

		OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));

		Entity casterEntity = focusedUnit != null && GameContext.World.IsAlive(focusedUnit.Entity) ? focusedUnit.Entity : Entity.Null;

		if (IsSpellOnCooldown(spellId, def)) return;

		if (GameContext.InputService.TryExecuteSpellCast(_playerEntity, casterEntity, spellId, out float maxCd))
		{
			HandleSpellCastSuccess(spellId, def, position);
		}
	}

	private bool IsSpellOnCooldown(string spellId, AbilityDefinition def)
	{
		float cd = GameContext.InputService.GetSpellCooldown(_playerEntity, spellId);
		if (cd <= 0f) return false;

		string displayName = def?.DisplayName ?? spellId;
		InGameHUD.Instance?.ShowFeedbackText($"{displayName} on cooldown: {cd:F1}s remaining", new Color(0.9f, 0.4f, 0.1f));
		return true;
	}

	private void HandleSpellCastSuccess(string spellId, AbilityDefinition def, Vector3 position)
	{
		string displayName = def?.DisplayName ?? spellId;
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Cast: {displayName}", new Color(0.9f, 0.3f, 0.1f));
			UIManager.Instance.PlayClickSound();
		}

		if (def != null)
		{
			GameContext.FXService.SpawnAbilityEffect(this, def, position);
			var simPos = new System.Numerics.Vector3(position.X, position.Y, position.Z);
			Entity target = SelectedUnits.Count > 0 ? SelectedUnits[0].Entity : Entity.Null;
			GameContext.InputService.ApplySpellEffect(def, simPos, target);
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}


	// Lanza una habilidad instantánea (compras/mejoras del mapa) usando la unidad
	// seleccionada como lanzador y su posición como objetivo; sin clic en el suelo.
	public void CastInstantAbility(string abilityId)
	{
		if (SelectedUnits.Count == 0 || !GameContext.World.IsAlive(SelectedUnits[0].Entity)) return;

		if (GameContext.InputService.TryGetCasterPosition(SelectedUnits[0].Entity, out var pos))
		{
			ExecuteSpellCast(abilityId, new Vector3(pos.X, pos.Y, pos.Z));
		}
	}

	public void BuyItem(string itemId, Entity castleEntity)
	{
		ResolveItemInfo(itemId, out string itemName, out float costGold);

		if (InGameHUD.Instance == null) return;

		if (InGameHUD.Instance.Gold < costGold)
		{
			InGameHUD.Instance.ShowFeedbackText("Cannot buy item: Insufficient gold!", new Color(1.0f, 0.2f, 0.2f));
			UIManager.Instance?.PlayWarningSound();
			return;
		}

		if (!TryGetUnit3D(castleEntity, out var castle3D)) return;

		var selectedEntity = SelectedUnits.Count > 0 ? SelectedUnits[0].Entity : Entity.Null;
		var pos = new System.Numerics.Vector3(castle3D.GlobalPosition.X, castle3D.GlobalPosition.Y, castle3D.GlobalPosition.Z);

		if (GameContext.InputService.BuyItem(itemId, _playerEntity, pos, selectedEntity, out Entity targetUnitEntity))
		{
			CompleteItemPurchase(itemId, itemName, costGold, targetUnitEntity);
		}
		else
		{
			InGameHUD.Instance.ShowFeedbackText("Cannot buy item: No friendly combat units nearby!", new Color(1.0f, 0.2f, 0.2f));
			UIManager.Instance?.PlayWarningSound();
		}
	}

	private void ResolveItemInfo(string itemId, out string itemName, out float costGold)
	{
		itemName = itemId;
		costGold = 50f;
		if (ItemRegistry.TryGetValue(itemId, out var itemMeta))
		{
			if (!string.IsNullOrEmpty(itemMeta.Name)) itemName = itemMeta.Name;
			if (itemMeta.CostGold > 0) costGold = itemMeta.CostGold;
		}
	}

	private void CompleteItemPurchase(string itemId, string itemName, float costGold, Entity targetUnitEntity)
	{
		var targetUnit = (GameContext.InputService.IsUnitAlive(targetUnitEntity)) ? GameContext.SimulationService.GetUnitWrapper(targetUnitEntity) : null;
		
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.Gold -= costGold;
		}

		if (targetUnit != null)
		{
			OnItemSold?.Invoke(targetUnit, itemId);
		}

		InGameHUD.Instance?.ShowFeedbackText($"Bought {itemName} for {targetUnit?.TemplateID.ToUpper()}!", new Color(0.3f, 0.9f, 0.4f));
		UIManager.Instance?.PlayClickSound();
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void UseItem(Unit3D unit, string itemId)
	{
		ResolveItemName(itemId, out string itemName, out var itemMeta);

		if (GameContext.InputService.UseItem(unit.Entity, itemId, out float healedAmount))
		{
			ProcessSuccessfulItemUse(unit, itemName, itemMeta, healedAmount);
		}
		else
		{
			ProcessFailedItemUse(unit);
		}
	}

	private void ResolveItemName(string itemId, out string itemName, out ItemMetadata itemMeta)
	{
		itemName = itemId;
		if (GameContext.InputService.TryGetItemMeta(itemId, out itemMeta) && !string.IsNullOrEmpty(itemMeta.Name))
		{
			itemName = itemMeta.Name;
		}
	}

	private void ProcessSuccessfulItemUse(Unit3D unit, string itemName, ItemMetadata itemMeta, float healedAmount)
	{
		InGameHUD.Instance?.ShowFeedbackText($"{unit.UnitId.ToUpper()} used {itemName} (+{healedAmount:F0} HP)!", new Color(0.3f, 0.9f, 0.4f));

		if (itemMeta != null && !string.IsNullOrEmpty(itemMeta.UseAbility) && GameContext.InputService.GetAbilityDefinition(itemMeta.UseAbility) is AbilityDefinition abDef)
		{
			GameContext.FXService.SpawnAbilityEffect(this, abDef, unit.GlobalPosition);
		}

		FlashHealUnit(unit);
		GameContext.FXService.SpawnHealNumber(this, unit.GlobalPosition, healedAmount);

		UIManager.Instance?.PlayClickSound();
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void ProcessFailedItemUse(Unit3D unit)
	{
		if (GameContext.InputService.IsUnitAtFullHealth(unit.Entity))
		{
			InGameHUD.Instance?.ShowFeedbackText("Unit is already at full health!", new Color(0.8f, 0.8f, 0.8f));
			UIManager.Instance?.PlayWarningSound();
		}
	}

	public void EnterBuildingPlacement(string type)
	{
		ActiveBuildingPlacementType = type;

		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}

		var mesh = new MeshInstance3D();
		var box = new BoxMesh();
		if (type == "castle")
		{
			box.Size = new Vector3(10f, 2f, 10f);
		}
		else
		{
			box.Size = new Vector3(3.2f, 4f, 3.2f);
		}

		mesh.Mesh = box;

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.2f, 0.9f, 0.3f, 0.5f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mesh.MaterialOverride = mat;

		AddChild(mesh);
		_buildingPreviewMesh = mesh;

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Place Building: Click on map to construct {type.ToUpper()}", new Color(0.2f, 0.9f, 0.4f));
		}
	}

	public void CancelBuildingPlacement()
	{
		ActiveBuildingPlacementType = null;
		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText("Building Placement Cancelled", new Color(0.8f, 0.8f, 0.8f));
		}

		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
	}

	private void ExecuteBuildingPlacement(string type, Vector3 position)
	{
		bool shiftHeld = Input.IsKeyPressed(Key.Shift);
		var meta = UnitRegistry[type];

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			if (InGameHUD.Instance != null)
			{
				DeductResourcesForBuilding(meta);
				InGameHUD.Instance.ShowFeedbackText($"Constructing {meta.Name}...", new Color(0.3f, 0.9f, 0.4f));
			}

			GameContext.InputService.ExecuteClientBuildingPlacement(meta, type, new System.Numerics.Vector3(position.X, position.Y, position.Z));
			QueueClientCommand("build", new List<int>(), position, 0, type);

			if (!shiftHeld) ClearBuildingPlacementState();
			return;
		}

		if (InGameHUD.Instance == null) return;

		float clearance = type == "castle" ? 7f : 4f;
		if (!GameContext.InputService.TryPlaceBuilding(new System.Numerics.Vector3(position.X, position.Y, position.Z), clearance))
		{
			ViewHandleObstructedPlacement();
			return;
		}

		if (!GameContext.InputService.HasSufficientResources(
			    meta,
			    InGameHUD.Instance.Gold,
			    InGameHUD.Instance.Wood,
			    InGameHUD.Instance.Stone
		    ))
		{
			InGameHUD.Instance.ShowFeedbackText("Cannot construct: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
			UIManager.Instance?.PlayWarningSound();
		}
		else
		{
			DeductResourcesForBuilding(meta);
			
			Unit3D firstWorker = SelectedUnits.Find(u => !u.IsBuilding && !u.IsEnemy && u.UnitId == "worker");
			if (GameContext.InputService.TryAssignFirstWorkerTask(firstWorker?.Entity ?? Arch.Core.Entity.Null, type, new System.Numerics.Vector3(position.X, position.Y, position.Z), shiftHeld))
			{
				AssignAdditionalWorkerTasks(type, new System.Numerics.Vector3(position.X, position.Y, position.Z), shiftHeld, firstWorker);
			}
			else
			{
				SpawnInstantBuilding(type, position, meta);
			}

			UIManager.Instance?.PlayClickSound();
		}

		if (!shiftHeld) ClearBuildingPlacementState();
	}

	private void ExecuteClientBuildingPlacement(string type, Vector3 position, UnitMetadata bldMeta, bool shiftHeld)
	{
		if (InGameHUD.Instance != null)
		{
			DeductResourcesForBuilding(bldMeta);
			InGameHUD.Instance.ShowFeedbackText($"Constructing {bldMeta.Name}...", new Color(0.3f, 0.9f, 0.4f));
		}

		GameContext.InputService.ExecuteClientBuildingPlacement(bldMeta, type, new System.Numerics.Vector3(position.X, position.Y, position.Z));
		QueueClientCommand("build", new List<int>(), position, 0, type);

		if (!shiftHeld) ClearBuildingPlacementState();
	}

	private void ViewHandleObstructedPlacement()
	{
		InGameHUD.Instance.ShowFeedbackText("Cannot construct: Area is obstructed!", new Color(1.0f, 0.2f, 0.2f));
		UIManager.Instance?.PlayWarningSound();
		ClearBuildingPlacementState();
	}

	private void DeductResourcesForBuilding(UnitMetadata meta)
	{
		GameContext.InputService.DeductResourcesForBuilding(
			meta,
			amount => InGameHUD.Instance.Gold -= amount,
			amount => InGameHUD.Instance.Wood -= amount,
			amount => InGameHUD.Instance.Stone -= amount);
	}

	private void ProcessBuildingPlacement(string type, Vector3 position, UnitMetadata meta, bool shiftHeld)
	{
		var buildingPos = new System.Numerics.Vector3(position.X, position.Y, position.Z);
		Unit3D firstWorker = SelectedUnits.Find(u => !u.IsBuilding && !u.IsEnemy && u.UnitId == "worker");

		if (firstWorker != null)
		{
			AssignWorkerTasks(type, buildingPos, meta, shiftHeld, firstWorker);
		}
		else
		{
			SpawnInstantBuilding(type, position, meta);
		}
	}

	private void AssignWorkerTasks(string type, System.Numerics.Vector3 buildingPos, UnitMetadata meta, bool shiftHeld, Unit3D firstWorker)
	{
		AssignFirstWorkerTask(type, buildingPos, meta, shiftHeld, firstWorker);
		AssignAdditionalWorkerTasks(type, buildingPos, shiftHeld, firstWorker);
	}

	private void AssignFirstWorkerTask(string type, System.Numerics.Vector3 buildingPos, UnitMetadata meta, bool shiftHeld, Unit3D firstWorker)
	{
		bool wasQueued = GameContext.InputService.AssignFirstWorkerTask(
			firstWorker.Entity, type, buildingPos, shiftHeld,
			(entity, t, pos) => AssignBuildTaskToWorker(entity, t, pos));

		if (wasQueued)
		{
			InGameHUD.Instance.ShowFeedbackText($"Queued: Construct {meta.Name}", new Color(0.5f, 0.8f, 1.0f));
		}
		else
		{
			InGameHUD.Instance.ShowFeedbackText($"Constructing: {meta.Name}", new Color(0.3f, 0.9f, 0.4f));
		}
	}

	private void AssignAdditionalWorkerTasks(string type, System.Numerics.Vector3 buildingPos, bool shiftHeld, Unit3D firstWorker)
	{
		foreach (var unit in SelectedUnits)
		{
			ProcessWorkerTaskAssignment(unit, firstWorker, type, buildingPos, shiftHeld);
		}
	}

	private void ProcessWorkerTaskAssignment(Unit3D unit, Unit3D firstWorker, string type, System.Numerics.Vector3 buildingPos, bool shiftHeld)
	{
		GameContext.InputService.ProcessWorkerTaskAssignment(
			unit.Entity, firstWorker.Entity, unit.IsBuilding, unit.IsEnemy, unit.UnitId, type, buildingPos, shiftHeld);
	}


	private void SpawnInstantBuilding(string type, Vector3 position, UnitMetadata meta)
	{
		var bldEntity = GameContext.InputService.CreateInstantBuildingEcsUnit(type, meta, new System.Numerics.Vector3(position.X, position.Y, position.Z), _playerEntity);
		string modelPath = GetFallbackModelPath(meta.ModelPath, true);
		SpawnUnit3D(bldEntity, type, modelPath, position, true, false);
		InGameHUD.Instance?.ShowFeedbackText($"Constructed: {meta.Name}", new Color(0.3f, 0.9f, 0.4f));
	}

	private void ClearBuildingPlacementState()
	{
		ActiveBuildingPlacementType = null;
		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}

		InGameHUD.Instance?.ExitBuildSubMenu();
	}

	public void CancelLastQueuedUnit(Entity castleEntity)
	{
		if (GameContext.World.IsAlive(castleEntity) && GameContext.World.Has<ProductionQueue>(castleEntity))
		{
			var prod = GameContext.World.Get<ProductionQueue>(castleEntity);
			if (prod.UnitIds.Count > 0)
			{
				CancelQueuedUnitAt(castleEntity, prod.UnitIds.Count - 1);
			}
		}
	}

	public bool CanProduceUnits(Unit3D unit)
	{
		if (unit == null || !unit.IsBuilding) return false;

		if (!BuildingRegistry.TryGetValue(unit.UnitId, out var meta)) return false;

		return HasValidBuildOption(meta) || HasProductionAbility(meta);
	}

	private bool HasValidBuildOption(UnitMetadata meta)
	{
		if (meta.BuildOptions == null) return false;

		foreach (var opt in meta.BuildOptions)
		{
			if (UnitRegistry.ContainsKey(opt)) return true;
		}

		return false;
	}

	private bool HasProductionAbility(UnitMetadata meta)
	{
		if (meta.Abilities == null) return false;

		foreach (var ab in meta.Abilities)
		{
			if (ab.Contains("spawn") || ab.Contains("train")) return true;
		}

		return false;
	}

	public void SetRallyPoint(Unit3D building, Vector3 position)
	{
		bool queue = Input.IsKeyPressed(Key.Shift);
		SpawnTargetIndicator(position, new Color(0.9f, 0.7f, 0.2f));
		if (InGameHUD.Instance != null)
		{
			if (queue)
			{
				InGameHUD.Instance.ShowFeedbackText($"Queued Rally Point set to {position.X:F0}, {position.Z:F0}", new Color(0.9f, 0.7f, 0.2f));
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText($"Rally Point set to {position.X:F0}, {position.Z:F0}", new Color(0.9f, 0.7f, 0.2f));
			}
		}

		GameContext.InputService.SetRallyPoint(building.Entity, new System.Numerics.Vector3(position.X, position.Y, position.Z), queue);
	}

	public void DeselectUnit(Unit3D unit)
	{
		if (SelectedUnits.Remove(unit))
		{
			unit.IsSelected = false;
		}
	}

	public void SelectOnlyUnit(Unit3D unit)
	{
		ClearSelection();
		SelectUnit(unit);
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	public void BuyWeaponsUpgrade()
	{
		if (HasWeaponsUpgrade) return;

		float costGold = 150f;
		float costWood = 100f;

		if (InGameHUD.Instance != null)
		{
			if (InGameHUD.Instance.Gold >= costGold && InGameHUD.Instance.Wood >= costWood)
			{
				if (GameContext.TechTreeService.BuyWeaponsUpgrade(_playerEntity))
				{
					InGameHUD.Instance.Gold -= costGold;
					InGameHUD.Instance.Wood -= costWood;

					InGameHUD.Instance.ShowFeedbackText("Weapons Upgrade Complete! +3 Damage to all units.", new Color(0.2f, 0.8f, 1.0f));
					UIManager.Instance?.PlayClickSound();
					InGameHUD.Instance.RefreshUI(SelectedUnits);
				}
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	public void BuyShieldsUpgrade()
	{
		if (HasShieldsUpgrade) return;

		float costGold = 150f;
		float costStone = 100f;

		if (InGameHUD.Instance != null)
		{
			if (InGameHUD.Instance.Gold >= costGold && InGameHUD.Instance.Stone >= costStone)
			{
				if (GameContext.TechTreeService.BuyShieldsUpgrade(_playerEntity))
				{
					InGameHUD.Instance.Gold -= costGold;
					InGameHUD.Instance.Stone -= costStone;

					InGameHUD.Instance.ShowFeedbackText("Plated Armor Upgrade Complete! +2 Armor to all units.", new Color(0.2f, 0.8f, 1.0f));
					UIManager.Instance?.PlayClickSound();
					InGameHUD.Instance.RefreshUI(SelectedUnits);
				}
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	public void BuyHarvestingUpgrade()
	{
		if (HasHarvestingUpgrade) return;

		float costWood = 150f;
		float costStone = 100f;

		if (InGameHUD.Instance != null)
		{
			if (InGameHUD.Instance.Wood >= costWood && InGameHUD.Instance.Stone >= costStone)
			{
				if (GameContext.TechTreeService.BuyHarvestingUpgrade(_playerEntity))
				{
					InGameHUD.Instance.Wood -= costWood;
					InGameHUD.Instance.Stone -= costStone;

					InGameHUD.Instance.ResourceGatherMultiplier = 1.5f;

					InGameHUD.Instance.ShowFeedbackText("Harvesting Upgrade Complete! Passive resource accumulation +50%.", new Color(0.2f, 0.8f, 1.0f));
					UIManager.Instance?.PlayClickSound();
					InGameHUD.Instance.RefreshUI(SelectedUnits);
				}
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot upgrade: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}
	}

	public void IssueAttackMoveCommand(Vector3 targetPos)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(targetPos, new Color(0.9f, 0.5f, 0.1f));

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText("Command: Attack-Move to position", new Color(0.9f, 0.5f, 0.1f));
		}

		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;
			selectedEntities.Add(unit.Entity);
		}

		GameContext.InputService.IssueAttackMoveCommand(selectedEntities, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
	}

	public void HoldSelectedUnits()
	{
		if (SelectedUnits.Count == 0) return;

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
			unit.Velocity = Vector3.Zero;
		}

		GameContext.InputService.HoldSelectedUnits(selectedEntities);

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("hold", targetIds, Vector3.Zero, 0, "");
		}
	}

	public void IssuePatrolCommand(Vector3 targetPos, bool isQueued = false)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(targetPos, new Color(0.6f, 0.3f, 1.0f));
		InGameHUD.Instance?.ShowFeedbackText("Command: Patrol Route Set", new Color(0.7f, 0.4f, 1.0f));

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}

		GameContext.InputService.IssuePatrolCommand(selectedEntities, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z), isQueued);

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("patrol", targetIds, targetPos, 0, "");
		}
	}

	public void IssueAttackMoveCommand(Vector3 targetPos, bool isQueued = false)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(targetPos, new Color(0.2f, 0.7f, 1.0f));
		InGameHUD.Instance?.ShowFeedbackText("Command: Queued Move (Shift+Click)", new Color(0.2f, 0.7f, 1.0f));

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}

		GameContext.InputService.IssueAttackMoveCommand(selectedEntities, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z), isQueued);

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("move_queued", targetIds, targetPos, 0, "");
		}
	}

	private void UpdateBuildingPreview()
	{
		if (_buildingPreviewMesh == null || !GodotObject.IsInstanceValid(_buildingPreviewMesh)) return;

		var mousePos = GetViewport().GetMousePosition();
		var hit = RaycastFromMouse(mousePos);
		if (hit != null && hit.ContainsKey("position"))
		{
			Vector3 pos = hit["position"].AsVector3();
			pos.Y = GetTerrainHeightAt(pos) + 0.1f;
			_buildingPreviewMesh.GlobalPosition = pos;

			float clearance = ActiveBuildingPlacementType == "castle" ? 7f : 4f;
			bool blocked = GameContext.InputService.IsAreaObstructed(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), clearance);

			var mat = _buildingPreviewMesh.MaterialOverride as StandardMaterial3D;
			if (mat != null)
			{
				mat.AlbedoColor = blocked ? new Color(0.9f, 0.2f, 0.2f, 0.5f) : new Color(0.2f, 0.9f, 0.3f, 0.5f);
			}
		}
	}

	public void TrainUnitAtCastle(string unitId)
	{
		if (InGameHUD.Instance == null) return;
		var meta = UnitRegistry[unitId];

		if (!TryGetCandidateBuilding(out Entity targetBuildingEntity)) return;

		if (!ValidateTrainingConstraints(meta)) return;

		if (!GameContext.InputService.HasSufficientResources(
			    meta,
			    InGameHUD.Instance.Gold,
			    InGameHUD.Instance.Wood,
			    InGameHUD.Instance.Stone
		    ))
		{
			ShowTrainingFeedback("Cannot train unit: Insufficient resources!", new Color(1f, 0.2f, 0.2f), isWarning: true);
			return;
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("train", new List<int> { GetServerEntityId(targetBuildingEntity) }, Vector3.Zero, 0, unitId);
			DeductResourcesAndNotify(meta);
		}
		else if (GameContext.InputService.TryQueueUnitAtCastle(_playerEntity, targetBuildingEntity, unitId, meta.PopCost, meta.ProductionTime))
		{
			DeductResourcesAndNotify(meta);
		}
	}

	private bool TryGetCandidateBuilding(out Entity targetBuildingEntity)
	{
		var candidateBuildingEntities = SelectedUnits.FindAll(u => !u.IsEnemy && u.IsBuilding && GameContext.World.IsAlive(u.Entity) && CanProduceUnits(u)).ConvertAll(u => u.Entity);

		var result = GameContext.InputService.TryGetCandidateBuilding(candidateBuildingEntities, out targetBuildingEntity);

		if (result == InputService.TryGetCandidateBuildingResult.NoProducingBuildingSelected)
		{
			ShowTrainingFeedback(TranslationServer.Translate("Cannot train unit: No producing building selected!"), new Color(1f, 0.3f, 0.3f), isWarning: true);
			return false;
		}
		else if (result == InputService.TryGetCandidateBuildingResult.TrainingQueueFull)
		{
			ShowTrainingFeedback(TranslationServer.Translate("Training queue is full! (Max 5)"), new Color(1f, 0.3f, 0.3f), isWarning: true);
			return false;
		}

		return true;
	}

	private bool ValidateTrainingConstraints(UnitMetadata meta)
	{
		var result = GameContext.InputService.ValidateTrainingConstraints(meta, CurrentPopulation, MaxPopulation);

		if (result == InputService.ValidateTrainingConstraintsResult.PopulationCapReached)
		{
			string msg = string.Format(TranslationServer.Translate("Population cap reached! ({0}/{1})"), CurrentPopulation, MaxPopulation);
			ShowTrainingFeedback(msg, new Color(1f, 0.3f, 0.3f), isWarning: true);
			return false;
		}

		return true;
	}

	private void DeductResourcesAndNotify(UnitMetadata meta)
	{
		float gold = InGameHUD.Instance.Gold;
		float wood = InGameHUD.Instance.Wood;
		float stone = InGameHUD.Instance.Stone;

		GameContext.InputService.DeductResources(meta, ref gold, ref wood, ref stone);

		InGameHUD.Instance.Gold = gold;
		InGameHUD.Instance.Wood = wood;
		InGameHUD.Instance.Stone = stone;

		string msg = string.Format(TranslationServer.Translate("Queued {0} ({1}/{2} pop)"), meta.Name, CurrentPopulation, MaxPopulation);
		ShowTrainingFeedback(msg, new Color(0.2f, 0.8f, 1f), isWarning: false);
		InGameHUD.Instance.RefreshUI(SelectedUnits);
	}

	private void ShowTrainingFeedback(string message, Color color, bool isWarning)
	{
		InGameHUD.Instance.ShowFeedbackText(message, color);
		if (isWarning)
			UIManager.Instance?.PlayWarningSound();
		else
			UIManager.Instance?.PlayClickSound();
	}

	public void SaveCameraLocation(int slotIndex)
	{
		if (slotIndex < 1 || slotIndex > 4) return;
		var camera = MainCamera;
		if (camera == null) return;

		Vector3 pos = camera.GlobalPosition;
		float zoom = camera is CameraControl camCtrl ? camCtrl.TargetHeight : pos.Y;

		GameContext.InputService.TrySaveCameraSlot(slotIndex, new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), zoom);
		ShowSaveCameraFeedback(slotIndex);
	}

	private void ShowSaveCameraFeedback(int slotIndex)
	{
		InGameHUD.Instance?.ShowFeedbackText($"Camera Location {slotIndex} Saved", new Color(0.5f, 0.8f, 1.0f));
	}

	public void RecallCameraLocation(int slotIndex)
	{
			if (!GameContext.InputService.TryGetCameraState(out var state)) return;

			CameraLocationSlot slot = GameContext.InputService.GetCameraSlotFromState(ref state, slotIndex);
		if (!slot.IsSet) return;

		ApplyCameraSlot(slot);
	}

	private void ApplyCameraSlot(CameraLocationSlot slot)
	{
		var camera = MainCamera;
		if (camera == null) return;

		Vector3 savedPos = new Vector3(slot.Position.X, slot.Position.Y, slot.Position.Z);
		camera.GlobalPosition = savedPos;

		if (camera is CameraControl camCtrl)
		{
			camCtrl.FollowTarget = null;
			camCtrl.TargetHeight = slot.ZoomLevel;
			camCtrl.CurrentHeight = savedPos.Y;
		}
	}

	public void CycleCameraZoom()
	{
		var camCtrl = MainCamera ?? GetViewport()?.GetCamera3D() as CameraControl;
		if (camCtrl != null)
		{
			camCtrl.CycleZoom();
		}
	}

	public void StopSelectedUnits()
	{
		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsEnemy) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
			unit.Velocity = Vector3.Zero;
		}

		GameContext.InputService.StopSelectedUnits(selectedEntities);

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("stop", targetIds, Vector3.Zero, 0, "");
		}
	}

	public void CancelQueuedUnitAt(Entity castleEntity, int index)
	{
		if (!GameContext.World.IsAlive(castleEntity) || !GameContext.World.Has<ProductionQueue>(castleEntity)) return;

		int popCost = GetPopCostForIndex(castleEntity, index);

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("cancel_train", new List<int> { GetServerEntityId(castleEntity) }, Vector3.Zero, index, "");
		}

		if (GameContext.InputService.CancelQueuedUnitAt(castleEntity, index, out string? cancelledId, out string? nextUnitId, popCost))
		{
			RefundCancelledUnit(cancelledId);
			UpdateProductionQueueAfterCancel(castleEntity, index, nextUnitId);

			UIManager.Instance?.PlayClickSound();
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	private int GetPopCostForIndex(Entity castleEntity, int index)
	{
		return GameContext.InputService.GetPopCostForIndex(castleEntity, index);
	}

	private void RefundCancelledUnit(string? cancelledId)
	{
		if (cancelledId == null || InGameHUD.Instance == null) return;

		var meta = UnitRegistry[cancelledId];
		InGameHUD.Instance.Gold += meta.CostGold;
		InGameHUD.Instance.Wood += meta.CostWood;
		InGameHUD.Instance.Stone += meta.CostStone;
		CurrentPopulation = Math.Max(0, CurrentPopulation - meta.PopCost);
		InGameHUD.Instance.ShowFeedbackText($"Cancelled {meta.Name} (Refunded {meta.CostGold}G, {meta.CostWood}W, {meta.CostStone}S)", new Color(1f, 0.8f, 0.2f));
	}

	private void UpdateProductionQueueAfterCancel(Entity castleEntity, int index, string? nextUnitId)
	{
		GameContext.InputService.UpdateProductionQueueAfterCancel(castleEntity, index, nextUnitId, _multiplayerActive && !Multiplayer.IsServer());
	}

	private bool UnitHasAbility(Unit3D unit, string abilityId)
	{
		if (UnitRegistry.TryGetValue(unit.UnitId, out var meta))
		{
			if (meta.Abilities != null)
			{
				return Array.Exists(meta.Abilities, a => a == abilityId);
			}
		}

		return false;
	}

	private float GetPlacementRadius(string placeId, float scale = 1.0f)
	{
		return GameContext.InputService.GetPlacementRadius(placeId, scale);
	}

	private Vector3? FindNearestFreePosition(Vector3 startPos, float checkRadius, float maxSearchDist = 20.0f)
	{
		var res = GameContext.InputService.FindNearestFreePosition(new System.Numerics.Vector3(startPos.X, startPos.Y, startPos.Z), checkRadius, maxSearchDist);
		if (res == null) return null;
		return new Vector3(res.Value.X, res.Value.Y, res.Value.Z);
	}

	private bool IsPositionBlocked(Vector3 pos, float radius, Node3D ignoreNode = null)
	{
		Entity ignoreEntity = Entity.Null;
		if (ignoreNode is Unit3D u) ignoreEntity = u.Entity;
		else if (ignoreNode is Prop3D p) ignoreEntity = p.Entity;
		return GameContext.InputService.IsPositionBlocked(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), radius, ignoreEntity);
	}

	public Unit3D SpawnUnitFromProduction(string unitId, System.Numerics.Vector3 position, bool isEnemy, Entity buildingEntity, bool isFromQueue = false)
	{
		if (!UnitRegistry.TryGetValue(unitId, out var meta)) return null;

		int ownerPeerId = DetermineOwnerPeerId(isEnemy);
		bool actualIsEnemy = NetworkService.ArePeersEnemies(_localPeerId, ownerPeerId);
		Entity playerOwnerEntity = DeterminePlayerOwnerEntity(ownerPeerId, actualIsEnemy);
		var playerOwner = playerOwnerEntity.AsPlayerEntity(GameContext.World);

		string targetModel = meta.ModelPath;
		string modelPath = GetFallbackModelPath(targetModel, false);
		string name = actualIsEnemy ? GameContext.UnitSpawnService.GetEnemyUnitName(unitId, meta.Name) : meta.Name;
		var godotPosition = new Vector3(position.X, position.Y, position.Z);

		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, godotPosition, playerOwner);

		int parentPlayer = ResolveSpawnParentPlayer(buildingEntity, ref actualIsEnemy);

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, godotPosition, false, actualIsEnemy, isFromQueue, parentPlayer);

		if (meta.Speed > 0f)
		{
			ApplySpawnRallyPointOrMove(entity, buildingEntity, position);
		}

		return unit3D;
	}

	private int ResolveSpawnParentPlayer(Entity buildingEntity, ref bool actualIsEnemy)
	{
		return GameContext.UnitSpawnService.ResolveSpawnParentPlayer(buildingEntity, LocalPlayerIndex, ref actualIsEnemy);
	}

	private void ApplySpawnRallyPointOrMove(Entity entity, Entity buildingEntity, System.Numerics.Vector3 defaultPosition)
	{
		GameContext.InputService.ApplySpawnRallyPointOrMove(entity, buildingEntity, defaultPosition);
	}

	private bool TryApplyRallyPoint(Entity entity, Entity buildingEntity)
	{
		return GameContext.InputService.TryApplyRallyPoint(entity, buildingEntity);
	}

	private int DetermineOwnerPeerId(bool isEnemy)
	{
		return GameContext.NetworkService.DetermineOwnerPeerId(isEnemy, _localPeerId);
	}

	private Entity DeterminePlayerOwnerEntity(int ownerPeerId, bool actualIsEnemy)
	{
		return GameContext.NetworkService.DeterminePlayerOwnerEntity(ownerPeerId, actualIsEnemy, _peerIdToPlayerEntityMap, _enemyPlayerEntity, _playerEntity);
	}

	private void IssueResumeConstructionCommand(Unit3D targetBuilding, bool shiftHeld)
	{
		if (targetBuilding == null || !GameContext.World.IsAlive(targetBuilding.Entity)) return;
		if (!GameContext.World.Has<UnderConstruction>(targetBuilding.Entity)) return;

		var pos = targetBuilding.GlobalPosition;
		var bPos = new System.Numerics.Vector3(pos.X, pos.Y, pos.Z);

		AssignResumeTasksToWorkers(targetBuilding, bPos, shiftHeld);

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Resuming construction...", new Color(0.3f, 0.9f, 0.4f));
			InGameHUD.Instance.RefreshUI(SelectedUnits);
		}
	}

	private void AssignResumeTasksToWorkers(Unit3D targetBuilding, System.Numerics.Vector3 bPos, bool shiftHeld)
	{
		var workerEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy || unit.UnitId != "worker") continue;
			workerEntities.Add(unit.Entity);
		}

		GameContext.InputService.AssignResumeTasksToWorkers(workerEntities, targetBuilding.Entity, targetBuilding.UnitId, bPos, shiftHeld);
	}

	public void HandleMinimapRightClick(Vector3 minimapWorldPos)
	{
		if (ClearTargetingModesIfActive()) return;
		if (SelectedUnits.Count == 0 || !SelectedUnits.Exists(su => !su.IsEnemy)) return;
		if (TrySetRallyPointForSingleProducer(minimapWorldPos)) return;

		var rayResult = CastMinimapRay(minimapWorldPos);
		bool shiftHeld = Input.IsKeyPressed(Key.Shift);

		if (TryHandleMinimapAttackOrInteract(rayResult, shiftHeld)) return;

		IssueMoveCommand(rayResult.HitPos, shiftHeld);
	}

	private bool TryHandleMinimapAttackOrInteract(MinimapRayResult rayResult, bool shiftHeld)
	{
		if (rayResult.ClickedUnit != null && rayResult.ClickedUnit.IsEnemy && rayResult.ClickedUnit.Visible)
		{
			IssueAttackCommand(rayResult.ClickedUnit, shiftHeld);
			return true;
		}

		if (rayResult.ClickedUnit != null && !rayResult.ClickedUnit.IsEnemy && rayResult.ClickedUnit != SelectedUnits.Find(u => !u.IsEnemy))
		{
			HandleFriendlyUnitRightClick(rayResult.ClickedUnit, shiftHeld);
			return true;
		}

		if (rayResult.ClickedProp != null && rayResult.ClickedProp.Visible && rayResult.ClickedProp.IsResource)
		{
			IssueGatherCommand(rayResult.ClickedProp, shiftHeld);
			return true;
		}

		return false;
	}

	private bool ClearTargetingModesIfActive()
	{
		if (ActiveSpellTargeting != null || ActiveCommandTargeting != null || ActiveBuildingPlacementType != null)
		{
			ActiveSpellTargeting = null;
			ActiveCommandTargeting = null;
			CancelBuildingPlacement();
			Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
			return true;
		}

		return false;
	}

	private bool TrySetRallyPointForSingleProducer(Vector3 minimapWorldPos)
	{
		if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && CanProduceUnits(SelectedUnits[0]))
		{
			SetRallyPoint(SelectedUnits[0], minimapWorldPos);
			return true;
		}

		return false;
	}


	private MinimapRayResult CastMinimapRay(Vector3 minimapWorldPos)
	{
		var result = new MinimapRayResult { HitPos = minimapWorldPos };

		var from = new Vector3(minimapWorldPos.X, 200f, minimapWorldPos.Z);
		var to = new Vector3(minimapWorldPos.X, -100f, minimapWorldPos.Z);
		var spaceState = GetWorld3D().DirectSpaceState;

		_cachedRaycastQuery ??= new PhysicsRayQueryParameters3D();
		_cachedRaycastQuery.From = from;
		_cachedRaycastQuery.To = to;

		var queryResult = spaceState.IntersectRay(_cachedRaycastQuery);

		if (queryResult != null && queryResult.Count > 0)
		{
			if (queryResult.ContainsKey("position")) result.HitPos = queryResult["position"].AsVector3();
			if (queryResult.ContainsKey("collider"))
			{
				var collider = queryResult["collider"].As<Node>();
				result.ClickedUnit = FindUnit3DInParentChain(collider);
				result.ClickedProp = FindProp3DInParentChain(collider);
			}
		}

		return result;
	}

	private void HandleFriendlyUnitRightClick(Unit3D clickedUnit, bool shiftHeld)
	{
		if (clickedUnit.IsBuilding && GameContext.World.Has<UnderConstruction>(clickedUnit.Entity))
		{
			if (SelectedUnits.Exists(u => u.UnitId == "worker"))
			{
				IssueResumeConstructionCommand(clickedUnit, shiftHeld);
				return;
			}
		}

		IssueFollowCommand(clickedUnit, shiftHeld);
	}
}