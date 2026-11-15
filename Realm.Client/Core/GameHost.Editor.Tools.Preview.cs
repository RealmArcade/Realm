using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;

namespace Realm.Client.Core
{
	public partial class GameHost
	{
		private void UpdateEditorPreview(Vector3 position)
		{
			bool needsPreview = ActiveEditorTool == EditorTool.PlaceUnit ||
			                    ActiveEditorTool == EditorTool.PlaceProp ||
			                    ActiveEditorTool == EditorTool.PlaceDecal ||
			                    ActiveEditorTool == EditorTool.PlaceVfx ||
			                    ActiveEditorTool == EditorTool.PlaceItem;

			if (!needsPreview || string.IsNullOrWhiteSpace(ActivePlaceId))
			{
				ClearEditorPreview();
				return;
			}

			string reqType = ActiveEditorTool.ToString();
			string reqId = ActivePlaceId;
			bool reqIsEnemy = GameContext.EditorService.Tools.PlaceUnitIsEnemy;

			UpdatePreviewNodeIfNeeded(reqType, reqId, reqIsEnemy);

			if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
			{
				ApplyAllGlobalOverridesToObject(_editorPreviewNode);

				if (EditorClumpMode || ActiveEditorTool == EditorTool.PlacePropClump)
				{
					_editorPreviewNode.Visible = false;
					return;
				}

				PositionEditorPreview(position);
			}
		}

		private void UpdatePreviewNodeIfNeeded(string reqType, string reqId, bool reqIsEnemy)
		{
			if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) &&
			    GameContext.EditorService.Tools.PreviewType == reqType && _editorPreviewId == reqId && _editorPreviewIsEnemy == reqIsEnemy)
			{
				return;
			}

			ClearEditorPreview();

			GameContext.EditorService.Tools.PreviewType = reqType;
			_editorPreviewId = reqId;
			_editorPreviewIsEnemy = reqIsEnemy;

			if (ActiveEditorTool == EditorTool.PlaceUnit) CreateOrUpdatePreviewUnit(reqId, reqIsEnemy);
			else if (ActiveEditorTool == EditorTool.PlaceProp) CreateOrUpdatePreviewProp(reqId);
			else if (ActiveEditorTool == EditorTool.PlaceDecal) CreateOrUpdatePreviewDecal(reqId);
			else if (ActiveEditorTool == EditorTool.PlaceVfx) CreateOrUpdatePreviewVfx(reqId);
			else if (ActiveEditorTool == EditorTool.PlaceItem) CreateOrUpdatePreviewItem(reqId);
		}

		/// <summary>
		///     Draws the drop an item placement would leave on the ground, as the same node the placement spawns, so the
		///     silhouette the cursor carries is the object the map will hold rather than a generic marker.
		/// </summary>
		/// <param name="reqId">The item template the drop would hold.</param>
		private void CreateOrUpdatePreviewItem(string reqId)
		{
			string? resolvedId = ResolveItemTemplateId(reqId);
			if (resolvedId == null) return;

			Color color = Realm.Client.Utils.PreviewHologram.PropColor();
			var previewItem = new LootItem3D();
			previewItem.Name = "PreviewItem_LootItem3D";
			previewItem.Initialize(0, resolvedId, 1, 0f, Realm.Shared.Simulation.GroundLootService.DefaultPickupRadius, false, -1);
			AddChild(previewItem);

			MakeHologramRecursive(previewItem, color);
			_editorPreviewNode = previewItem;
		}

		private void CreateOrUpdatePreviewUnit(string reqId, bool reqIsEnemy)
		{
			if (!Placeables.TryGetUnitOrBuilding(reqId, out _))
			{
				LoadUnitMetadata(!string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : GameContext.EditorService._editingDirectory);
			}

			bool isBuilding = Placeables.TryGet(reqId, out PlaceableTemplate requestTemplate) && requestTemplate.Kind == PlaceableKind.Building;
			Placeables.TryGetUnitOrBuilding(reqId, out UnitMetadata? meta);

			if (meta?.TemplateID != null)
			{
				string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : reqId;
				string modelPath = GetFallbackModelPath(targetModel, isBuilding);

				Color color = Realm.Client.Utils.PreviewHologram.UnitColor(false);
				var previewUnit = new Unit3D();
				previewUnit.Name = "PreviewUnit_Unit3D";
				previewUnit.UnitId = reqId;
				previewUnit.IsBuilding = isBuilding;
				previewUnit.Player = reqIsEnemy ? 1 : 0;
				previewUnit.IsPreview = true;
				previewUnit.PreviewHologramColor = color;
				AddChild(previewUnit);
				previewUnit.LoadModel(modelPath);

				MakeHologramRecursive(previewUnit, color);
				_editorPreviewNode = previewUnit;
			}
		}

		private void CreateOrUpdatePreviewProp(string reqId)
		{
			if (!HasPreviewPropRequirement(reqId))
			{
				LoadUnitMetadata(!string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : GameContext.EditorService._editingDirectory);
				if (!HasPreviewPropRequirement(reqId))
				{
					return;
				}
			}

			Color color = Realm.Client.Utils.PreviewHologram.PropColor();
			var previewProp = new Prop3D();
			previewProp.Name = "PreviewProp_Prop3D";
			previewProp.PropId = reqId;
			previewProp.IsPreview = true;
			previewProp.PreviewHologramColor = color;
			AddChild(previewProp);

			MakeHologramRecursive(previewProp, color);
			_editorPreviewNode = previewProp;
		}

		private bool HasPreviewPropRequirement(string reqId)
		{
			if (PropRegistry.ContainsKey(reqId) || ResourceRegistry.ContainsKey(reqId)) return true;
			string cleanReqId = Path.GetFileNameWithoutExtension(reqId);
			if (!string.IsNullOrEmpty(cleanReqId) && (PropRegistry.ContainsKey(cleanReqId) || ResourceRegistry.ContainsKey(cleanReqId))) return true;
			return false;
		}

		private void CreateOrUpdatePreviewDecal(string reqId)
		{
			var previewDecal = new Decal3D();
			previewDecal.Name = "PreviewDecal_Decal3D";
			previewDecal.CullMask = RuntimeTerrain.TerrainDecalCullMask;
			previewDecal.DecalId = string.IsNullOrEmpty(reqId) ? "logo" : reqId;
			AddChild(previewDecal);
			ApplyDecalPropertiesFromMetadata(previewDecal, reqId);
			previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
			previewDecal.AlbedoMix = 0.5f;
			_editorPreviewNode = previewDecal;
		}

		private void CreateOrUpdatePreviewVfx(string reqId)
		{
			VfxAttachmentConfig config;
			if (GameContext.RegistryService.TryGetVfxConfig(reqId, out var regCfg))
			{
				config = regCfg.Clone();
			}
			else if (Enum.TryParse<VfxPrimitiveType>(reqId, true, out var primType))
			{
				config = new VfxAttachmentConfig { VfxId = reqId, PrimitiveType = primType };
			}
			else
			{
				config = new VfxAttachmentConfig { VfxId = reqId, Name = reqId };
			}

			var previewVfx = new ProceduralVfxInstance3D(config);
			previewVfx.Name = "PreviewVfx_ProceduralVfxInstance3D";
			previewVfx.IsPreview = true;
			AddChild(previewVfx);
			_editorPreviewNode = previewVfx;
		}

		private void PositionEditorPreview(Vector3 position)
		{
			if (_editorPreviewNode is not { } previewNode) return;

			if (!GameContext.EditorService.Interaction.HasCachedRandom) GameContext.EditorService.Interaction.GenerateNewRandomPlacementRotationAndScale();
			float previewRot = (GameContext.EditorService.Tools.RandomRotation && !GameContext.EditorService.Interaction.IsPastingObject) ? GameContext.EditorService.Interaction.CachedRandomRotation : GameContext.EditorService.Tools.PlacementRotation;
			float previewScaleVal = (GameContext.EditorService.Tools.RandomScale && !GameContext.EditorService.Interaction.IsPastingObject) ? GameContext.EditorService.Interaction.CachedRandomScale : EditorPlacementScale;

			Vector3 previewPos = CalculateEditorPreviewPosition(position, previewScaleVal);

			float safePreviewScale = previewScaleVal <= 0.001f ? 1.0f : previewScaleVal;

			if (previewNode is ProceduralVfxInstance3D previewVfxNode)
			{
				PositionVfxPreview(previewVfxNode, previewPos, previewRot, safePreviewScale);
			}
			else if (previewNode is Decal previewDecal)
			{
				PositionDecalPreview(previewDecal, previewPos, previewRot, safePreviewScale);
			}
			else
			{
				PositionStandardPreview(previewNode, previewPos, previewRot, safePreviewScale);
			}

			UpdatePreviewSlopeAlignment(previewNode);
			previewNode.Visible = true;
		}

		private Vector3 CalculateEditorPreviewPosition(Vector3 position, float previewScaleVal)
		{
			Vector3 previewPos = position;
			if (EditorSnapToGrid && GroundTerrain != null)
			{
				previewPos = EditorGridSnapping.SnapToCellCenter(previewPos);
			}

			if (ActiveEditorTool != EditorTool.PlaceDecal)
			{
				previewPos.Y = GameContext.EditorService.GetTerrainHeightAt(previewPos);
			}

			if (ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp)
			{
				float radius = GetPlacementRadius(ActivePlaceId, previewScaleVal);
				var finalPos = FindNearestFreePosition(previewPos, radius);
				if (finalPos != null)
				{
					previewPos = finalPos.Value;
				}
			}

			return previewPos;
		}

		private void PositionVfxPreview(ProceduralVfxInstance3D previewVfxNode, Vector3 previewPos, float previewRot, float safePreviewScale)
		{
			float normalOffset = previewVfxNode.Config?.SurfaceNormalOffset ?? 0.02f;
			if (previewVfxNode.Config?.PlacementMode == VfxPlacementMode.SurfaceSnap)
			{
				Vector3 hitNormal = GetTerrainNormalAt(previewPos);
				Basis alignedBasis = CreateAlignedBasis(hitNormal);
				previewVfxNode.Basis = alignedBasis;
				previewVfxNode.RotateObjectLocal(Vector3.Up, Mathf.DegToRad(previewRot));
				previewVfxNode.Position = previewPos + hitNormal * normalOffset;
			}
			else
			{
				previewVfxNode.Position = previewPos + Vector3.Up * normalOffset;
				previewVfxNode.RotationDegrees = new Vector3(0.0f, previewRot, 0.0f);
			}

			previewVfxNode.Scale = Vector3.One * safePreviewScale;
		}

		private void PositionDecalPreview(Decal previewDecal, Vector3 previewPos, float previewRot, float safePreviewScale)
		{
			Vector3 hitNormal = GetTerrainNormalAt(previewPos);
			var mousePos = GetViewport().GetMousePosition();
			var terrainHit = RaycastTerrainFromMouse(mousePos);
			if (terrainHit != null && terrainHit.ContainsKey("normal"))
			{
				hitNormal = terrainHit["normal"].AsVector3();
			}

			Basis alignedBasis = CreateAlignedBasis(hitNormal);
			previewDecal.Basis = alignedBasis;
			previewDecal.RotateObjectLocal(Vector3.Up, Mathf.DegToRad(previewRot));
			previewDecal.Position = previewPos;
			previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * safePreviewScale;
			previewDecal.Scale = Vector3.One;
		}

		private void PositionStandardPreview(Node3D previewNode, Vector3 previewPos, float previewRot, float safePreviewScale)
		{
			previewNode.Position = previewPos;
			previewNode.RotationDegrees = new Vector3(0.0f, previewRot, 0.0f);
			previewNode.Scale = Vector3.One * safePreviewScale;
		}

		private void ClearEditorPreview()
		{
			if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
			{
				if (_editorPreviewNode.GetParent() != null)
				{
					_editorPreviewNode.GetParent().RemoveChild(_editorPreviewNode);
				}
				_editorPreviewNode.QueueFree();
			}

			_editorPreviewNode = null;
			GameContext.EditorService.Tools.PreviewType = "";
			_editorPreviewId = "";
			_editorPreviewIsEnemy = false;
		}

		private void MakeHologramRecursive(Node node, Color color)
		{
			Realm.Client.Utils.PreviewHologram.Apply(node, color);
		}

		private void UpdatePreviewSlopeAlignment(Node previewNode)
		{
			if (previewNode is Unit3D unitPreview) unitPreview.UpdateSlopeAlignment();
			else if (previewNode is Prop3D propPreview) propPreview.UpdateSlopeAlignment();
		}

	}
}
