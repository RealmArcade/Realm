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
	private void HandleEditorLeftClick(InputEventMouseButton editorMouseBtn)
	{
		if (_leftClickInitiatedOverUI || IsMouseOverUI() || FloatingDialogBase.HasAnyDialogOpen)
		{
			return;
		}

		var terrainHit = RaycastTerrainFromMouse(editorMouseBtn.Position);
		var hit = RaycastFromMouse(editorMouseBtn.Position);
		Vector3? hitPosOrNull = null;
		if (terrainHit != null && terrainHit.ContainsKey("position")) hitPosOrNull = terrainHit["position"].AsVector3();
		else if (hit != null && hit.ContainsKey("position")) hitPosOrNull = hit["position"].AsVector3();

		if (hitPosOrNull is Vector3 hitPos)
		{

			if (EditorSnapToGrid && GroundTerrain != null)
			{
				hitPos = EditorGridSnapping.SnapToCellCenter(hitPos);
			}

			if (IsObjectPlacementTool(ActiveEditorTool) && string.IsNullOrWhiteSpace(ActivePlaceId))
			{
				GameContext.MapEditorHud?.ShowConfirmationDialog(TranslationServer.Translate("Please create a template first"), null, confirmText: TranslationServer.Translate("OK"), showCancel: false);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (ActiveEditorTool == EditorTool.PlaceUnit)
			{
				if (!TryGetValidPlacementContext(hitPos, out float placementRot, out float scaleVal, out Vector3 spawnPos)) return;

				int placePlayer = GameContext.EditorService.Tools.PlaceUnitPlayerIndex;
				var unit = SpawnUnitExternal(ActivePlaceId, spawnPos, placementRot, scaleVal, placePlayer);
				if (unit != null)
				{
						float radius = GameContext.InputService.GetPlacementRadius(ActivePlaceId, scaleVal);
					var actions = new List<IEditorAction>
					{
						new ObjectSpawnAction("unit", ActivePlaceId, spawnPos, placementRot, scaleVal, unit, placePlayer)
					};
					if (EditorMirrorMode != MirrorMode.None)
					{
						foreach (var t in GetMirroredTransforms(spawnPos, placementRot))
						{
							Vector3 mPos = t.Position;
							mPos.Y = GetTerrainHeightAt(mPos);
							if (IsPositionBlocked(mPos, radius)) continue;
							var mUnit = SpawnUnitExternal(ActivePlaceId, mPos, t.Rotation, scaleVal, placePlayer);
							if (mUnit != null)
							{
								actions.Add(new ObjectSpawnAction("unit", ActivePlaceId, mPos, t.Rotation, scaleVal, mUnit, placePlayer));
							}
						}
					}

					var composite = new CompositeAction(actions);
					EditorHistoryManager.RecordAction(composite);
					EditorHasUnsavedChanges = true;
				}

				GenerateNewRandomPlacementRotationAndScale();
				GameContext.EditorService.Interaction.SetIsPastingObject(false);
				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.PlaceProp)
			{
				if (!TryGetValidPlacementContext(hitPos, out float placementRot, out float scaleVal, out Vector3 spawnPos)) return;

				var prop = SpawnPropExternalWithParams(ActivePlaceId, spawnPos, placementRot, scaleVal);
				if (prop != null)
				{
					var actions = new List<IEditorAction>
					{
						new ObjectSpawnAction("prop", ActivePlaceId, spawnPos, placementRot, scaleVal, false, prop)
					};
					if (EditorMirrorMode != MirrorMode.None)
					{
							float propRadius = GameContext.InputService.GetPlacementRadius(ActivePlaceId, scaleVal);
						foreach (var t in GetMirroredTransforms(spawnPos, placementRot))
						{
							Vector3 mPos = t.Position;
							mPos.Y = GetTerrainHeightAt(mPos);
								if (IsPositionBlocked(mPos, propRadius)) continue;
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
				GameContext.EditorService.Interaction.SetIsPastingObject(false);
				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.PlaceItem)
			{
				if (!GameContext.EditorService.Interaction.HasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
				float placementRot = (GameContext.EditorService.Tools.RandomRotation && !GameContext.EditorService.Interaction.IsPastingObject) ? GameContext.EditorService.Interaction.CachedRandomRotation : GameContext.EditorService.Tools.PlacementRotation;
				Vector3 spawnPos = CalculatePlacementPosition(hitPos);
				spawnPos.Y = GetTerrainHeightAt(spawnPos);

				var lootItem = SpawnItemExternal(ActivePlaceId, spawnPos);
				if (lootItem != null)
				{
					var actions = new List<IEditorAction>
					{
						new ObjectSpawnAction("item", ActivePlaceId, spawnPos, placementRot, 1f, lootItem)
					};
					if (EditorMirrorMode != MirrorMode.None)
					{
						foreach (var t in GetMirroredTransforms(spawnPos, placementRot))
						{
							Vector3 mPos = t.Position;
							mPos.Y = GetTerrainHeightAt(mPos);
							var mirroredItem = SpawnItemExternal(ActivePlaceId, mPos);
							if (mirroredItem != null)
							{
								actions.Add(new ObjectSpawnAction("item", ActivePlaceId, mPos, t.Rotation, 1f, mirroredItem));
							}
						}
					}

					var composite = new CompositeAction(actions);
					EditorHistoryManager.RecordAction(composite);
					EditorHasUnsavedChanges = true;
				}

				GenerateNewRandomPlacementRotationAndScale();
				GameContext.EditorService.Interaction.SetIsPastingObject(false);
				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.PlaceDecal)
			{
				if (!TryGetValidPlacementContext(hitPos, out float placementRot, out float scaleVal, out Vector3 spawnPos)) return;

				Vector3 hitNormal = (terrainHit != null && terrainHit.ContainsKey("normal")) ? terrainHit["normal"].AsVector3() : (hit != null && hit.ContainsKey("normal") ? hit["normal"].AsVector3() : GetTerrainNormalAt(hitPos));
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
				GameContext.EditorService.Interaction.SetIsPastingObject(false);
				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.PlaceVfx)
			{
				if (EditorClumpMode) return;
				if (!GameContext.EditorService.Interaction.HasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
				float placementRot = (GameContext.EditorService.Tools.RandomRotation && !GameContext.EditorService.Interaction.IsPastingObject) ? GameContext.EditorService.Interaction.CachedRandomRotation : GameContext.EditorService.Tools.PlacementRotation;
				float scaleVal = (GameContext.EditorService.Tools.RandomScale && !GameContext.EditorService.Interaction.IsPastingObject) ? GameContext.EditorService.Interaction.CachedRandomScale : EditorPlacementScale;
				float safeScale = scaleVal <= 0.001f ? 1.0f : scaleVal;

				VfxAttachmentConfig config;
				if (GameContext.RegistryService.TryGetVfxConfig(ActivePlaceId, out var regCfg))
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
					Vector3 hitNormal = hit != null && hit.ContainsKey("normal") ? hit["normal"].AsVector3() : GetTerrainNormalAt(hitPos);
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
				GameContext.EditorService.Interaction.SetIsPastingObject(false);
				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.DeleteObject)
			{
				var collider = hit != null && hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
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
				string mode = GameContext.MapEditorHud != null ? GameContext.MapEditorHud.GetEyedropperMode() : "all";
				var collider = hit != null && hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
				Node? clickedNode = null;

				if (mode == "all" || mode == "3d")
				{
					if (collider != null)
					{
						clickedNode = FindUnit3DInParentChain(collider);
						if (clickedNode == null)
						{
							clickedNode = (Node?)(collider as PropEntityProxyNode);
							if (clickedNode == null)
							{
								clickedNode = FindProp3DInParentChain(collider);
							}
						}
					}

					if (clickedNode == null && GameContext.World != null)
					{
						string? closestStaticPropId = null;
						float closestStaticDist = 3.0f;
						var propQuery = QueryCache.AllPropIdentityAndPositionQuery;
						World.Query(in propQuery, (Entity entity, ref PropIdentity pId, ref Position pPos) =>
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
							if (GameContext.MapEditorHud != null)
							{
								GameContext.MapEditorHud.SelectPickedUnitOrProp(closestStaticPropId, false);
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
					Decal? closestDecal = null;
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
						int pickedPlayer = unit.Player;
						if (GameContext.MapEditorHud != null)
						{
							GameContext.MapEditorHud.SetSpawnPlayer(pickedPlayer);
							GameContext.MapEditorHud.SelectPickedUnitOrProp(unit.UnitId, unit.IsBuilding);
						}
						else
						{
							ActivePlaceId = unit.UnitId;
							GameContext.EditorService.SetPlaceUnitPlayerIndexDomain(pickedPlayer);
							ActiveEditorTool = EditorTool.PlaceUnit;
						}
					}
					else if (clickedNode is PropEntityProxyNode propProxy)
					{
						if (GameContext.MapEditorHud != null)
						{
							GameContext.MapEditorHud.SelectPickedUnitOrProp(propProxy.PropId, false);
						}
						else
						{
							ActivePlaceId = propProxy.PropId;
							ActiveEditorTool = EditorTool.PlaceProp;
						}
					}
					else if (clickedNode is Prop3D prop)
					{
						if (GameContext.MapEditorHud != null)
						{
							GameContext.MapEditorHud.SelectPickedUnitOrProp(prop.PropId, false);
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
						if (GameContext.MapEditorHud != null)
						{
							GameContext.MapEditorHud.SelectPickedDecalView(decalId);
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
						GameContext.MapEditorHud?.UpdateExactHeightExternal(sampledHeight);
						EditorTool targetTool = EditorTool.Height;
						if (GameContext.MapEditorHud != null)
						{
							GameContext.MapEditorHud.SelectToolFromHotkey(targetTool);
						}
						else
						{
							ActiveEditorTool = targetTool;
						}

						GameContext.MapEditorHud?.ShowFeedbackExternal($"Picked Height: {sampledHeight:F1}m");
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
						GameContext.EditorService.Tools.PaintTextureIndex = sampledIndex;
						if (GameContext.MapEditorHud != null)
						{
							GameContext.MapEditorHud.SelectPaintSwatchByIndexView(sampledIndex);
						}
						else
						{
							ActiveEditorTool = EditorTool.PaintTexture;
						}

						GameContext.MapEditorHud?.ShowFeedbackExternal($"Picked Texture: #{sampledIndex}");
					}
				}

				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.SelectMove)
			{
				var collider = hit != null && hit.ContainsKey("collider") ? hit["collider"].As<Node>() : null;
				Node? clickedNode = null;
				if (collider != null)
				{
					clickedNode = FindUnit3DInParentChain(collider);
					if (clickedNode == null)
					{
						clickedNode = collider as PropEntityProxyNode;
						if (clickedNode == null)
						{
							clickedNode = FindProp3DInParentChain(collider);
						}
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
					Decal? closestDecal = null;
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
					ProceduralVfxInstance3D? closestVfx = null;
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

				Vector3 terrainHitPos = (terrainHit != null && terrainHit.ContainsKey("position")) ? terrainHit["position"].AsVector3() : hitPos;

				if (clickedNode != null)
				{
					bool shiftPressed = Input.IsKeyPressed(Key.Shift);
					bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);

					if (shiftPressed || ctrlPressed)
					{
						ToggleEditorObjectSelection(clickedNode);
					}
					else if (!(SelectedEditorObjectCount > 1 && IsEditorObjectSelected(clickedNode)))
					{
						SelectedEditorObject = clickedNode;
					}

					if (IsEditorObjectSelected(clickedNode))
					{
						BeginEditorObjectDrag(terrainHitPos, editorMouseBtn.Position);
					}
				}
				else
				{
					if (!Input.IsKeyPressed(Key.Shift) && !Input.IsKeyPressed(Key.Ctrl))
					{
						SelectedEditorObject = null;
					}

					if (GroundTerrain != null)
					{
						var (startCellX, startCellZ) = EditorGridSnapping.WorldPosToCellCoords(terrainHitPos);
						var startCell = new Vector2I(startCellX, startCellZ);
						GameContext.EditorService.Interaction.SetObjectMarquee(startCell, startCell, false);
					}
				}

				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.Ramp)
			{
				BeginRampInteraction(hitPos, editorMouseBtn.Position);
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

				GameContext.EditorService.FloodFillTerrainAt(hitPos.X, hitPos.Y, hitPos.Z, GameContext.EditorService.Tools.PaintTextureIndex, isCliff);
				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.FloodFillPathing)
			{
				int pathingMask = 0;
				bool pathingAdd = true;
				if (GameContext.MapEditorHud != null)
				{
					pathingMask = GameContext.MapEditorHud.GetSelectedPathingMask();
					pathingAdd = GameContext.MapEditorHud.IsPathingAddMode();
				}

				GameContext.EditorService.FloodFillPathingAt(hitPos.X, hitPos.Y, hitPos.Z, pathingMask, pathingAdd);
				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.Water)
			{
				bool isRemove = GameContext.MapEditorHud != null && GameContext.MapEditorHud.IsWaterRemoveAction();
				if (GameContext.MapEditorHud != null)
				{
					EditorWaterMode = GameContext.MapEditorHud.GetSelectedWaterMode();
					ActiveWaterProfileIndex = GameContext.MapEditorHud.GetSelectedWaterProfileIndex();
					GameContext.EditorService.Tools.WaterHeight = GameContext.MapEditorHud.GetSelectedWaterHeight();
				}

				GameContext.EditorService.FloodFillWaterAt(hitPos.X, hitPos.Y, hitPos.Z, isRemove);
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
					GameContext.EditorService.Interaction.SetSelectionStart(new Vector2I(cx, cz));
					GameContext.EditorService.Interaction.SetSelectionEnd(new Vector2I(cx, cz));
					GameContext.EditorService.Interaction.SetIsSelectingArea(true);
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
					GameContext.EditorService.Interaction.SetSelectionStart(new Vector2I(rcx, rcz));
					GameContext.EditorService.Interaction.SetSelectionEnd(new Vector2I(rcx, rcz));
					GameContext.EditorService.Interaction.SetIsSelectingArea(true);
					HideCoordinatePreviewMesh();
				}

				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.PasteArea)
			{
				if (GroundTerrain != null && GameContext.EditorService.Clipboard.HasCopiedArea)
				{
					var (cx, cz) = EditorGridSnapping.WorldPosToCellCoords(hitPos);
					var (startX, startZ, targetWidth, targetDepth) = EditorGridSnapping.GetAnchoredPasteBounds(GameContext.EditorService.Clipboard.CopiedArea, cx, cz, GameContext.EditorService.Tools.PasteRotation, GameContext.EditorService.Tools.PasteReflectionMode);
					PerformPasteArea(startX, startZ, GameContext.EditorService.Tools.PasteRotation, GameContext.EditorService.Tools.PasteReflectionMode);
					GameContext.MapEditorHud?.ShowFeedbackExternal("Pasted clipboard contents");
				}

				GetViewport().SetInputAsHandled();
			}
			else if (ActiveEditorTool == EditorTool.Measure)
			{
				if (GroundTerrain != null)
				{
					if (!EditorTapeMeasureActive)
					{
						GameContext.EditorService.Tools.TapeMeasureStart = hitPos;
						EditorTapeMeasureEnd = hitPos;
						EditorTapeMeasureActive = true;
						UpdateMeasureVisuals(hitPos, hitPos);
					}
					else if (GameContext.EditorService.Tools.TapeMeasureStart.HasValue)
					{
						EditorTapeMeasureEnd = hitPos;
						UpdateMeasureVisuals(GameContext.EditorService.Tools.TapeMeasureStart.Value, hitPos);
						EditorTapeMeasureActive = false;
					}
				}

				GetViewport().SetInputAsHandled();
			}
		}
	}
}
