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
private void HandleEditorKeyInput(InputEventKey editorKeyEvent)
{
		bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
		bool shiftPressed = Input.IsKeyPressed(Key.Shift);

		if (editorKeyEvent.Keycode == Key.Escape)
		{
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

			if (_is3DDragOperationActive)
			{
				_is3DDragOperationActive = false;
				_is3DLeftClickDown = false;
				GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(false);
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
		}

		if (editorKeyEvent.Keycode == Key.Z && !ctrlPressed && !shiftPressed)
		{
			CycleCameraZoom();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.H && ctrlPressed)
		{
			if (GameContext.MapEditorHud != null)
			{
				GameContext.MapEditorHud.Visible = !GameContext.MapEditorHud.Visible;
				GameContext.MapEditorHud.ShowFeedbackExternal(GameContext.MapEditorHud.Visible ? "HUD: Visible" : "HUD: Hidden");
			}

			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.H && !ctrlPressed && !shiftPressed)
		{
			GameContext.MapEditorHud?.ToggleHelpPanelExternal();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.M && !ctrlPressed && !shiftPressed)
		{
			EditorBlockMode = !EditorBlockMode;
			GameContext.MapEditorHud?.UpdateBlockModeExternal(EditorBlockMode);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.Q && !ctrlPressed && !shiftPressed && ActiveEditorTool == EditorTool.PaintTexture)
		{
			if (MainCamera is CameraControl paintFreeCameraCtrl && paintFreeCameraCtrl.IsFreeCamera)
			{
				return;
			}

			GameContext.MapEditorHud?.CyclePaintSurfaceMode();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.Q && !ctrlPressed && !shiftPressed)
		{
			if (MainCamera is CameraControl camCtrl && camCtrl.IsFreeCamera)
			{
				return;
			}

			GameContext.MapEditorHud?.SelectToolFromHotkey(EditorTool.SelectMove);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.I && !ctrlPressed && !shiftPressed)
		{
			GameContext.MapEditorHud?.SelectToolFromHotkey(EditorTool.Eyedropper);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.Delete)
		{
			if (ActiveEditorTool == EditorTool.SelectMove && SelectedEditorObjectCount > 0)
			{
				var (success, deletedCount) = DeleteSelectedObjects();
				if (success)
				{
					string deletedMessage = deletedCount > 1
						? string.Format(LocalizationManager.Translate("{0} Objects Deleted"), deletedCount)
						: LocalizationManager.Translate("Deleted Object");
					GameContext.MapEditorHud?.ShowFeedbackExternal(deletedMessage);
					GetViewport().SetInputAsHandled();
					return;
				}
			}
		}

		if (editorKeyEvent.Keycode == Key.Z && ctrlPressed)
		{
			if (shiftPressed)
			{
				EditorHistoryManager.Redo();
				GameContext.MapEditorHud?.ShowFeedbackExternal("Redo Action performed");
			}
			else
			{
				EditorHistoryManager.Undo();
				GameContext.MapEditorHud?.ShowFeedbackExternal("Undo Action performed");
			}

			EditorHasUnsavedChanges = true;
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.Y && ctrlPressed)
		{
			EditorHistoryManager.Redo();
			GameContext.MapEditorHud?.ShowFeedbackExternal("Redo Action performed");
			EditorHasUnsavedChanges = true;
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.S && ctrlPressed)
		{
			GameContext.MapEditorHud?.SaveMapActionExternal();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.O && ctrlPressed)
		{
			GameContext.MapEditorHud?.LoadMapAction();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.P && ctrlPressed)
		{
			SaveMapToFile();
			GameContext.MapEditorHud?.ShowFeedbackExternal("Map published & compiled!");
			GetViewport().SetInputAsHandled();
			return;
		}

		if ((editorKeyEvent.Keycode == Key.O || editorKeyEvent.Keycode == Key.V) && !ctrlPressed && !shiftPressed)
		{
			GameContext.EditorService.Tools.GridMode = GameContext.EditorService.Tools.GridMode switch
			{
				GridOverlayMode.Off => GridOverlayMode.Grid,
				GridOverlayMode.Grid => GridOverlayMode.Polar,
				GridOverlayMode.Polar => GridOverlayMode.Both,
				GridOverlayMode.Both => GridOverlayMode.Off,
				_ => GridOverlayMode.Off
			};
			UpdateGridOverlayVisibility();
			GameContext.MapEditorHud?.UpdateGridOverlayExternal(GameContext.EditorService.Tools.GridMode);
			string modeName = GameContext.EditorService.Tools.GridMode switch
			{
				GridOverlayMode.Off => "OFF",
				GridOverlayMode.Grid => "GRID",
				GridOverlayMode.Polar => "POLAR",
				GridOverlayMode.Both => "GRID + POLAR",
				_ => "OFF"
			};
			GameContext.MapEditorHud?.ShowFeedbackExternal($"Overlay Mode: {modeName}");
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.U && !ctrlPressed && !shiftPressed)
		{
			ActiveEditorTool = ActiveEditorTool == EditorTool.Measure ? EditorTool.None : EditorTool.Measure;
			GameContext.MapEditorHud?.SelectToolFromHotkey(ActiveEditorTool);
			if (ActiveEditorTool != EditorTool.Measure)
			{
				ClearMeasureVisuals();
				GameContext.MapEditorHud?.ClearMeasureTelemetry();
			}
			else
			{
				GameContext.MapEditorHud?.ShowFeedbackExternal("Tape Measure Active - Click Point A then Point B");
			}

			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.G && ctrlPressed)
		{
			EditorSnapToGrid = !EditorSnapToGrid;
			GameContext.MapEditorHud?.UpdateGridSnapExternal(EditorSnapToGrid);
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
				GameContext.MapEditorHud?.UpdateCameraAngleButtonText(topDown);
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (editorKeyEvent.Keycode == Key.R && !ctrlPressed && !shiftPressed)
		{
			if (MainCamera is CameraControl freeCameraCtrl && freeCameraCtrl.IsFreeCamera)
			{
				return;
			}

			MainCamera?.Rotate90Degrees();
			GetViewport().SetInputAsHandled();
			return;
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
					GameContext.EditorService.Clipboard.SetCopiedObject(new CopiedObjectTemplate
					{
						Type = CopiedObjectKind.Unit,
						Id = unit.UnitId,
						Rotation = unit.RotationDegrees.Y,
						Scale = unit.Scale.X,
						Player = unit.Player
					});
					GameContext.MapEditorHud?.ShowFeedbackExternal($"Copied Unit: {unit.UnitId.ToUpper()}");
				}
				else if (SelectedEditorObject is PropEntityProxyNode propProxy)
				{
					GameContext.EditorService.Clipboard.SetCopiedObject(new CopiedObjectTemplate
					{
						Type = CopiedObjectKind.Prop,
						Id = propProxy.PropId,
						Rotation = propProxy.RotationDegrees.Y,
						Scale = propProxy.Scale.X,
						Player = 0
					});
					GameContext.MapEditorHud?.ShowFeedbackExternal($"Copied Prop: {propProxy.PropId.ToUpper()}");
				}
				else if (SelectedEditorObject is Prop3D prop)
				{
					GameContext.EditorService.Clipboard.SetCopiedObject(new CopiedObjectTemplate
					{
						Type = CopiedObjectKind.Prop,
						Id = prop.PropId,
						Rotation = prop.RotationDegrees.Y,
						Scale = prop.Scale.X,
						Player = 0
					});
					GameContext.MapEditorHud?.ShowFeedbackExternal($"Copied Prop: {prop.PropId.ToUpper()}");
				}
				else if (SelectedEditorObject is Decal decal)
				{
					string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
					GameContext.EditorService.Clipboard.SetCopiedObject(new CopiedObjectTemplate
					{
						Type = CopiedObjectKind.Decal,
						Id = decalId,
						Rotation = decal.RotationDegrees.Y,
						Scale = decal.Scale.X,
						Player = 0
					});
					GameContext.MapEditorHud?.ShowFeedbackExternal($"Copied Decal: {decalId.ToUpper()}");
				}

				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (editorKeyEvent.Keycode == Key.V && ctrlPressed)
		{
			if (ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea)
			{
				if (GameContext.EditorService.Clipboard.HasCopiedArea)
				{
					ActiveEditorTool = EditorTool.PasteArea;
					GameContext.MapEditorHud?.SelectToolFromHotkey(EditorTool.PasteArea);
					GameContext.MapEditorHud?.ShowFeedbackExternal("Paste Mode Active - Click to paste");
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			var copiedObj = GameContext.EditorService.Clipboard.GetCopiedObject();
			if (copiedObj != null)
			{
				var hit = RaycastTerrainFromMouse(GetViewport().GetMousePosition());
				if (hit != null && hit.ContainsKey("position"))
				{
					Vector3 spawnPos = CalculatePlacementPosition(hit["position"].AsVector3());

					spawnPos.Y = GetTerrainHeightAt(spawnPos);
					var cop = copiedObj.Value;
					Node? pastedNode = null;
					IEditorAction? action = null;
					if (cop.Type == CopiedObjectKind.Unit)
					{
						pastedNode = SpawnUnitExternal(cop.Id, spawnPos, cop.Rotation, cop.Scale, cop.Player);
						if (pastedNode != null)
						{
							action = new ObjectSpawnAction("unit", cop.Id, spawnPos, cop.Rotation, cop.Scale, pastedNode, cop.Player);
							GameContext.MapEditorHud?.ShowFeedbackExternal($"Pasted Unit: {cop.Id.ToUpper()}");
						}
					}
					else if (cop.Type == CopiedObjectKind.Prop)
					{
						pastedNode = SpawnPropExternalWithParams(cop.Id, spawnPos, cop.Rotation, cop.Scale);
						if (pastedNode != null)
						{
							action = new ObjectSpawnAction("prop", cop.Id, spawnPos, cop.Rotation, cop.Scale, false, pastedNode);
							GameContext.MapEditorHud?.ShowFeedbackExternal($"Pasted Prop: {cop.Id.ToUpper()}");
						}
					}
					else if (cop.Type == CopiedObjectKind.Decal)
					{
						pastedNode = SpawnDecalExternalWithParams(cop.Id, spawnPos, cop.Rotation, cop.Scale);
						if (pastedNode != null)
						{
							action = new ObjectSpawnAction("decal", cop.Id, spawnPos, cop.Rotation, cop.Scale, false, pastedNode);
							GameContext.MapEditorHud?.ShowFeedbackExternal($"Pasted Decal: {cop.Id.ToUpper()}");
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
								Node? mNode = null;
								if (cop.Type == CopiedObjectKind.Unit)
								{
									mNode = SpawnUnitExternal(cop.Id, mPos, t.Rotation, cop.Scale, cop.Player);
									if (mNode != null)
									{
										actionsList.Add(new ObjectSpawnAction("unit", cop.Id, mPos, t.Rotation, cop.Scale, mNode, cop.Player));
									}
								}
								else if (cop.Type == CopiedObjectKind.Prop)
								{
									mNode = SpawnPropExternalWithParams(cop.Id, mPos, t.Rotation, cop.Scale);
									if (mNode != null)
									{
										actionsList.Add(new ObjectSpawnAction("prop", cop.Id, mPos, t.Rotation, cop.Scale, false, mNode));
									}
								}
								else if (cop.Type == CopiedObjectKind.Decal)
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
			if (ActiveEditorTool == EditorTool.SelectMove && SelectedEditorObject is Node3D selectedNode && GodotObject.IsInstanceValid(SelectedEditorObject))
			{
				Vector3 spawnPos;
				var hit = RaycastTerrainFromMouse(GetViewport().GetMousePosition());
				if (hit != null && hit.ContainsKey("position"))
				{
					spawnPos = CalculatePlacementPosition(hit["position"].AsVector3());
				}
				else
				{
					spawnPos = selectedNode.Position + new Vector3(2.0f, 0.0f, 2.0f);
				}

				spawnPos.Y = GetTerrainHeightAt(spawnPos);
				float rotY = selectedNode.RotationDegrees.Y;
				float scaleVal = selectedNode.Scale.X;
				Node? clonedNode = null;
				IEditorAction? action = null;
				if (SelectedEditorObject is Unit3D unit)
				{
					clonedNode = SpawnUnitExternal(unit.UnitId, spawnPos, rotY, scaleVal, unit.Player);
					if (clonedNode != null)
					{
						action = new ObjectSpawnAction("unit", unit.UnitId, spawnPos, rotY, scaleVal, clonedNode, unit.Player);
						GameContext.MapEditorHud?.ShowFeedbackExternal($"Duplicated Unit: {unit.UnitId.ToUpper()}");
					}
				}
				else if (SelectedEditorObject is PropEntityProxyNode propProxy)
				{
					clonedNode = SpawnPropExternalWithParams(propProxy.PropId, spawnPos, rotY, scaleVal);
					if (clonedNode != null)
					{
						action = new ObjectSpawnAction("prop", propProxy.PropId, spawnPos, rotY, scaleVal, false, clonedNode);
						GameContext.MapEditorHud?.ShowFeedbackExternal($"Duplicated Prop: {propProxy.PropId.ToUpper()}");
					}
				}
				else if (SelectedEditorObject is Prop3D prop)
				{
					clonedNode = SpawnPropExternalWithParams(prop.PropId, spawnPos, rotY, scaleVal);
					if (clonedNode != null)
					{
						action = new ObjectSpawnAction("prop", prop.PropId, spawnPos, rotY, scaleVal, false, clonedNode);
						GameContext.MapEditorHud?.ShowFeedbackExternal($"Duplicated Prop: {prop.PropId.ToUpper()}");
					}
				}
				else if (SelectedEditorObject is Decal decal)
				{
					string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
					clonedNode = SpawnDecalExternalWithParams(decalId, spawnPos, rotY, scaleVal);
					if (clonedNode != null)
					{
						action = new ObjectSpawnAction("decal", decalId, spawnPos, rotY, scaleVal, false, clonedNode);
						GameContext.MapEditorHud?.ShowFeedbackExternal($"Duplicated Decal: {decalId.ToUpper()}");
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
			EditorBrushRadius = Mathf.Max(EditorToolSettings.MinBrushRadius, EditorBrushRadius - 1.0f);
			GameContext.MapEditorHud?.UpdateBrushSizeExternal(EditorBrushRadius);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.Bracketright)
		{
			EditorBrushRadius = Mathf.Min(EditorToolSettings.MaxBrushRadius, EditorBrushRadius + 1.0f);
			GameContext.MapEditorHud?.UpdateBrushSizeExternal(EditorBrushRadius);
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
					else if (node3D is PropEntityProxyNode px) radius = GameContext.InputService.GetPlacementRadius(px.PropId, px.Scale.X);
					else if (node3D is Prop3D p) radius = GameContext.InputService.GetPlacementRadius(p.PropId, p.Scale.X);

					if (IsPositionBlocked(targetPos, radius, node3D))
					{
						valid = false;
					}
				}

				if (valid)
				{
					targetPos.Y = GetTerrainHeightAt(targetPos);
					var action = new ObjectTransformAction(
						node3D,
						node3D.Position, targetPos,
						node3D.RotationDegrees, node3D.RotationDegrees,
						node3D.Scale, node3D.Scale
					);
					node3D.Position = targetPos;
					if (node3D is Unit3D unit && World.IsAlive(unit.Entity))
					{
						GameContext.InputService.SetEntityPosition(unit.Entity, new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
					}
					else if (node3D is PropEntityProxyNode propProxy && World.IsAlive(propProxy.Entity))
					{
						World.Set(propProxy.Entity, new Position(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z)));
						GameContext.PropMultiMesh?.UpdateInstanceTransform(propProxy.Entity, targetPos, propProxy.RotationDegrees.Y, propProxy.Scale.X);
						UpdateSelectedPropRingPosition(targetPos);
					}
					else if (node3D is Prop3D prop)
					{
						GameContext.PropMultiMesh?.MarkDirty(prop.PropId);
					}

					EditorHistoryManager.RecordAction(action);
					EditorHasUnsavedChanges = true;
					GameContext.MapEditorHud?.UpdateSelectedObjectInfo();
					GameContext.MapEditorHud?.ShowFeedbackExternal("Object nudged");
				}
				else
				{
					GameContext.UserInterface?.PlayWarningSound();
				}
			}

			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.Minus)
		{
			EditorBrushStrength = Mathf.Max(EditorToolSettings.MinBrushStrength, EditorBrushStrength - 0.5f);
			GameContext.MapEditorHud?.UpdateBrushStrengthExternal(EditorBrushStrength);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.Equal)
		{
			EditorBrushStrength = Mathf.Min(EditorToolSettings.MaxBrushStrength, EditorBrushStrength + 0.5f);
			GameContext.MapEditorHud?.UpdateBrushStrengthExternal(EditorBrushStrength);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.Key0)
		{
			GameContext.MapEditorHud?.SelectToolFromHotkey(EditorTool.Water);
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
				GameContext.MapEditorHud?.SelectToolFromHotkey(targetTool);
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (editorKeyEvent.Keycode == Key.B && !ctrlPressed && !shiftPressed)
		{
			EditorBrushIsSquare = !EditorBrushIsSquare;
			UpdateBrushMesh();
			GameContext.MapEditorHud?.UpdateBrushShapeExternal(EditorBrushIsSquare);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.T && !ctrlPressed && !shiftPressed)
		{
			GenerateNewRandomPlacementRotationAndScale();
			GameContext.MapEditorHud?.ShowFeedbackExternal("Re-randomized Rotation & Scale");
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
			GameContext.MapEditorHud?.UpdateEnvLightingSelection(res.TimeOfDayIndex);
			GameContext.MapEditorHud?.ShowFeedbackExternal(string.Format(TranslationServer.Translate("Lighting: {0} {1}"), icon, TranslationServer.Translate(timeName)));
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.K && !ctrlPressed && !shiftPressed && !editorKeyEvent.AltPressed)
		{
			if (GameContext.EnvironmentService != null)
			{
				string nextWeather = GameContext.EnvironmentService.CycleWeather();
				string icon = nextWeather switch
				{
					"rain" => "🌧️",
					"snow" => "❄️",
					"fog" => "🌫️",
					_ => "☀️"
				};
				GameContext.MapEditorHud?.UpdateEnvWeatherSelection(nextWeather);
				GameContext.MapEditorHud?.ShowFeedbackExternal(string.Format(TranslationServer.Translate("Weather: {0} {1}"), icon, TranslationServer.Translate(nextWeather.Capitalize())));
			}

			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.F8 || (editorKeyEvent.Keycode == Key.Y && !ctrlPressed && !shiftPressed && !editorKeyEvent.AltPressed))
		{
			GameContext.MapEditorHud?.ToggleFreeCamera();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (editorKeyEvent.Keycode == Key.F9)
		{
			GameContext.MapEditorHud?.ToggleShadows();
			GetViewport().SetInputAsHandled();
			return;
		}
}
}
