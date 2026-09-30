using Godot;
using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Common;
using Realm.MapAPI;
using Realm.Godot.ReplaySystem;
using System;
using System.Collections.Generic;
using Realm.Godot.VFX;

public partial class GameHost
{


	private InputService _inputService;
	private PhysicsRayQueryParameters3D? _cachedRaycastQuery;
	private bool _leftClickInitiatedOverUI = false;
	private bool _is3DLeftClickDown = false;
	private Vector2 _leftClick3DStartPos = Vector2.Zero;
	private bool _is3DDragOperationActive = false;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (SettingsMenu.IsOpen)
		{
			GetViewport().SetInputAsHandled();
			return;
		}

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
					if (IsMapEditorMode && !FloatingDialogBase.HasAnyDialogOpen)
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
				if (IsMapEditorMode && _is3DDragOperationActive)
				{
					_is3DDragOperationActive = false;
					MapEditorHUD.Instance?.Set3DInteractionActive(false);
				}
			}
		}

		if (InGameHUD.Instance != null && InGameHUD.Instance.IsChatActive)
		{
			return;
		}

		if (IsMapEditorMode)
		{
			if (@event is InputEventMouseMotion editorMm)
			{
				if (_is3DLeftClickDown && !_is3DDragOperationActive)
				{
					if (editorMm.Position.DistanceTo(_leftClick3DStartPos) > 3.0f)
					{
						_is3DDragOperationActive = true;
						MapEditorHUD.Instance?.Set3DInteractionActive(true);
					}
				}
			}

			if (@event is InputEventKey editorKeyEvent && editorKeyEvent.Pressed && !editorKeyEvent.Echo)
			{
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				bool shiftPressed = Input.IsKeyPressed(Key.Shift);
				
				if (editorKeyEvent.Keycode == Key.Escape)
				{
					if (_is3DDragOperationActive)
					{
						_is3DDragOperationActive = false;
						_is3DLeftClickDown = false;
						MapEditorHUD.Instance?.Set3DInteractionActive(false);
					}
					if (_editorService.RampStartPos != null)
					{
						_editorService.SetRampStartPos(null);
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
					if (cam != null && cam.HasMethod("ToggleTopDown"))
					{
						cam.Call("ToggleTopDown");
						bool topDown = cam.Call("IsTopDown").AsBool();
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
							_editorService.SetCopiedObject(new EditorService.CopiedObjectTemplate {
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
							_editorService.SetCopiedObject(new EditorService.CopiedObjectTemplate {
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
							_editorService.SetCopiedObject(new EditorService.CopiedObjectTemplate {
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
						if (_editorService.HasCopiedArea)
						{
							ActiveEditorTool = EditorTool.PasteArea;
							MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PasteArea);
							MapEditorHUD.Instance?.ShowFeedbackExternal("Paste Mode Active - Click to paste");
							GetViewport().SetInputAsHandled();
							return;
						}
					}
					var copiedObj = _editorService.GetCopiedObject();
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
					EditorBrushRadius = Mathf.Max(MIN_BRUSH_RADIUS, EditorBrushRadius - 1.0f);
					MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Bracketright)
				{
					EditorBrushRadius = Mathf.Min(MAX_BRUSH_RADIUS, EditorBrushRadius + 1.0f);
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
							if (node3D is Unit3D u) radius = _inputService.GetPlacementRadius(u.UnitId, u.Scale.X);
							else if (node3D is Prop3D p) radius = _inputService.GetPlacementRadius(p.PropId, p.Scale.X);

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
							if (node3D is Unit3D unit && EcsWorld.IsAlive(unit.Entity))
							{
								_inputService.SetEntityPosition(unit.Entity, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
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
					EditorBrushStrength = Mathf.Max(MIN_BRUSH_STRENGTH, EditorBrushStrength - 0.5f);
					MapEditorHUD.Instance?.UpdateBrushStrengthExternal(EditorBrushStrength);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.Equal)
				{
					EditorBrushStrength = Mathf.Min(MAX_BRUSH_STRENGTH, EditorBrushStrength + 0.5f);
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
						2 => EditorTool.Smooth,
						3 => EditorTool.Plateau,
						4 => EditorTool.Ramp,
						5 => EditorTool.Noise,
						6 => EditorTool.PaintTexture,
						7 => EditorTool.PlaceProp,
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
				if (editorKeyEvent.Keycode == Key.V && !ctrlPressed && !shiftPressed)
				{
					EditorGridMode = EditorGridMode switch
					{
						GridOverlayMode.Off => GridOverlayMode.Mesh,
						GridOverlayMode.Mesh => GridOverlayMode.Off,
						_ => GridOverlayMode.Off
					};
					UpdateGridOverlayVisibility();
					MapEditorHUD.Instance?.UpdateGridOverlayExternal(EditorGridMode);
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
					string timeName = EnvironmentService?.GetTimeOfDayName(res.TimeOfDayIndex) ?? "Day";
					string icon = res.TimeOfDayIndex switch
					{
						0 => "☀️",
						1 => "🌅",
						2 => "🌙",
						3 => "🌄",
						_ => "☀️"
					};
					MapEditorHUD.Instance?.ShowFeedbackExternal(string.Format(TranslationServer.Translate("Lighting: {0} {1}"), icon, TranslationServer.Translate(timeName)));
					GetViewport().SetInputAsHandled();
					return;
				}
				if (editorKeyEvent.Keycode == Key.K && !ctrlPressed && !shiftPressed && !editorKeyEvent.AltPressed)
				{
					if (EnvironmentService != null)
					{
						string nextWeather = EnvironmentService.CycleWeather(this);
						string icon = nextWeather switch
						{
							"rain" => "🌧️",
							"snow" => "❄️",
							"fog" => "🌫️",
							_ => "☀️"
						};
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
						EditorBrushRadius = Mathf.Clamp(EditorBrushRadius + deltaSize, MIN_BRUSH_RADIUS, MAX_BRUSH_RADIUS);
						MapEditorHUD.Instance?.UpdateBrushSizeExternal(EditorBrushRadius);
						GetViewport().SetInputAsHandled();
						return;
					}
					if (ctrlPressed)
					{
						float deltaStr = isUp ? 0.5f : -0.5f;
						EditorBrushStrength = Mathf.Clamp(EditorBrushStrength + deltaStr, MIN_BRUSH_STRENGTH, MAX_BRUSH_STRENGTH);
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
						EditorPlacementRotation = (EditorPlacementRotation + rotDelta + 360.0f) % 360.0f;
						MapEditorHUD.Instance?.UpdateRotationExternal(EditorPlacementRotation);
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
						EditorPlacementScale = Mathf.Clamp(EditorPlacementScale + scaleDelta, MIN_PLACEMENT_SCALE, MAX_PLACEMENT_SCALE);
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
				if (_editorService.RampStartPos != null)
				{
					_editorService.SetRampStartPos(null);
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
				else if (ActiveEditorTool == EditorTool.PasteArea)
				{
					ActiveEditorTool = EditorTool.SelectArea;
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectArea);
					if (_selectionHighlightMesh != null) _selectionHighlightMesh.Visible = false;
					GetViewport().SetInputAsHandled();
					return;
				}
				else if (ActiveEditorTool != EditorTool.SelectMove)
				{
					ActiveEditorTool = EditorTool.SelectMove;
					MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectMove);
					if (_selectionHighlightMesh != null) _selectionHighlightMesh.Visible = false;
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
					MapEditorHUD.Instance?.Set3DInteractionActive(false);
				}

				if (FloatingDialogBase.HasAnyDialogOpen)
				{
					return;
				}

				if (_editorService.IsSelectingArea)
				{
					_editorService.SetIsSelectingArea(false);
					if (ActiveEditorTool == EditorTool.DrawCoordinate && _editorService.SelectionStart != null && _editorService.SelectionEnd != null)
					{
						int rMinX = Mathf.Min(_editorService.SelectionStart.Value.X, _editorService.SelectionEnd.Value.X);
						int rMinZ = Mathf.Min(_editorService.SelectionStart.Value.Y, _editorService.SelectionEnd.Value.Y);
						int rMaxX = Mathf.Max(_editorService.SelectionStart.Value.X, _editorService.SelectionEnd.Value.X);
						int rMaxZ = Mathf.Max(_editorService.SelectionStart.Value.Y, _editorService.SelectionEnd.Value.Y);
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
						if (EditorClumpMode) return;
						if (!_editorService.HasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
						float placementRot = (EditorRandomRotation && !_editorService.IsPastingObject) ? _editorService.CachedRandomRotation : EditorPlacementRotation;
						float scaleVal = (EditorRandomScale && !_editorService.IsPastingObject) ? _editorService.CachedRandomScale : EditorPlacementScale;

						Vector3 spawnPos = hitPos;
						spawnPos.Y = GetTerrainHeightAt(spawnPos);
						float radius = _inputService.GetPlacementRadius(ActivePlaceId, scaleVal);
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
							return;
						}
						spawnPos = finalPos.Value;

						var unit = SpawnUnitExternal(ActivePlaceId, spawnPos, PlaceUnitIsEnemy, placementRot, scaleVal);
						if (unit != null)
						{
							var actions = new List<IEditorAction> {
								new ObjectSpawnAction("unit", ActivePlaceId, spawnPos, placementRot, scaleVal, PlaceUnitIsEnemy, unit)
							};
							if (EditorMirrorMode != MirrorMode.None)
							{
								foreach (var t in GetMirroredTransforms(spawnPos, placementRot))
								{
									Vector3 mPos = t.Position;
									mPos.Y = GetTerrainHeightAt(mPos);
									if (IsPositionBlocked(mPos, radius)) continue;
									var mUnit = SpawnUnitExternal(ActivePlaceId, mPos, PlaceUnitIsEnemy, t.Rotation, scaleVal);
									if (mUnit != null)
									{
										actions.Add(new ObjectSpawnAction("unit", ActivePlaceId, mPos, t.Rotation, scaleVal, PlaceUnitIsEnemy, mUnit));
									}
								}
							}
							var composite = new CompositeAction(actions);
							EditorHistoryManager.RecordAction(composite);
							EditorHasUnsavedChanges = true;
						}
						GenerateNewRandomPlacementRotationAndScale();
						_editorService.SetIsPastingObject(false);
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.PlaceProp)
					{
						if (EditorClumpMode) return;
						if (!_editorService.HasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
						float placementRot = (EditorRandomRotation && !_editorService.IsPastingObject) ? _editorService.CachedRandomRotation : EditorPlacementRotation;
						float scaleVal = (EditorRandomScale && !_editorService.IsPastingObject) ? _editorService.CachedRandomScale : EditorPlacementScale;

						Vector3 spawnPos = hitPos;
						spawnPos.Y = GetTerrainHeightAt(spawnPos);
						float radius = _inputService.GetPlacementRadius(ActivePlaceId, scaleVal);
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
							return;
						}
						spawnPos = finalPos.Value;

						var prop = SpawnPropExternalWithParams(ActivePlaceId, spawnPos, placementRot, scaleVal);
						if (prop != null)
						{
							var actions = new List<IEditorAction> {
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
						_editorService.SetIsPastingObject(false);
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.PlaceDecal)
					{
						if (EditorClumpMode) return;
						if (!_editorService.HasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
						float placementRot = (EditorRandomRotation && !_editorService.IsPastingObject) ? _editorService.CachedRandomRotation : EditorPlacementRotation;
						float scaleVal = (EditorRandomScale && !_editorService.IsPastingObject) ? _editorService.CachedRandomScale : EditorPlacementScale;
						
						Vector3 hitNormal = (terrainHit != null && terrainHit.ContainsKey("normal")) ? terrainHit["normal"].AsVector3() : (hit.ContainsKey("normal") ? hit["normal"].AsVector3() : GetTerrainNormalAt(hitPos));
						Basis alignedBasis = CreateAlignedBasis(hitNormal);
						alignedBasis = alignedBasis.Rotated(hitNormal, Mathf.DegToRad(placementRot));
						Vector3 spawnRot = alignedBasis.GetRotationQuaternion().GetEuler() * (180f / MathF.PI);

						var decal = SpawnDecalExternalWithParams(ActivePlaceId, hitPos, spawnRot, scaleVal);
						if (decal != null)
						{
							var actions = new List<IEditorAction> {
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
						_editorService.SetIsPastingObject(false);
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.PlaceVfx)
					{
						if (EditorClumpMode) return;
						if (!_editorService.HasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
						float placementRot = (EditorRandomRotation && !_editorService.IsPastingObject) ? _editorService.CachedRandomRotation : EditorPlacementRotation;
						float scaleVal = (EditorRandomScale && !_editorService.IsPastingObject) ? _editorService.CachedRandomScale : EditorPlacementScale;
						float safeScale = scaleVal <= 0.001f ? 1.0f : scaleVal;

						VfxAttachmentConfig config;
						if (VfxRegistry.TryGetValue(ActivePlaceId, out var regCfg))
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
							var actions = new List<IEditorAction> {
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
						_editorService.SetIsPastingObject(false);
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

							if (clickedNode == null && EcsWorld != null)
							{
								string closestStaticPropId = null;
								float closestStaticDist = 3.0f;
								var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
								EcsWorld.Query(in propQuery, (Arch.Core.Entity entity, ref PropIdentity pId, ref Position pPos) =>
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
									PlaceUnitIsEnemy = unit.IsEnemy;
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
									MapEditorHUD.Instance.SelectPickedDecal(decalId);
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
								EditorBlockLevelHeight = sampledHeight;
								MapEditorHUD.Instance?.UpdateBlockLevelHeightExternal(sampledHeight);
								float avgHeight = 0f;
								if (GroundTerrain != null && GroundTerrain.Cells != null)
								{
									int w = GroundTerrain.Width;
									int d = GroundTerrain.Depth;
									var cells = GroundTerrain.Cells;
									float sum = 0f;
									for (int z = 0; z < d; z++)
									{
										for (int x = 0; x < w; x++)
										{
											sum += cells[x, z].CenterHeight;
										}
									}
									avgHeight = sum / (w * d);
								}
								EditorTool targetTool = sampledHeight >= avgHeight ? EditorTool.Raise : EditorTool.Lower;
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
								EditorPaintTextureIndex = sampledIndex;
								if (MapEditorHUD.Instance != null)
								{
									MapEditorHUD.Instance.SelectPaintSwatchByIndex(sampledIndex);
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
							_dragStartMousePos = editorMouseBtn.Position;
							Vector3 terrainHitPos = (terrainHit != null && terrainHit.ContainsKey("position")) ? terrainHit["position"].AsVector3() : hitPos;
							_dragStartGroundPos = terrainHitPos;
							_dragObjectStartHitPos = terrainHitPos;
							_dragObjectHasMoved = false;
						}
						else
						{
							SelectedEditorObject = null;
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.Ramp)
					{
						if (_editorService.RampStartPos == null)
						{
							_editorService.SetRampStartPos(hitPos);
							MapEditorHUD.Instance?.ShowFeedbackExternal("Ramp Start Point Set!");
						}
						else
						{
							Vector3 start = _editorService.RampStartPos.Value;
							Vector3 end = hitPos;
							if (GroundTerrain != null && GroundTerrain.Cells != null && GroundTerrain.SplatMap != null && GroundTerrain.PathingCodes != null)
							{
								var cellsBefore = (Realm.Ecs.Components.Terrain.TerrainCell[,])GroundTerrain.Cells.Clone();
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
									var cellsAfter = (Realm.Ecs.Components.Terrain.TerrainCell[,])GroundTerrain.Cells.Clone();
									var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
									var cliffAfter = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
									var pathingAfter = (int[,])GroundTerrain.PathingCodes.Clone();
									var action = new TerrainModifyAction(cellsBefore, cellsAfter, splatBefore, splatAfter, pathingBefore, pathingAfter, cliffBefore, cliffAfter);
									EditorHistoryManager.RecordAction(action);
									EditorHasUnsavedChanges = true;
									UpdatePathingOverlay();
								}
							}
							_editorService.SetRampStartPos(null);
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
						PerformFloodFill(hitPos, EditorPaintTextureIndex, isCliff);
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
							EditorWaterHeight = MapEditorHUD.Instance.GetSelectedWaterHeight();
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
							_editorService.SetSelectionStart(new Vector2I(cx, cz));
							_editorService.SetSelectionEnd(new Vector2I(cx, cz));
							_editorService.SetIsSelectingArea(true);
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
							_editorService.SetSelectionStart(new Vector2I(rcx, rcz));
							_editorService.SetSelectionEnd(new Vector2I(rcx, rcz));
							_editorService.SetIsSelectingArea(true);
							HideCoordinatePreviewMesh();
						}
						GetViewport().SetInputAsHandled();
					}
					else if (ActiveEditorTool == EditorTool.PasteArea)
					{
						if (GroundTerrain != null && _editorService.HasCopiedArea)
						{
							float fx = hitPos.X / GroundTerrain.QuadSize + (GroundTerrain.Width - 1) / 2.0f;
							float fz = hitPos.Z / GroundTerrain.QuadSize + (GroundTerrain.Depth - 1) / 2.0f;
							int cx = Mathf.Clamp((int)Math.Round(fx), 0, GroundTerrain.Width - 1);
							int cz = Mathf.Clamp((int)Math.Round(fz), 0, GroundTerrain.Depth - 1);
							PerformPasteArea(cx, cz, EditorPasteRotation);
							ActiveEditorTool = EditorTool.SelectArea;
							MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.SelectArea);
							if (_selectionHighlightMesh != null) _selectionHighlightMesh.Visible = false;
						}
						GetViewport().SetInputAsHandled();
					}
				}
			}
			return;
		}

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
					if (EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castle.Entity) && EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(castle.Entity).UnitIds.Count > 0)
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
				if (Realm.Godot.UI.WasmConsoleWindow.IsSinglePlayerOrTestMode())
				{
					Realm.Godot.UI.WasmConsoleWindow.Instance.ToggleVisibility();
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
				if (!su.IsEnemy) { anyFriendlySelected = true; break; }
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
						if (clickedUnit.IsBuilding && EcsWorld.Has<Realm.Ecs.Components.Tags.UnderConstruction>(clickedUnit.Entity))
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
		
		if (SelectedProp != null && GodotObject.IsInstanceValid(SelectedProp))
		{
			SelectedProp.IsSelected = false;
		}
		SelectedProp = null;

		var hit = RaycastFromMouse(clickPos);
		if (hit != null && hit.ContainsKey("collider"))
		{
			var collider = hit["collider"].As<Node>();
			var clickedUnit = FindUnit3DInParentChain(collider);
			
			if (clickedUnit != null)
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
				}
				else
				{
					bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
					if (ctrlPressed)
					{
						PerformDoubleClickSelection(clickPos);
						return;
					}

					bool shiftPressed = Input.IsKeyPressed(Key.Shift);
					bool selectingEnemy = SelectedUnits.Count > 0 && SelectedUnits[0].IsEnemy;
					
					if (selectingEnemy)
					{
						ClearSelection();
						SelectUnit(clickedUnit);
					}
					else if (shiftPressed)
					{
						if (SelectedUnits.Contains(clickedUnit))
						{
							DeselectUnit(clickedUnit);
						}
						else
						{
							SelectUnit(clickedUnit);
						}
					}
					else
					{
						ClearSelection();
						SelectUnit(clickedUnit);
					}
				}
			}
			else
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
		}
		else
		{
			ClearSelection();
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void PerformBoxSelection(Vector2 start, Vector2 end)
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		Vector2 min = new Vector2(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
		Vector2 max = new Vector2(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y));
		var dragRect = new Rect2(min, max - min);

		bool shiftPressed = Input.IsKeyPressed(Key.Shift);
		bool selectingEnemy = SelectedUnits.Count > 0 && SelectedUnits[0].IsEnemy;
		
		if (selectingEnemy || !shiftPressed)
		{
			ClearSelection();
		}

		var friendlyUnits = new List<Unit3D>();
		var enemyUnits = new List<Unit3D>();
		var resourceProps = new List<Prop3D>();

		foreach (var unit in AllUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit) || !unit.Visible) continue;

			var screenPos = camera.UnprojectPosition(unit.GlobalPosition);
			if (dragRect.HasPoint(screenPos))
			{
				if (unit.IsEnemy)
				{
					enemyUnits.Add(unit);
				}
				else
				{
					friendlyUnits.Add(unit);
				}
			}
		}

		foreach (var prop in AllProps)
		{
			if (prop == null || !GodotObject.IsInstanceValid(prop) || !prop.Visible) continue;
			if (prop.IsResource)
			{
				var screenPos = camera.UnprojectPosition(prop.GlobalPosition);
				if (dragRect.HasPoint(screenPos))
				{
					resourceProps.Add(prop);
				}
			}
		}

		if (friendlyUnits.Count > 0)
		{
			foreach (var unit in friendlyUnits)
			{
				SelectUnit(unit);
			}
		}
		else if (enemyUnits.Count > 0)
		{
			foreach (var unit in enemyUnits)
			{
				SelectUnit(unit);
			}
		}
		else if (resourceProps.Count > 0)
		{
			ClearSelection();
			SelectedProp = resourceProps[0];
			SelectedProp.IsSelected = true;
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void UpdateTemporarySelection(Vector2 start, Vector2 end)
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		Vector2 min = new Vector2(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
		Vector2 max = new Vector2(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y));
		var dragRect = new Rect2(min, max - min);

		var friendlyUnits = new List<Unit3D>();
		var enemyUnits = new List<Unit3D>();
		var resourceProps = new List<Prop3D>();

		foreach (var unit in AllUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit) || !unit.Visible) continue;

			var screenPos = camera.UnprojectPosition(unit.GlobalPosition);
			bool isInside = dragRect.HasPoint(screenPos);
			if (isInside)
			{
				if (unit.IsEnemy)
				{
					enemyUnits.Add(unit);
				}
				else
				{
					friendlyUnits.Add(unit);
				}
			}
			unit.SetTemporarySelectionHighlight(false);
		}

		foreach (var prop in AllProps)
		{
			if (prop == null || !GodotObject.IsInstanceValid(prop) || !prop.Visible) continue;
			if (prop.IsResource)
			{
				var screenPos = camera.UnprojectPosition(prop.GlobalPosition);
				bool isInside = dragRect.HasPoint(screenPos);
				if (isInside)
				{
					resourceProps.Add(prop);
				}
				prop.SetTemporarySelectionHighlight(false);
			}
		}

		if (friendlyUnits.Count > 0)
		{
			foreach (var unit in friendlyUnits)
			{
				unit.SetTemporarySelectionHighlight(true);
			}
		}
		else if (enemyUnits.Count > 0)
		{
			foreach (var unit in enemyUnits)
			{
				unit.SetTemporarySelectionHighlight(true);
			}
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
			_audioService?.PlayUnitSound(unit.UnitId, UnitSoundEvent.Select, unit.GlobalPosition);
			OnUnitSelected?.Invoke(GetUnitWrapper(unit.Entity));
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
		if (!IsMapEditorMode) return null;
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
		if (!IsMapEditorMode) return null;
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

	public Godot.Collections.Dictionary RaycastTerrainFromMouse(Vector2 mousePos)
	{
		return RaycastFromMouse(mousePos, EditableTerrain.TerrainCollisionLayer);
	}

	public Godot.Collections.Dictionary RaycastFromMouse(Vector2 mousePos, uint collisionMask = uint.MaxValue)
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

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}

		if (isQueued)
		{
			_inputService.IssueMoveCommandQueued(selectedEntities, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
		}
		else
		{
			_inputService.IssueMoveCommand(selectedEntities, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
		}

		if (selectedEntities.Count > 0)
		{
			var primaryUnit = SelectedUnits[0];
			_audioService?.PlayUnitSound(primaryUnit.UnitId, UnitSoundEvent.MoveOrder, primaryUnit.GlobalPosition);
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand(isQueued ? "move_queued" : "move", targetIds, targetPos, 0, "");
		}
	}

	public void IssueAttackCommand(Unit3D target, bool isQueued = false)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(target.GlobalPosition, new Color(0.9f, 0.1f, 0.1f));

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Command: Attack {target.UnitId.ToUpper()}", new Color(0.9f, 0.2f, 0.2f));
		}

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}

		_inputService.IssueAttackCommand(selectedEntities, target.Entity, isQueued);

		if (selectedEntities.Count > 0)
		{
			var primaryUnit = SelectedUnits[0];
			_audioService?.PlayUnitSound(primaryUnit.UnitId, UnitSoundEvent.AttackOrder, primaryUnit.GlobalPosition);
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("attack", targetIds, target.GlobalPosition, GetServerEntityId(target.Entity), "");
		}
	}

	public void IssueFollowCommand(Unit3D target, bool isQueued = false)
	{
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(target.GlobalPosition, new Color(0.2f, 0.6f, 1.0f));

		bool hasPriest = false;
		foreach (var unit in SelectedUnits)
		{
			if (EcsWorld.Has<DefinitionId>(unit.Entity) && EcsWorld.Get<DefinitionId>(unit.Entity).Value == "priest")
			{
				hasPriest = true;
				break;
			}
		}

		if (InGameHUD.Instance != null)
		{
			if (hasPriest)
			{
				InGameHUD.Instance.ShowFeedbackText($"Priest: Healing support target {target.UnitId.ToUpper()}", new Color(0.2f, 0.9f, 0.3f));
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText($"Command: Follow {target.UnitId.ToUpper()}", new Color(0.2f, 0.6f, 1.0f));
			}
		}

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy || unit.Entity == target.Entity) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}

		_inputService.IssueFollowCommand(selectedEntities, target.Entity, isQueued);

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("follow", targetIds, target.GlobalPosition, GetServerEntityId(target.Entity), "");
		}
	}

	private void SelectAllBuildings()
	{
		ClearSelection();
		var entities = _inputService.GetBuildingEntities(_playerEntity);
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
		int index = _inputService.CycleSelectionFocus(_worldEntity, unitIds, reverse);
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
		Entity buildingEntity = _inputService.CycleThroughBuildings(_playerEntity);
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

		_inputService.MarkEntitiesAsDead(entities);

		foreach (var unit in toDelete)
		{
			CallDeferred("KillUnitDeferred", unit);
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
		var entities = _inputService.GetIdleUnitEntities(_playerEntity);
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
		var entities = _inputService.GetMilitaryUnitEntities(_playerEntity);
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

		var collider = hit["collider"].As<Node>();
		var clickedUnit = FindUnit3DInParentChain(collider);
		if (clickedUnit == null || clickedUnit.IsEnemy) return;

		string type = clickedUnit.UnitId;
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		var viewportRect = GetViewport().GetVisibleRect();
		int selectCount = 0;

		foreach (var unit in AllUnits)
		{
			if (unit.IsEnemy || unit.IsBuilding || unit.UnitId != type) continue;

			var screenPos = camera.UnprojectPosition(unit.GlobalPosition);
			if (viewportRect.HasPoint(screenPos))
			{
				SelectUnit(unit);
				selectCount++;
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Selected {selectCount} {type.ToUpper()}(s)", new Color(0.5f, 1.0f, 0.5f));
		}
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
		if (ActiveEditorTool == EditorTool.SelectMove && GodotObject.IsInstanceValid(SelectedEditorObject))
		{
			if (SelectedEditorObject is Unit3D unit)
			{
				_editorService.SetCopiedObject(new EditorService.CopiedObjectTemplate {
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
				_editorService.SetCopiedObject(new EditorService.CopiedObjectTemplate {
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
				_editorService.SetCopiedObject(new EditorService.CopiedObjectTemplate {
					Type = "decal",
					Id = decalId,
					Rotation = decal.RotationDegrees.Y,
					Scale = decal.Scale.X,
					IsEnemy = false
				});
				MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Decal: {decalId.ToUpper()}");
			}
		}
		else
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Copy (select an object or area first)");
		}
	}

	public void TriggerPasteFromUI()
	{
		if (ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea)
		{
			if (_editorService.HasCopiedArea)
			{
				ActiveEditorTool = EditorTool.PasteArea;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PasteArea);
				MapEditorHUD.Instance?.ShowFeedbackExternal("Paste Mode Active - Click to paste");
				return;
			}
		}
		var copiedObjOpt = _editorService.GetCopiedObject();
		if (copiedObjOpt != null)
		{
			var copiedObj = copiedObjOpt.Value;
			if (copiedObj.Type == "unit")
			{
				ActiveEditorTool = EditorTool.PlaceUnit;
				_editorService.SetIsPastingObject(true);
				ActivePlaceId = copiedObj.Id;
				PlaceUnitIsEnemy = copiedObj.IsEnemy;
				EditorPlacementRotation = copiedObj.Rotation;
				EditorPlacementScale = copiedObj.Scale;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PlaceUnit);
			}
			else if (copiedObj.Type == "prop")
			{
				ActiveEditorTool = EditorTool.PlaceProp;
				_editorService.SetIsPastingObject(true);
				ActivePlaceId = copiedObj.Id;
				EditorPlacementRotation = copiedObj.Rotation;
				EditorPlacementScale = copiedObj.Scale;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PlaceProp);
			}
			else if (copiedObj.Type == "decal")
			{
				ActiveEditorTool = EditorTool.PlaceDecal;
				_editorService.SetIsPastingObject(true);
				ActivePlaceId = copiedObj.Id;
				EditorPlacementRotation = copiedObj.Rotation;
				EditorPlacementScale = copiedObj.Scale;
				MapEditorHUD.Instance?.SelectToolFromHotkey(EditorTool.PlaceDecal);
			}
			MapEditorHUD.Instance?.ShowFeedbackExternal($"Paste Mode Active - Placing {copiedObj.Id.ToUpper()}");
		}
		else
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Paste (copy an object or area first)");
		}
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
		ControlGroups[index] = groupUnits;
		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Assigned {groupUnits.Count} units to Control Group {index}", new Color(0.5f, 0.8f, 1.0f));
			InGameHUD.Instance.RefreshUI(SelectedUnits);
		}
	}

	public void RecallControlGroup(int index)
	{
		var group = ControlGroups[index];
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
		_inputService.ClearTargetingModes();
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
			if (def != null && def.Cooldown > 0f)
			{
				SetPlayerSpellCooldown(spellId, def.Cooldown);
			}

			if (def != null)
			{
				_fxService.SpawnAbilityEffect(this, def, position);
			}

			var targetIds = new List<int>();
			if (SelectedUnits.Count > 0 && !SelectedUnits[0].IsEnemy)
			{
				targetIds.Add(GetServerEntityId(SelectedUnits[0].Entity));
			}
			QueueClientCommand("spell", targetIds, position, 0, spellId);
			return;
		}

		int focusedIdx = Math.Clamp(CycleSelectionIndex, 0, Math.Max(0, SelectedUnits.Count - 1));
		Unit3D focusedUnit = SelectedUnits.Count > focusedIdx ? SelectedUnits[focusedIdx] : null;

		IUnit caster = null;
		if (focusedUnit != null && EcsWorld.IsAlive(focusedUnit.Entity))
		{
			caster = GetUnitWrapper(focusedUnit.Entity);
			_audioService?.PlayUnitSound(focusedUnit.UnitId, UnitSoundEvent.SpellCast, position);
		}
		OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));

		Entity casterEntity = focusedUnit != null && EcsWorld.IsAlive(focusedUnit.Entity) ? focusedUnit.Entity : Entity.Null;

		float cd = GetPlayerSpellCooldown(spellId);
		if (cd > 0f)
		{
			string displayName = def?.DisplayName ?? spellId;
			InGameHUD.Instance?.ShowFeedbackText($"{displayName} on cooldown: {cd:F1}s remaining", new Color(0.9f, 0.4f, 0.1f));
			return;
		}

		if (_inputService.TryExecuteSpellCast(_playerEntity, casterEntity, spellId, out float maxCd))
		{
			string displayName = def?.DisplayName ?? spellId;
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.ShowFeedbackText($"Cast: {displayName}", new Color(0.9f, 0.3f, 0.1f));
				UIManager.Instance.PlayClickSound();
			}

			if (def != null)
			{
				_fxService.SpawnAbilityEffect(this, def, position);
				if (def.Damage > 0f)
				{
					float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;
					_simulationService.DealSpellDamageAOE(new System.Numerics.Vector3(position.X, position.Y, position.Z), aoe, def.Damage, SelectedUnits.Count > 0 ? SelectedUnits[0].Entity : Entity.Null);
				}
				else if (def.Healing > 0f)
				{
					float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;
					_simulationService.HealAOE(new System.Numerics.Vector3(position.X, position.Y, position.Z), aoe, def.Healing);
				}
			}

			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	// Lanza una habilidad instantánea (compras/mejoras del mapa) usando la unidad
	// seleccionada como lanzador y su posición como objetivo; sin clic en el suelo.
	public void CastInstantAbility(string abilityId)
	{
		if (SelectedUnits.Count == 0 || !EcsWorld.IsAlive(SelectedUnits[0].Entity)) return;
		if (!EcsWorld.Has<Position>(SelectedUnits[0].Entity)) return;

		ref var pos = ref EcsWorld.Get<Position>(SelectedUnits[0].Entity);
		ExecuteSpellCast(abilityId, new Godot.Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z));
	}

	public void BuyItem(string itemId, Entity castleEntity)
	{
		string itemName = itemId;
		float costGold = 50f;
		if (ItemRegistry.TryGetValue(itemId, out var itemMeta))
		{
			if (!string.IsNullOrEmpty(itemMeta.Name)) itemName = itemMeta.Name;
			if (itemMeta.CostGold > 0) costGold = itemMeta.CostGold;
		}

		if (InGameHUD.Instance != null && InGameHUD.Instance.Gold >= costGold)
		{
			if (GameHost.TryGetUnit3D(castleEntity, out var castle3D))
			{
				var selectedEntity = SelectedUnits.Count > 0 ? SelectedUnits[0].Entity : Entity.Null;

				if (_inputService.BuyItem(itemId, _playerEntity, new System.Numerics.Vector3(castle3D.GlobalPosition.X, castle3D.GlobalPosition.Y, castle3D.GlobalPosition.Z), selectedEntity, out Entity targetUnitEntity))
				{
					var targetUnit = AllUnits.Find(u => u.Entity == targetUnitEntity);
					InGameHUD.Instance.Gold -= costGold;

					InGameHUD.Instance.ShowFeedbackText($"Bought {itemName} for {targetUnit.UnitId.ToUpper()}!", new Color(0.3f, 0.9f, 0.4f));
					UIManager.Instance?.PlayClickSound();
					InGameHUD.Instance.RefreshUI(SelectedUnits);
				}
				else
				{
					InGameHUD.Instance.ShowFeedbackText("Cannot buy item: No friendly combat units nearby!", new Color(1.0f, 0.2f, 0.2f));
					UIManager.Instance?.PlayWarningSound();
				}
			}
		}
		else
		{
			InGameHUD.Instance?.ShowFeedbackText("Cannot buy item: Insufficient gold!", new Color(1.0f, 0.2f, 0.2f));
			UIManager.Instance?.PlayWarningSound();
		}
	}

	public void UseItem(Unit3D unit, string itemId)
	{
		string itemName = itemId;
		if (ItemRegistry.TryGetValue(itemId, out var itemMeta) && !string.IsNullOrEmpty(itemMeta.Name))
		{
			itemName = itemMeta.Name;
		}

		if (_inputService.UseItem(unit.Entity, itemId, out float healedAmount))
		{
			InGameHUD.Instance?.ShowFeedbackText($"{unit.UnitId.ToUpper()} used {itemName} (+{healedAmount:F0} HP)!", new Color(0.3f, 0.9f, 0.4f));
			if (!string.IsNullOrEmpty(itemMeta.UseAbility) && GetAbilityDefinition(itemMeta.UseAbility) is AbilityDefinition abDef)
			{
				_fxService.SpawnAbilityEffect(this, abDef, unit.GlobalPosition);
			}
			FlashHealUnit(unit);
			_fxService.SpawnHealNumber(this, unit.GlobalPosition, healedAmount);

			UIManager.Instance?.PlayClickSound();
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
		else
		{
			if (EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<Health>(unit.Entity))
			{
				var hp = EcsWorld.Get<Health>(unit.Entity);
				if (hp.Current >= hp.Max)
				{
					InGameHUD.Instance?.ShowFeedbackText("Unit is already at full health!", new Color(0.8f, 0.8f, 0.8f));
					UIManager.Instance?.PlayWarningSound();
				}
			}
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

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			var bldMeta = UnitRegistry[type];
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.Gold -= bldMeta.CostGold;
				InGameHUD.Instance.Wood -= bldMeta.CostWood;
				InGameHUD.Instance.Stone -= bldMeta.CostStone;
				InGameHUD.Instance.ShowFeedbackText($"Constructing {bldMeta.Name}...", new Color(0.3f, 0.9f, 0.4f));
			}
			QueueClientCommand("build", new List<int>(), position, 0, type);
			if (!shiftHeld)
			{
				ActiveBuildingPlacementType = null;
				if (_buildingPreviewMesh != null)
				{
					_buildingPreviewMesh.QueueFree();
					_buildingPreviewMesh = null;
				}
				InGameHUD.Instance?.ExitBuildSubMenu();
			}
			return;
		}

		var meta = UnitRegistry[type];
		if (InGameHUD.Instance != null)
		{
			float clearance = type == "castle" ? 7f : 4f;
			if (!_inputService.TryPlaceBuilding(new System.Numerics.Vector3(position.X, position.Y, position.Z), clearance))
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot construct: Area is obstructed!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
				ActiveBuildingPlacementType = null;
				if (_buildingPreviewMesh != null)
				{
					_buildingPreviewMesh.QueueFree();
					_buildingPreviewMesh = null;
				}
				return;
			}

			if (InGameHUD.Instance.Gold >= meta.CostGold &&
				InGameHUD.Instance.Wood >= meta.CostWood &&
				InGameHUD.Instance.Stone >= meta.CostStone)
			{
				InGameHUD.Instance.Gold -= meta.CostGold;
				InGameHUD.Instance.Wood -= meta.CostWood;
				InGameHUD.Instance.Stone -= meta.CostStone;

				var buildingPos = new System.Numerics.Vector3(position.X, position.Y, position.Z);

				Unit3D firstWorker = null;
				foreach (var unit in SelectedUnits)
				{
					if (!unit.IsBuilding && !unit.IsEnemy && unit.UnitId == "worker")
					{
						firstWorker = unit;
						break;
					}
				}

				if (firstWorker != null)
				{
					if (shiftHeld && (_inputService.IsUnitActive(firstWorker.Entity) || EcsWorld.Has<BuildQueue>(firstWorker.Entity)))
					{
						if (!EcsWorld.Has<BuildQueue>(firstWorker.Entity))
							EcsWorld.Add(firstWorker.Entity, new BuildQueue());
						ref var buildQueue = ref EcsWorld.Get<BuildQueue>(firstWorker.Entity);
						buildQueue.TryEnqueue(type, buildingPos);
						InGameHUD.Instance.ShowFeedbackText($"Queued: Construct {meta.Name}", new Color(0.5f, 0.8f, 1.0f));
					}
					else
					{
						AssignBuildTaskToWorker(firstWorker.Entity, type, buildingPos);
						InGameHUD.Instance.ShowFeedbackText($"Constructing: {meta.Name}", new Color(0.3f, 0.9f, 0.4f));
					}

					foreach (var unit in SelectedUnits)
					{
						if (!unit.IsBuilding && !unit.IsEnemy && unit.UnitId == "worker" && unit != firstWorker)
						{
							if (EcsWorld.IsAlive(unit.Entity))
							{
								if (shiftHeld && (_inputService.IsUnitActive(unit.Entity) || EcsWorld.Has<BuildQueue>(unit.Entity)))
								{
									if (!EcsWorld.Has<BuildQueue>(unit.Entity))
										EcsWorld.Add(unit.Entity, new BuildQueue());
									ref var q = ref EcsWorld.Get<BuildQueue>(unit.Entity);
									q.TryEnqueue(type, buildingPos);
								}
								else if (!EcsWorld.Has<BuildTask>(unit.Entity))
								{
									var moveTo = new MoveTo(buildingPos);
									EcsWorld.SetOrAdd(unit.Entity, moveTo);
								}
							}
						}
					}
				}
				else
				{
					var playerOwner = _playerEntity.AsPlayerEntity(EcsWorld);
					string targetModel = meta.ModelPath;
					string modelPath = GetFallbackModelPath(targetModel, true);

					var bldEntity = CreateEcsUnit(type, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f, position, playerOwner);
					SpawnUnit3D(bldEntity, type, modelPath, position, true, false);
					InGameHUD.Instance.ShowFeedbackText($"Constructed: {meta.Name}", new Color(0.3f, 0.9f, 0.4f));
				}

				UIManager.Instance?.PlayClickSound();
			}
			else
			{
				InGameHUD.Instance.ShowFeedbackText("Cannot construct: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
				UIManager.Instance?.PlayWarningSound();
			}
		}

		if (!shiftHeld)
		{
			ActiveBuildingPlacementType = null;
			if (_buildingPreviewMesh != null)
			{
				_buildingPreviewMesh.QueueFree();
				_buildingPreviewMesh = null;
			}
			InGameHUD.Instance?.ExitBuildSubMenu();
		}
	}

	public void CancelLastQueuedUnit(Entity castleEntity)
	{
		if (EcsWorld.IsAlive(castleEntity) && EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity))
		{
			var prod = EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity);
			if (prod.UnitIds.Count > 0)
			{
				CancelQueuedUnitAt(castleEntity, prod.UnitIds.Count - 1);
			}
		}
	}

	public bool CanProduceUnits(Unit3D unit)
	{
		if (unit == null || !unit.IsBuilding) return false;
		if (BuildingRegistry.TryGetValue(unit.UnitId, out var meta))
		{
			if (meta.BuildOptions != null)
			{
				foreach (var opt in meta.BuildOptions)
				{
					if (UnitRegistry.ContainsKey(opt))
					{
						return true;
					}
				}
			}
			if (meta.Abilities != null)
			{
				foreach (var ab in meta.Abilities)
				{
					if (ab.Contains("spawn") || ab.Contains("train"))
					{
						return true;
					}
				}
			}
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
		_inputService.SetRallyPoint(building.Entity, new System.Numerics.Vector3(position.X, position.Y, position.Z), queue);
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
				if (_inputService.BuyWeaponsUpgrade(_playerEntity))
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
				if (_inputService.BuyShieldsUpgrade(_playerEntity))
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
				if (_inputService.BuyHarvestingUpgrade(_playerEntity))
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

		_inputService.IssueAttackMoveCommand(selectedEntities, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
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

		_inputService.HoldSelectedUnits(selectedEntities);

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

		_inputService.IssuePatrolCommand(selectedEntities, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z), isQueued);

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

		_inputService.IssueAttackMoveCommand(selectedEntities, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z), isQueued);

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
			bool blocked = _inputService.IsAreaObstructed(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), clearance);

			var mat = _buildingPreviewMesh.MaterialOverride as StandardMaterial3D;
			if (mat != null)
			{
				mat.AlbedoColor = blocked ? new Color(0.9f, 0.2f, 0.2f, 0.5f) : new Color(0.2f, 0.9f, 0.3f, 0.5f);
			}
		}
	}

	public void TrainUnitAtCastle(string unitId)
	{
		var meta = UnitRegistry[unitId];
		if (InGameHUD.Instance == null) return;

		var candidateBuildingEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (!unit.IsEnemy && unit.IsBuilding && EcsWorld.IsAlive(unit.Entity) && CanProduceUnits(unit))
			{
				candidateBuildingEntities.Add(unit.Entity);
			}
		}

		if (candidateBuildingEntities.Count == 0)
		{
			InGameHUD.Instance.ShowFeedbackText(TranslationServer.Translate("Cannot train unit: No producing building selected!"), new Color(1f, 0.3f, 0.3f));
			UIManager.Instance?.PlayWarningSound();
			return;
		}

		Entity targetBuildingEntity = _inputService.GetNextProductionStructure(candidateBuildingEntities);

		if (targetBuildingEntity == Entity.Null)
		{
			InGameHUD.Instance.ShowFeedbackText(TranslationServer.Translate("Training queue is full! (Max 5)"), new Color(1f, 0.3f, 0.3f));
			UIManager.Instance?.PlayWarningSound();
			return;
		}

		if (meta.PopCost > 0 && CurrentPopulation + meta.PopCost > MaxPopulation)
		{
			InGameHUD.Instance.ShowFeedbackText(string.Format(TranslationServer.Translate("Population cap reached! ({0}/{1})"), CurrentPopulation, MaxPopulation), new Color(1f, 0.3f, 0.3f));
			UIManager.Instance?.PlayWarningSound();
			return;
		}

		if (InGameHUD.Instance.Gold >= meta.CostGold && 
			InGameHUD.Instance.Wood >= meta.CostWood && 
			InGameHUD.Instance.Stone >= meta.CostStone)
		{
			if (_multiplayerActive && !IsServerActive())
			{
				QueueClientCommand("train", new List<int> { GetServerEntityId(targetBuildingEntity) }, Vector3.Zero, 0, unitId);
				InGameHUD.Instance.Gold -= meta.CostGold;
				InGameHUD.Instance.Wood -= meta.CostWood;
				InGameHUD.Instance.Stone -= meta.CostStone;

				InGameHUD.Instance.ShowFeedbackText(string.Format(TranslationServer.Translate("Queued {0} ({1}/{2} pop)"), meta.Name, CurrentPopulation, MaxPopulation), new Color(0.2f, 0.8f, 1f));
				UIManager.Instance?.PlayClickSound();
				InGameHUD.Instance.RefreshUI(SelectedUnits);
			}
			else
			{
				if (_inputService.TryQueueUnitAtCastle(_playerEntity, targetBuildingEntity, unitId, meta.PopCost, meta.ProductionTime))
				{
					InGameHUD.Instance.Gold -= meta.CostGold;
					InGameHUD.Instance.Wood -= meta.CostWood;
					InGameHUD.Instance.Stone -= meta.CostStone;

					InGameHUD.Instance.ShowFeedbackText(string.Format(TranslationServer.Translate("Queued {0} ({1}/{2} pop)"), meta.Name, CurrentPopulation, MaxPopulation), new Color(0.2f, 0.8f, 1f));
					UIManager.Instance?.PlayClickSound();
					InGameHUD.Instance.RefreshUI(SelectedUnits);
				}
			}
		}
		else
		{
			InGameHUD.Instance.ShowFeedbackText(TranslationServer.Translate("Cannot train unit: Insufficient resources!"), new Color(1f, 0.2f, 0.2f));
			UIManager.Instance?.PlayWarningSound();
		}
	}

	public void SaveCameraLocation(int slotIndex)
	{
		if (slotIndex < 1 || slotIndex > 4) return;
		var camera = MainCamera;
		if (camera == null) return;

		Vector3 pos = camera.GlobalPosition;
		float zoom = camera is CameraControl camCtrl ? camCtrl.TargetHeight : pos.Y;

		if (EcsWorld != null && EcsWorld.IsAlive(WorldEntity) && EcsWorld.Has<CameraState>(WorldEntity))
		{
			ref var state = ref EcsWorld.Get<CameraState>(WorldEntity);
			var slot = new CameraLocationSlot
			{
				Position = new System.Numerics.Vector3(pos.X, pos.Y, pos.Z),
				ZoomLevel = zoom,
				IsSet = true
			};
			switch (slotIndex)
			{
				case 1: state.LocationSlot1 = slot; break;
				case 2: state.LocationSlot2 = slot; break;
				case 3: state.LocationSlot3 = slot; break;
				case 4: state.LocationSlot4 = slot; break;
			}
		}

		InGameHUD.Instance?.ShowFeedbackText($"Camera Location {slotIndex} Saved", new Color(0.5f, 0.8f, 1.0f));
	}

	public void RecallCameraLocation(int slotIndex)
	{
		if (slotIndex < 1 || slotIndex > 4) return;
		if (EcsWorld == null || !EcsWorld.IsAlive(WorldEntity) || !EcsWorld.Has<CameraState>(WorldEntity)) return;

		ref var state = ref EcsWorld.Get<CameraState>(WorldEntity);
		CameraLocationSlot slot = slotIndex switch
		{
			1 => state.LocationSlot1,
			2 => state.LocationSlot2,
			3 => state.LocationSlot3,
			4 => state.LocationSlot4,
			_ => default
		};

		if (!slot.IsSet) return;

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
		var camera = GetViewport().GetCamera3D();
		if (camera != null && camera is CameraControl camCtrl)
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

		_inputService.StopSelectedUnits(selectedEntities);

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			QueueClientCommand("stop", targetIds, Vector3.Zero, 0, "");
		}
	}

	public void CancelQueuedUnitAt(Entity castleEntity, int index)
	{
		if (EcsWorld.IsAlive(castleEntity))
		{
			string? peekCancelledId = null;
			int popCost = 0;
			if (EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity))
			{
				var prod = EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity);
				if (index >= 0 && index < prod.UnitIds.Count)
				{
					peekCancelledId = prod.UnitIds[index];
					if (UnitRegistry.TryGetValue(peekCancelledId, out var metaPeek))
					{
						popCost = metaPeek.PopCost;
					}
				}
			}

			if (_multiplayerActive && !IsServerActive())
			{
				QueueClientCommand("cancel_train", new List<int> { GetServerEntityId(castleEntity) }, Vector3.Zero, index, "");
				if (EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity))
				{
					if (_inputService.CancelQueuedUnitAt(castleEntity, index, out string? cancelledId, out string? nextUnitId, popCost))
					{
						if (cancelledId != null)
						{
							var meta = UnitRegistry[cancelledId];
							if (InGameHUD.Instance != null)
							{
								InGameHUD.Instance.Gold += meta.CostGold;
								InGameHUD.Instance.Wood += meta.CostWood;
								InGameHUD.Instance.Stone += meta.CostStone;
								CurrentPopulation = Math.Max(0, CurrentPopulation - meta.PopCost);
								InGameHUD.Instance.ShowFeedbackText($"Cancelled {meta.Name} (Refunded {meta.CostGold}G, {meta.CostWood}W, {meta.CostStone}S)", new Color(1f, 0.8f, 0.2f));
							}
						}
						UIManager.Instance?.PlayClickSound();
						InGameHUD.Instance?.RefreshUI(SelectedUnits);
					}
				}
			}
			else if (EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity))
			{
				if (_inputService.CancelQueuedUnitAt(castleEntity, index, out string? cancelledId, out string? nextUnitId, popCost))
				{
					if (cancelledId != null)
					{
						var meta = UnitRegistry[cancelledId];
						if (InGameHUD.Instance != null)
						{
							InGameHUD.Instance.Gold += meta.CostGold;
							InGameHUD.Instance.Wood += meta.CostWood;
							InGameHUD.Instance.Stone += meta.CostStone;

							CurrentPopulation = Math.Max(0, CurrentPopulation - meta.PopCost);
							InGameHUD.Instance.ShowFeedbackText($"Cancelled {meta.Name} (Refunded {meta.CostGold}G, {meta.CostWood}W, {meta.CostStone}S)", new Color(1f, 0.8f, 0.2f));
						}
					}

					if (index == 0 && nextUnitId != null)
					{
						var nextMeta = UnitRegistry[nextUnitId];
						if (EcsWorld.Has<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity))
						{
							var p = EcsWorld.Get<Realm.Ecs.Components.Core.ProductionQueue>(castleEntity);
							p.BuildTime = nextMeta.ProductionTime;
							EcsWorld.Set(castleEntity, p);
						}
					}

					UIManager.Instance?.PlayClickSound();
					InGameHUD.Instance?.RefreshUI(SelectedUnits);
				}
			}
		}
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
		return _inputService.GetPlacementRadius(placeId, scale);
	}

	private Vector3? FindNearestFreePosition(Vector3 startPos, float checkRadius, float maxSearchDist = 20.0f)
	{
		var res = _inputService.FindNearestFreePosition(new System.Numerics.Vector3(startPos.X, startPos.Y, startPos.Z), checkRadius, maxSearchDist);
		if (res == null) return null;
		return new Vector3(res.Value.X, res.Value.Y, res.Value.Z);
	}

	private bool IsPositionBlocked(Vector3 pos, float radius, Node3D ignoreNode = null)
	{
		Entity ignoreEntity = Entity.Null;
		if (ignoreNode is Unit3D u) ignoreEntity = u.Entity;
		else if (ignoreNode is Prop3D p) ignoreEntity = p.Entity;
		return _inputService.IsPositionBlocked(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), radius, ignoreEntity);
	}

	public Unit3D SpawnUnitFromProduction(string unitId, System.Numerics.Vector3 position, bool isEnemy, Entity buildingEntity, bool isFromQueue = false)
	{
		if (!UnitRegistry.TryGetValue(unitId, out var meta)) return null;

		int ownerPeerId = _localPeerId;
		if (isEnemy)
		{
			ownerPeerId = -1; // Default to AI
			var mappingEntity = _worldEntity;
			if (mappingEntity != Entity.Null && EcsWorld.Has<NetworkMappingState>(mappingEntity))
			{
				var mapping = EcsWorld.Get<NetworkMappingState>(mappingEntity);
				foreach (var kvp in mapping.PeerIdToPlayerEntityMap)
				{
					if (kvp.Key != _localPeerId)
					{
						ownerPeerId = kvp.Key;
						break;
					}
				}
			}
		}
		bool actualIsEnemy = NetworkService.ArePeersEnemies(_localPeerId, ownerPeerId);
		Entity playerOwnerEntity = _playerEntity;
		if (_peerIdToPlayerEntityMap != null && _peerIdToPlayerEntityMap.TryGetValue(ownerPeerId, out var pe) && EcsWorld.IsAlive(pe))
		{
			playerOwnerEntity = pe;
		}
		else if (actualIsEnemy && _enemyPlayerEntity != Entity.Null && EcsWorld.IsAlive(_enemyPlayerEntity))
		{
			playerOwnerEntity = _enemyPlayerEntity;
		}
		var playerOwner = playerOwnerEntity.AsPlayerEntity(EcsWorld);

		string targetModel = meta.ModelPath;
		string modelPath = GetFallbackModelPath(targetModel, false);

		string name = actualIsEnemy ? _unitSpawnService.GetEnemyUnitName(unitId, meta.Name) : meta.Name;

		var godotPosition = new Vector3(position.X, position.Y, position.Z);
		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, godotPosition, playerOwner);

		int parentPlayer = actualIsEnemy ? 1 : 0;
		if (EcsWorld.IsAlive(buildingEntity) && EcsWorld.Has<UnitOwnerPlayer>(buildingEntity))
		{
			parentPlayer = EcsWorld.Get<UnitOwnerPlayer>(buildingEntity).PlayerIndex;
			actualIsEnemy = NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, parentPlayer);
		}

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, godotPosition, false, actualIsEnemy, isFromQueue, parentPlayer);

		if (meta.Speed > 0f)
		{
			if (EcsWorld.IsAlive(buildingEntity) && EcsWorld.Has<Realm.Ecs.Components.Core.RallyPoint>(buildingEntity))
			{
				var rp = EcsWorld.Get<Realm.Ecs.Components.Core.RallyPoint>(buildingEntity);
				if (rp.Count > 0)
				{
					EcsWorld.Add(entity, new MoveTo(rp.Waypoints[0]));
					if (rp.Count > 1)
					{
						var wq = new WaypointQueue(rp.Waypoints[1]);
						for (int i = 2; i < rp.Count; i++)
						{
							wq.Add(rp.Waypoints[i]);
						}
						EcsWorld.Add(entity, wq);
					}
				}
			}
			else
			{
				EcsWorld.Add(entity, new MoveTo(position));
			}
		}
		return unit3D;
	}

	private void IssueResumeConstructionCommand(Unit3D targetBuilding, bool shiftHeld)
	{
		if (targetBuilding == null || !EcsWorld.IsAlive(targetBuilding.Entity)) return;
		if (!EcsWorld.Has<Realm.Ecs.Components.Tags.UnderConstruction>(targetBuilding.Entity)) return;

		var pos = targetBuilding.GlobalPosition;
		var bPos = new System.Numerics.Vector3(pos.X, pos.Y, pos.Z);

		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.IsEnemy || unit.UnitId != "worker") continue;

			if (shiftHeld && (_inputService.IsUnitActive(unit.Entity) || EcsWorld.Has<BuildQueue>(unit.Entity)))
			{
				if (!EcsWorld.Has<BuildQueue>(unit.Entity))
					EcsWorld.Add(unit.Entity, new BuildQueue());
				ref var q = ref EcsWorld.Get<BuildQueue>(unit.Entity);
				q.TryEnqueue(targetBuilding.UnitId, bPos, targetBuilding.Entity);
			}
			else
			{
				ClearUnitOrders(unit.Entity);
				
				if (EcsWorld.Has<ConstructionState>(targetBuilding.Entity))
				{
					var cState = EcsWorld.Get<ConstructionState>(targetBuilding.Entity);
					var buildTask = new BuildTask(targetBuilding.Entity, cState.TotalBuildTime);
					buildTask.Progress = cState.Progress;
					EcsWorld.SetOrAdd(unit.Entity, buildTask);

					EcsWorld.SetOrAdd(unit.Entity, new MoveTo(bPos));
				}
			}
		}

		if (InGameHUD.Instance != null)
		{
			InGameHUD.Instance.ShowFeedbackText($"Resuming construction...", new Color(0.3f, 0.9f, 0.4f));
			InGameHUD.Instance.RefreshUI(SelectedUnits);
		}
	}

	public void HandleMinimapRightClick(Vector3 minimapWorldPos)
	{
		if (ActiveSpellTargeting != null || ActiveCommandTargeting != null || ActiveBuildingPlacementType != null)
		{
			ActiveSpellTargeting = null;
			ActiveCommandTargeting = null;
			CancelBuildingPlacement();
			Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
			return;
		}

		if (SelectedUnits.Count == 0) return;

		bool anyFriendlySelected = false;
		foreach (var su in SelectedUnits)
		{
			if (!su.IsEnemy)
			{
				anyFriendlySelected = true;
				break;
			}
		}
		if (!anyFriendlySelected) return;

		if (SelectedUnits.Count == 1 && !SelectedUnits[0].IsEnemy && CanProduceUnits(SelectedUnits[0]))
		{
			SetRallyPoint(SelectedUnits[0], minimapWorldPos);
			return;
		}

		var from = new Vector3(minimapWorldPos.X, 200f, minimapWorldPos.Z);
		var to = new Vector3(minimapWorldPos.X, -100f, minimapWorldPos.Z);
		var spaceState = GetWorld3D().DirectSpaceState;
		_cachedRaycastQuery ??= new PhysicsRayQueryParameters3D();
		_cachedRaycastQuery.From = from;
		_cachedRaycastQuery.To = to;
		var result = spaceState.IntersectRay(_cachedRaycastQuery);

		Unit3D clickedUnit = null;
		Prop3D clickedProp = null;
		Vector3 hitPos = minimapWorldPos;

		if (result != null && result.Count > 0)
		{
			if (result.ContainsKey("position"))
			{
				hitPos = result["position"].AsVector3();
			}
			if (result.ContainsKey("collider"))
			{
				var collider = result["collider"].As<Node>();
				clickedUnit = FindUnit3DInParentChain(collider);
				clickedProp = FindProp3DInParentChain(collider);
			}
		}

		bool shiftHeld = Input.IsKeyPressed(Key.Shift);

		if (clickedUnit != null && clickedUnit.IsEnemy && clickedUnit.Visible)
		{
			IssueAttackCommand(clickedUnit, shiftHeld);
		}
		else if (clickedUnit != null && !clickedUnit.IsEnemy && clickedUnit != SelectedUnits.Find(u => !u.IsEnemy))
		{
			if (clickedUnit.IsBuilding && EcsWorld.Has<Realm.Ecs.Components.Tags.UnderConstruction>(clickedUnit.Entity))
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
	}
}
