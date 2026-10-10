using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Client;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Client.Core
{
    public partial class GameHost
    {

        private void ApplyContinuousTerrainEditing(Vector3 worldPos, float delta, bool isFirstClick = false)
        {
            if (GroundTerrain == null) return;

            var positions = GetEditingPositions(worldPos);
            GetPathingOptions(out int pathingMask, out bool pathingAdd);

            bool applyGround = Realm.Client.UI.MapEditorHUD.Instance?.IsApplyGroundTextureEnabled() ?? true;
            bool applyCliff = Realm.Client.UI.MapEditorHUD.Instance?.IsApplyCliffTextureEnabled() ?? true;
            float targetBlockHeight = ActiveEditorTool == EditorTool.Height ? EditorExactHeight : EditorBlockLevelHeight;

            bool anyModified = ApplyEditsToPositions(positions, delta, targetBlockHeight, pathingMask, pathingAdd, isFirstClick, applyGround, applyCliff);

            if (anyModified)
            {
                FlushTerrainModifications(isFirstClick);
            }
        }

        private List<Vector3> GetEditingPositions(Vector3 worldPos)
        {
            var positions = new List<Vector3> { worldPos };
            if (EditorMirrorMode != MirrorMode.None)
            {
                foreach (var t in GetMirroredTransforms(worldPos, 0.0f))
                    positions.Add(t.Position);
            }
            return positions;
        }

        private void GetPathingOptions(out int pathingMask, out bool pathingAdd)
        {
            pathingMask = 0;
            pathingAdd = true;
            if (ActiveEditorTool == EditorTool.PaintPathing && Realm.Client.UI.MapEditorHUD.Instance != null)
            {
                pathingMask = Realm.Client.UI.MapEditorHUD.Instance.GetSelectedPathingMask();
                pathingAdd = Realm.Client.UI.MapEditorHUD.Instance.IsPathingAddMode();
            }
        }

        private bool ApplyEditsToPositions(List<Vector3> positions, float delta, float targetBlockHeight, int pathingMask, bool pathingAdd, bool isFirstClick, bool applyGround, bool applyCliff)
        {
            bool anyModified = false;
            foreach (var pos in positions)
            {
                var result = _editorService.ApplyContinuousTerrainEditing(
                    pos, delta, ActiveEditorTool, EditorBrushRadius, EditorBrushStrength, EditorBrushIsSquare,
                    EditorBlockMode, targetBlockHeight, EditorPaintTextureIndex, EditorCliffPaintTextureIndex,
                    pathingMask, pathingAdd, isFirstClick, applyGround, applyCliff);

                if (!result.HeightsModified && !result.SplatModified && !result.PathingModified) continue;
                
                Rect2I affected = new Rect2I(result.MinX - 2, result.MinZ - 2, result.MaxX - result.MinX + 5, result.MaxZ - result.MinZ + 5);
                _terrainFlushRegion = _terrainFlushRegion.HasValue ? _terrainFlushRegion.Value.Merge(affected) : affected;
                _terrainGeometryDirty = true;
                if (result.HeightsModified) _terrainHeightsDirty = true;
                if (result.PathingModified) _terrainPathingDirty = true;
                anyModified = true;
            }
            return anyModified;
        }

        private void FlushTerrainModifications(bool isFirstClick)
        {
            EditorHasUnsavedChanges = true;
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!isFirstClick && nowMs - _lastTerrainMeshRebuildMs < TerrainMeshRebuildPeriodMs)
                return;

            _lastTerrainMeshRebuildMs = nowMs;
            if (!_terrainFlushRegion.HasValue) return;

            var flushRegion = _terrainFlushRegion.Value;
            GroundTerrain.UpdateMeshAndPhysics(false, false, flushRegion, _terrainHeightsDirty);
            if (_terrainHeightsDirty) AlignAllEntitiesToTerrain(flushRegion);
            if (_terrainPathingDirty && PathingOverlayVisible) RebuildPathingOverlay();
            
            _terrainFlushRegion = null;
            _terrainGeometryDirty = false;
            _terrainHeightsDirty = false;
            _terrainPathingDirty = false;
        }

        public void FlushTerrainMeshAndPhysics()
        {
            if (_terrainGeometryDirty && _terrainFlushRegion.HasValue && GroundTerrain != null)
            {
                var flushRegion = _terrainFlushRegion.Value;
                GroundTerrain.UpdateMeshAndPhysics(_terrainHeightsDirty, false, flushRegion, _terrainHeightsDirty);
                if (_terrainHeightsDirty)
                {
                    AlignAllEntitiesToTerrain(flushRegion);
                }
                if (_terrainPathingDirty && PathingOverlayVisible)
                {
                    RebuildPathingOverlay();
                }
                _terrainFlushRegion = null;
                _terrainGeometryDirty = false;
                _terrainHeightsDirty = false;
                _terrainPathingDirty = false;
                _lastTerrainMeshRebuildMs = long.MinValue;
            }
        }

        public Decal SpawnDecalExternal(Vector3 position)
        {
            var entity = EcsWorld.Create();
            var decal = new Realm.Client.Decal3D();
            decal.Entity = entity;
            decal.DecalId = "logo";
            decal.TextureAlbedo = GD.Load<Texture2D>("res://icon.svg");
            decal.Size = new Vector3(6.0f, 20.0f, 6.0f);
            decal.AlbedoMix = 1.0f;
            decal.CullMask = Realm.Client.RuntimeTerrain.TerrainDecalCullMask;
            AddChild(decal);
            AllDecals.Add(decal);

            position.Y = _editorService.GetTerrainHeightAt(position);
            decal.Position = position;

            EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
            EcsWorld.Add(entity, new RotationY(0.0f));
            EcsWorld.Add(entity, new ModelScale(1.0f));

            if (IsMapEditorMode)
            {
                decal.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
                decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * (EditorPlacementScale <= 0.001f ? 1.0f : EditorPlacementScale);
                decal.Scale = Vector3.One;
                EcsWorld.Set(entity, new RotationY(EditorPlacementRotation));
                EcsWorld.Set(entity, new ModelScale(EditorPlacementScale));
            }
            return decal;
        }

        public float GetTerrainHeightAt(Vector3 worldPos)
        {
            return _editorService.GetTerrainHeightAt(worldPos);
        }
        private void AlignAllEntitiesToTerrain(Rect2I? affectedRegion = null)
        {
            float quadSize = GroundTerrain != null ? GroundTerrain.QuadSize : Realm.Client.EditableTerrain.DefaultQuadSize;
            float halfW = GroundTerrain != null ? GroundTerrain.Width / 2.0f * quadSize : 0f;
            float halfD = GroundTerrain != null ? GroundTerrain.Depth / 2.0f * quadSize : 0f;

            AlignUnitsToTerrain(affectedRegion, quadSize, halfW, halfD);
            bool anyPropMoved = AlignPropsAndDecalsToTerrain(affectedRegion, quadSize, halfW, halfD);

            if (!affectedRegion.HasValue && anyPropMoved)
            {
                Realm.Client.PropMultiMeshManager.Instance?.MarkAllDirty();
            }
        }

        private bool IsPositionInRegion(Vector3 pos, Rect2I? affectedRegion, float quadSize, float halfW, float halfD)
        {
            if (!affectedRegion.HasValue) return true;
            var region = affectedRegion.Value;
            float gridX = pos.X / quadSize + halfW / quadSize;
            float gridZ = pos.Z / quadSize + halfD / quadSize;
            int x = (int)Mathf.Round(gridX);
            int z = (int)Mathf.Round(gridZ);
            return x >= region.Position.X - 2 && x <= region.Position.X + region.Size.X + 2 &&
                   z >= region.Position.Y - 2 && z <= region.Position.Y + region.Size.Y + 2;
        }

        private void AlignUnitsToTerrain(Rect2I? affectedRegion, float quadSize, float halfW, float halfD)
        {
            foreach (var unit in AllUnits)
            {
                if (!GodotObject.IsInstanceValid(unit)) continue;
                var pos = unit.GlobalPosition;
                if (!IsPositionInRegion(pos, affectedRegion, quadSize, halfW, halfD)) continue;

                float targetY = _editorService.GetTerrainHeightAt(pos);
                if (MathF.Abs(pos.Y - targetY) > 0.001f)
                {
                    pos.Y = targetY;
                    unit.GlobalPosition = pos;
                    if (EcsWorld.IsAlive(unit.Entity))
                    {
                        EcsWorld.Set(unit.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
                    }
                }
            }
        }

        private bool AlignPropsAndDecalsToTerrain(Rect2I? affectedRegion, float quadSize, float halfW, float halfD)
        {
            bool anyPropMoved = false;
            foreach (var child in GetChildren())
            {
                if (child is Realm.Client.Prop3D prop && GodotObject.IsInstanceValid(prop))
                {
                    anyPropMoved |= TryAlignProp(prop, affectedRegion, quadSize, halfW, halfD);
                }
                else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
                {
                    TryAlignDecal(decal, affectedRegion, quadSize, halfW, halfD);
                }
            }
            return anyPropMoved;
        }

        private bool TryAlignProp(Realm.Client.Prop3D prop, Rect2I? affectedRegion, float quadSize, float halfW, float halfD)
        {
            var pos = prop.GlobalPosition;
            if (!IsPositionInRegion(pos, affectedRegion, quadSize, halfW, halfD)) return false;

            float targetY = _editorService.GetTerrainHeightAt(pos);
            if (MathF.Abs(pos.Y - targetY) <= 0.001f) return false;

            pos.Y = targetY;
            prop.GlobalPosition = pos;
            Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
            return true;
        }

        private void TryAlignDecal(Decal decal, Rect2I? affectedRegion, float quadSize, float halfW, float halfD)
        {
            var pos = decal.GlobalPosition;
            if (!IsPositionInRegion(pos, affectedRegion, quadSize, halfW, halfD)) return;

            float targetY = _editorService.GetTerrainHeightAt(pos);
            if (MathF.Abs(pos.Y - targetY) <= 0.001f) return;

            pos.Y = targetY;
            decal.GlobalPosition = pos;
        }

        public Decal SpawnDecalExternalWithParams(string decalId, Vector3 position, Vector3 rotationDegrees, float scale)
        {
            var entity = EcsWorld.Create();
            var decal = new Realm.Client.Decal3D();
            decal.Entity = entity;
            decal.DecalId = string.IsNullOrEmpty(decalId) ? "logo" : decalId;
            decal.TextureAlbedo = LoadDecalTexture(decalId);
            decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * (scale <= 0.001f ? 1.0f : scale);
            decal.AlbedoMix = 1.0f;
            decal.CullMask = Realm.Client.RuntimeTerrain.TerrainDecalCullMask;
            AddChild(decal);
            AllDecals.Add(decal);

            decal.Position = position;
            decal.RotationDegrees = rotationDegrees;
            decal.Scale = Vector3.One;

            EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
            EcsWorld.Add(entity, new Realm.Ecs.Components.Meta.Rotation3D(new System.Numerics.Vector3(rotationDegrees.X, rotationDegrees.Y, rotationDegrees.Z)));
            EcsWorld.Add(entity, new RotationY(rotationDegrees.Y));
            EcsWorld.Add(entity, new ModelScale(scale));

            ApplyDecalPropertiesFromMetadata(decal, decalId);

            return decal;
        }

        public Decal SpawnDecalExternalWithParams(string decalId, Vector3 position, float rotationY, float scale)
        {
            Vector3 pos = position;
            pos.Y = _editorService.GetTerrainHeightAt(pos);
            return SpawnDecalExternalWithParams(decalId, pos, new Vector3(0.0f, rotationY, 0.0f), scale);
        }

        public Vector3 GetTerrainNormalAt(Vector3 pos)
        {
            float step = 0.5f;
            float hL = _editorService.GetTerrainHeightAt(new Vector3(pos.X - step, 0f, pos.Z));
            float hR = _editorService.GetTerrainHeightAt(new Vector3(pos.X + step, 0f, pos.Z));
            float hD = _editorService.GetTerrainHeightAt(new Vector3(pos.X, 0f, pos.Z - step));
            float hU = _editorService.GetTerrainHeightAt(new Vector3(pos.X, 0f, pos.Z + step));
            return new Vector3(hL - hR, 2f * step, hD - hU).Normalized();
        }

        public void AlignAllEntitiesToTerrainExternal(Rect2I? affectedRegion = null)
        {
            AlignAllEntitiesToTerrain(affectedRegion);
        }

        private float GetMinHeightInBrushBounds(Vector3 worldPos)
        {
            return _editorService.GetMinHeightInBrushBounds(worldPos, EditorBrushRadius, EditorBrushIsSquare);
        }

        public void SwapTexturesExternal(int indexA, int indexB)
        {
            if (GroundTerrain == null) return;
            if (GroundTerrain.SplatMap == null) return;
            if (indexA == indexB) return;

            TerrainSplatWeights[,] splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
            TerrainSplatWeights[,] cliffBefore = null;
            
            if (GroundTerrain.CliffSplatMap != null)
            {
                cliffBefore = (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone();
            }

            bool anyChanged = SwapAllSplats(GroundTerrain.SplatMap, indexA, indexB);
            anyChanged |= SwapAllSplats(GroundTerrain.CliffSplatMap, indexA, indexB);

            if (!anyChanged) return;

            TerrainSplatWeights[,] splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
            TerrainSplatWeights[,] cliffAfter = null;
            
            if (GroundTerrain.CliffSplatMap != null)
            {
                cliffAfter = (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone();
            }

            var action = new TerrainModifyAction((Realm.Ecs.Components.Terrain.TerrainCell[,])null, (Realm.Ecs.Components.Terrain.TerrainCell[,])null, splatBefore, splatAfter, null, null, cliffBefore, cliffAfter);
            EditorHistoryManager.RecordAction(action);
            EditorHasUnsavedChanges = true;

            GroundTerrain.UpdateMeshAndPhysics(false, false);
            
            if (Realm.Client.UI.MapEditorHUD.Instance != null)
            {
                Realm.Client.UI.MapEditorHUD.Instance.ShowFeedbackExternal("Textures swapped successfully!");
            }
        }

        private bool SwapAllSplats(TerrainSplatWeights[,] map, int indexA, int indexB)
        {
            if (map == null) return false;

            bool anyChanged = false;
            int w = map.GetLength(0);
            int d = map.GetLength(1);

            for (int z = 0; z < d; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    anyChanged |= TrySwapSplat(map, x, z, indexA, indexB);
                }
            }

            return anyChanged;
        }

        private bool TrySwapSplat(TerrainSplatWeights[,] map, int x, int z, int indexA, int indexB)
        {
            var s = map[x, z];
            if (!NeedsSwap(s, indexA, indexB)) return false;

            map[x, z] = new TerrainSplatWeights
            {
                Index0 = SwapIndex(s.Index0, indexA, indexB),
                Index1 = SwapIndex(s.Index1, indexA, indexB),
                Index2 = SwapIndex(s.Index2, indexA, indexB),
                Index3 = SwapIndex(s.Index3, indexA, indexB),
                Weight0 = s.Weight0,
                Weight1 = s.Weight1,
                Weight2 = s.Weight2,
                Weight3 = s.Weight3
            };
            return true;
        }

        private bool NeedsSwap(TerrainSplatWeights s, int indexA, int indexB)
        {
            if (s.Index0 == indexA || s.Index0 == indexB) return true;
            if (s.Index1 == indexA || s.Index1 == indexB) return true;
            if (s.Index2 == indexA || s.Index2 == indexB) return true;
            if (s.Index3 == indexA || s.Index3 == indexB) return true;
            return false;
        }

        private int SwapIndex(int current, int indexA, int indexB)
        {
            if (current == indexA) return indexB;
            if (current == indexB) return indexA;
            return current;
        }

        public void AlignTerrainSplatMapExternal()
        {
            if (GroundTerrain != null)
            {
                _editorService.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);
            }
        }

        public void ResizeMapExternal(int newWidth, int newDepth)
        {
            if (GroundTerrain == null) return;

            newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
            newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

            var before = MapStateSnapshot.CreateSnapshot();

            int oldWidth = GroundTerrain.Width;
            int oldDepth = GroundTerrain.Depth;

            float diffWidth = (newWidth - oldWidth) * GroundTerrain.QuadSize;
            float diffDepth = (newDepth - oldDepth) * GroundTerrain.QuadSize;

            EditorCameraBoundsLeft -= diffWidth / 2.0f;
            EditorCameraBoundsRight += diffWidth / 2.0f;
            EditorCameraBoundsTop -= diffDepth / 2.0f;
            EditorCameraBoundsBottom += diffDepth / 2.0f;

            GroundTerrain.ResizeTerrain(newWidth, newDepth);

            _editorService.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);
            DeleteEntitiesOutsideBounds();
            Realm.Client.PropMultiMeshManager.Instance?.RebuildAll();

            RebuildCameraBoundsOverlay();
            Realm.Client.UI.MapEditorHUD.Instance?.UpdateCameraBoundsUI();
            Realm.Client.UI.MapEditorHUD.Instance?.RegenerateMinimap();

            string activeWsPath = Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
            string metaPath = MetadataService.ResolveMetadataPath(activeWsPath);
            if (System.IO.File.Exists(metaPath))
            {
                try
                {
                    MetadataService.Instance.UpdateMetadata(activeWsPath, meta =>
                    {
                        meta.MapProperties.MapWidth = newWidth;
                        meta.MapProperties.MapHeight = newDepth;
                    });
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"Failed to update metadata.json map dimensions: {ex.Message}");
                }
            }

            EditorHasUnsavedChanges = true;
            Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal($"Map resized to {newWidth}x{newDepth}");

            var after = MapStateSnapshot.CreateSnapshot();
            EditorHistoryManager.RecordAction(new MapResizeAction(before, after));
        }

        public void PerformFloodFillPathing(Vector3 clickPos, int pathingMask, bool pathingAdd)
        {
            if (GroundTerrain == null || GroundTerrain.PathingCodes == null) return;

            var result = _editorService.PerformFloodFillPathing(clickPos, pathingMask, pathingAdd, EditorMirrorMode);

            if (result.Before != null && result.After != null)
            {
                GroundTerrain.UpdateMeshAndPhysics(false, false);
                var action = new TerrainModifyAction((Realm.Ecs.Components.Terrain.TerrainCell[,])null, (Realm.Ecs.Components.Terrain.TerrainCell[,])null, null, null, result.Before, result.After);
                EditorHistoryManager.RecordAction(action);
                EditorHasUnsavedChanges = true;
                Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal("Flood filled pathing area");
                UpdatePathingOverlay();
            }
        }

        public void PerformWaterFloodFill(Vector3 clickPos, bool isRemoveAction = false)
        {
            if (GroundTerrain == null || GroundTerrain.Cells == null) return;

            WaterType activeMode = EditorWaterMode;
            byte activeProfile = ActiveWaterProfileIndex;
            float waterHeight = EditorWaterHeight;

            var result = _editorService.PerformWaterFloodFill(clickPos, activeMode, activeProfile, waterHeight, EditorMirrorMode, isRemoveAction);
            if (result.BeforeCells == null || result.AfterCells == null) return;

            GroundTerrain.UpdateMeshAndPhysics(rebuildPhysics: false, rebuildNavMesh: true, affectedRegions: null, rebuildWater: true);

            var action = new TerrainModifyAction(result.BeforeCells, result.AfterCells, null, null, result.BeforePathing, result.AfterPathing);
            EditorHistoryManager.RecordAction(action);
            EditorHasUnsavedChanges = true;

            string statusMsg = result.WasAdded ? "Water added via flood fill" : "Water removed via flood fill";
            Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal(statusMsg);
        }

        private Vector3[] BuildHighlightMeshVertices(int selWidth, int selDepth, int minX, int minZ)
        {
            int vertexCount = selWidth * selDepth;
            var vertices = new Vector3[vertexCount];
            int width = GroundTerrain.Width;
            int depth = GroundTerrain.Depth;
            float quadSize = GroundTerrain.QuadSize;
            var cells = GroundTerrain.Cells;
            float halfW = width * 0.5f;
            float halfD = depth * 0.5f;

            for (int sz = 0; sz < selDepth; sz++)
            {
                int mapZ = minZ + sz;
                float lz = (mapZ - halfD) * quadSize;
                int rowOffset = sz * selWidth;
                for (int sx = 0; sx < selWidth; sx++)
                {
                    int mapX = minX + sx;
                    int idx = rowOffset + sx;
                    float lx = (mapX - halfW) * quadSize;
                    float h = Realm.Client.EditableTerrain.GetGridNodeHeight(mapX, mapZ, cells, width, depth);
                    vertices[idx] = new Vector3(lx, h + 0.05f, lz);
                }
            }
            return vertices;
        }

        private void AddQuadIndices(int[] indices, ref int iIdx, int row0, int row1, int sx)
        {
            int v00 = row0 + sx;
            int v10 = row0 + (sx + 1);
            int v01 = row1 + sx;
            int v11 = row1 + (sx + 1);

            indices[iIdx++] = v00;
            indices[iIdx++] = v10;
            indices[iIdx++] = v01;
            indices[iIdx++] = v10;
            indices[iIdx++] = v11;
            indices[iIdx++] = v01;
        }

        private void RebuildCoordinateMeshInstance(MeshInstance3D meshInstance, int minX, int minZ, int maxX, int maxZ, Color color, float yOffset = 0.15f)
        {
            if (meshInstance == null || GroundTerrain?.Cells == null) return;
            
            int selWidth = maxX - minX + 1;
            int selDepth = maxZ - minZ + 1;
            if (selWidth < 2 || selDepth < 2)
            {
                meshInstance.Visible = false;
                return;
            }

            int vertexCount = selWidth * selDepth;
            var vertices = GenerateCoordinateVertices(minX, minZ, selWidth, selDepth, yOffset);
            var indices = GenerateCoordinateIndices(selWidth, selDepth);
            var colors = new Color[vertexCount];
            Array.Fill(colors, color);

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.Index] = indices;
            arrays[(int)Mesh.ArrayType.Color] = colors;
            
            var arrayMesh = new ArrayMesh();
            arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            meshInstance.Mesh = arrayMesh;
            meshInstance.Visible = true;
        }

        private Vector3[] GenerateCoordinateVertices(int minX, int minZ, int selWidth, int selDepth, float yOffset)
        {
            int vertexCount = selWidth * selDepth;
            var vertices = new Vector3[vertexCount];
            int width = GroundTerrain.Width;
            int depth = GroundTerrain.Depth;
            float quadSize = GroundTerrain.QuadSize;
            var cells = GroundTerrain.Cells;
            float halfW = width * 0.5f;
            float halfD = depth * 0.5f;

            for (int sz = 0; sz < selDepth; sz++)
            {
                int mapZ = minZ + sz;
                float lz = (mapZ - halfD) * quadSize;
                int rowOffset = sz * selWidth;
                for (int sx = 0; sx < selWidth; sx++)
                {
                    int mapX = minX + sx;
                    float lx = (mapX - halfW) * quadSize;
                    float h = Realm.Client.EditableTerrain.GetGridNodeHeight(mapX, mapZ, cells, width, depth);
                    vertices[rowOffset + sx] = new Vector3(lx, h + yOffset, lz);
                }
            }
            return vertices;
        }

        private int[] GenerateCoordinateIndices(int selWidth, int selDepth)
        {
            int cellWidth = selWidth - 1;
            int cellDepth = selDepth - 1;
            var indices = new int[cellWidth * cellDepth * 6];
            int iIdx = 0;
            
            for (int sz = 0; sz < cellDepth; sz++)
            {
                int row0 = sz * selWidth;
                int row1 = (sz + 1) * selWidth;
                for (int sx = 0; sx < cellWidth; sx++)
                {
                    int v00 = row0 + sx;
                    int v10 = row0 + (sx + 1);
                    int v01 = row1 + sx;
                    int v11 = row1 + (sx + 1);
                    
                    indices[iIdx++] = v00;
                    indices[iIdx++] = v10;
                    indices[iIdx++] = v01;
                    indices[iIdx++] = v10;
                    indices[iIdx++] = v11;
                    indices[iIdx++] = v01;
                }
            }
            return indices;
        }

        public bool CommitCoordinateExternal(string coordinateName, int minX, int minZ, int maxX, int maxZ)
        {
            if (GroundTerrain == null) return false;
            string safeName = coordinateName.Trim();
            if (string.IsNullOrEmpty(safeName)) return false;

            var oldCoordinates = new List<EditorCoordinate>(EditorCoordinates);

            int width = GroundTerrain.Width;
            int depth = GroundTerrain.Depth;
            float quadSize = GroundTerrain.QuadSize;

            float worldMinX = (minX - width / 2.0f) * quadSize;
            float worldMinZ = (minZ - depth / 2.0f) * quadSize;
            float worldMaxX = (maxX - width / 2.0f) * quadSize;
            float worldMaxZ = (maxZ - depth / 2.0f) * quadSize;

            bool committed = false;
            for (int i = 0; i < EditorCoordinates.Count; i++)
            {
                if (EditorCoordinates[i].Name == safeName)
                {
                    EditorCoordinates[i] = new EditorCoordinate { Name = safeName, MinX = worldMinX, MinZ = worldMinZ, MaxX = worldMaxX, MaxZ = worldMaxZ };
                    committed = true;
                    break;
                }
            }

            if (!committed)
            {
                EditorCoordinates.Add(new EditorCoordinate { Name = safeName, MinX = worldMinX, MinZ = worldMinZ, MaxX = worldMaxX, MaxZ = worldMaxZ });
            }

            RebuildAllCoordinatePersistentMeshes();

            var newCoordinates = new List<EditorCoordinate>(EditorCoordinates);
            var action = new CoordinateAction(oldCoordinates, newCoordinates);
            EditorHistoryManager.RecordAction(action);
            EditorHasUnsavedChanges = true;

            return true;
        }

        private List<IEditorAction> PerformEraseArea(bool recordToHistory = true)
        {
            if (!CanEraseArea())
            {
                Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Erase (select an area first)");
                return new List<IEditorAction>();
            }

            var bounds = _editorService.GetCurrentSelectionBounds();
            var terrainSnapshotBefore = CreateTerrainSnapshot();
            
            var node3Ds = new List<Node3D>();
            foreach (var child in GetChildren())
            {
                if (child is Node3D n3d) node3Ds.Add(n3d);
            }

            var eraseResult = _editorService.BuildEraseAreaResult(
                bounds.Item1, bounds.Item2, bounds.Item3, bounds.Item4,
                PasteOptionHeights, PasteOptionTextures, PasteOptionEntities, PasteOptionPathing,
                node3Ds, _editorPreviewNode as Node3D, EditorBrushIsSquare);

            ApplyEraseResultTerrainModifications(eraseResult, bounds.Item1, bounds.Item2, bounds.Item3, bounds.Item4);

            var actions = BuildEraseActions(eraseResult, terrainSnapshotBefore);

            if (actions.Count > 0 && recordToHistory)
            {
                EditorHistoryManager.RecordAction(new CompositeAction(actions));
                EditorHasUnsavedChanges = true;
                Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal("Area Erased");
            }
            return actions;
        }

        private bool CanEraseArea()
        {
            return GroundTerrain?.Cells != null && 
                   GroundTerrain.SplatMap != null && 
                   _editorService.SelectionStart != null && 
                   _editorService.SelectionEnd != null;
        }

        private (Realm.Ecs.Components.Terrain.TerrainCell[,] Cells, TerrainSplatWeights[,] Splat, int[,] Pathing, TerrainSplatWeights[,] Cliff) CreateTerrainSnapshot()
        {
            return (
                (Realm.Ecs.Components.Terrain.TerrainCell[,])GroundTerrain.Cells.Clone(),
                (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone(),
                (int[,])GroundTerrain.PathingCodes.Clone(),
                GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null
            );
        }

        private void ApplyEraseResultTerrainModifications(EditorService.EraseAreaResult eraseResult, int minX, int minZ, int maxX, int maxZ)
        {
            if (!eraseResult.TerrainModified) return;

            Rect2I affected = new Rect2I(minX - 2, minZ - 2, maxX - minX + 4, maxZ - minZ + 4);
            if (eraseResult.HeightsModified)
            {
                AlignAllEntitiesToTerrain(affected);
            }
            GroundTerrain.UpdateMeshAndPhysics(eraseResult.HeightsModified, false, affected, eraseResult.HeightsModified);
            if (eraseResult.PathingModified)
            {
                UpdatePathingOverlay();
            }
        }

        private List<IEditorAction> BuildEraseActions(EditorService.EraseAreaResult eraseResult, (Realm.Ecs.Components.Terrain.TerrainCell[,] Cells, TerrainSplatWeights[,] Splat, int[,] Pathing, TerrainSplatWeights[,] Cliff) snapshotBefore)
        {
            var actions = new List<IEditorAction>();
            
            if (eraseResult.TerrainModified)
            {
                var cellsAfter = (Realm.Ecs.Components.Terrain.TerrainCell[,])GroundTerrain.Cells.Clone();
                var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
                var pathingAfter = (int[,])GroundTerrain.PathingCodes.Clone();
                var cliffAfter = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;

                var action = new TerrainModifyAction(snapshotBefore.Cells, cellsAfter, snapshotBefore.Splat, splatAfter, snapshotBefore.Pathing, pathingAfter, snapshotBefore.Cliff, cliffAfter);
                actions.Add(action);
            }

            foreach (var node in eraseResult.NodesToDelete)
            {
                var act = DeleteObjectAtWithUndo(node, node.Position);
                if (act != null) actions.Add(act);
            }
            
            return actions;
        }
    }
}
