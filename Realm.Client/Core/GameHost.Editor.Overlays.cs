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

        private bool IsMouseOverUI()
        {
            if (Realm.Client.UI.SettingsMenu.IsOpen)
            {
                return true;
            }

            if (GodotObject.IsInstanceValid(Realm.Client.UI.MapEditorHUD.Instance))
            {
                return Realm.Client.UI.MapEditorHUD.Instance.IsMouseOverUI(GetViewport().GetMousePosition());
            }

            if (GodotObject.IsInstanceValid(Realm.Client.UI.InGameHUD.Instance))
            {
                return Realm.Client.UI.InGameHUD.Instance.IsMouseOverUI(GetViewport().GetMousePosition());
            }

            var hoveredControl = GetViewport().GuiGetHoveredControl();
            if (hoveredControl != null)
            {
                return true;
            }

            return false;
        }

        public void InvalidateDecalCache(string decalId)
        {
            if (string.IsNullOrEmpty(decalId)) return;
            string baseKey = System.IO.Path.GetFileNameWithoutExtension(decalId);
            _decalAssetCache.Remove(decalId);
            _decalAssetCache.Remove(baseKey);
            _decalAssetCache.Remove($"{baseKey}.rtex");
            _decalAssetCache.Remove($"{baseKey}.png");
        }

        public ImageTexture GetOrCreateOrmTexture(float roughness, float metallic)
        {
            int rKey = Mathf.RoundToInt(Mathf.Clamp(roughness, 0f, 1f) * 100f);
            int mKey = Mathf.RoundToInt(Mathf.Clamp(metallic, 0f, 1f) * 100f);
            if (_decalOrmCache.TryGetValue((rKey, mKey), out var cached) && GodotObject.IsInstanceValid(cached))
            {
                return cached;
            }

            var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            img.Fill(new Color(1.0f, rKey / 100f, mKey / 100f, 1.0f));
            var tex = ImageTexture.CreateFromImage(img);
            _decalOrmCache[(rKey, mKey)] = tex;
            return tex;
        }
        public ImageTexture GetOrCreateNormalTexture(Texture2D albedoTex, float normalStrength, string decalKey)
        {
            if (albedoTex == null || normalStrength <= 0.01f) return null;

            int sKey = Mathf.RoundToInt(Mathf.Clamp(normalStrength, 0.05f, 2.0f) * 20f);
            string cacheKey = $"{decalKey}_{albedoTex.GetInstanceId()}_{sKey}";
            if (_decalNormalCache.TryGetValue(cacheKey, out var cached) && GodotObject.IsInstanceValid(cached)) return cached;

            var albedoImg = albedoTex.GetImage();
            if (albedoImg == null) return null;

            var normTex = GenerateNormalMap(albedoImg, sKey);
            if (normTex != null)
            {
                _decalNormalCache[cacheKey] = normTex;
            }

            return normTex;
        }

        public void ApplyDecalRenderingProperties(
            Decal decal,
            float brightness,
            Color tint,
            float contrast,
            float saturation,
            float opacity,
            float albedoMix,
            float normalStrength,
            float roughness,
            float metallic,
            string blendMode,
            string decalKey = "")
        {
            if (decal == null || !GodotObject.IsInstanceValid(decal)) return;

            decal.CullMask = Realm.Client.RuntimeTerrain.TerrainDecalCullMask;

            ApplyDecalColorModulation(decal, brightness, tint, contrast, saturation, opacity);
            ApplyDecalORM(decal, roughness, metallic);
            ApplyDecalNormal(decal, normalStrength, decalKey);
            ApplyDecalBlendMode(decal, blendMode, brightness, albedoMix);

            if (decal is Realm.Client.Decal3D decal3DNode)
            {
                decal3DNode.SetBaseProperties(decal.Modulate, decal.EmissionEnergy);
            }
        }

        private void ApplyDecalColorModulation(Decal decal, float brightness, Color tint, float contrast, float saturation, float opacity)
        {
            float r = tint.R * brightness;
            float g = tint.G * brightness;
            float b = tint.B * brightness;

            float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            r = lum + (r - lum) * saturation;
            g = lum + (g - lum) * saturation;
            b = lum + (b - lum) * saturation;

            r = (r - 0.5f) * contrast + 0.5f;
            g = (g - 0.5f) * contrast + 0.5f;
            b = (b - 0.5f) * contrast + 0.5f;

            decal.Modulate = new Color(Mathf.Clamp(r, 0f, 4f), Mathf.Clamp(g, 0f, 4f), Mathf.Clamp(b, 0f, 4f), Mathf.Clamp(opacity, 0f, 1f));
        }

        private void ApplyDecalORM(Decal decal, float roughness, float metallic)
        {
            if (roughness >= 0.99f && metallic <= 0.01f)
            {
                decal.TextureOrm = null;
                return;
            }
            decal.TextureOrm = GetOrCreateOrmTexture(roughness, metallic);
        }

        private void ApplyDecalNormal(Decal decal, float normalStrength, string decalKey)
        {
            if (normalStrength <= 0.01f)
            {
                if (decal is Realm.Client.Decal3D d3dDisable) d3dDisable.NormalEnabled = false;
                decal.TextureNormal = null;
                return;
            }

            if (decal is Realm.Client.Decal3D d3dEnable) d3dEnable.NormalEnabled = true;
            if (decal.TextureNormal == null && decal.TextureAlbedo != null)
            {
                decal.TextureNormal = GetOrCreateNormalTexture(decal.TextureAlbedo, normalStrength, decalKey);
            }
        }

        private void ApplyDecalBlendMode(Decal decal, string blendMode, float brightness, float albedoMix)
        {
            switch (blendMode?.ToLowerInvariant())
            {
                case "additive":
                    decal.AlbedoMix = 0.0f;
                    decal.TextureEmission = decal.TextureAlbedo;
                    decal.EmissionEnergy = brightness * albedoMix * 2.0f;
                    break;
                case "screen":
                    decal.AlbedoMix = albedoMix * 0.4f;
                    decal.TextureEmission = decal.TextureAlbedo;
                    decal.EmissionEnergy = brightness * albedoMix * 1.5f;
                    break;
                case "multiply":
                case "mix":
                default:
                    decal.AlbedoMix = albedoMix;
                    decal.TextureEmission = null;
                    decal.EmissionEnergy = 0.0f;
                    break;
            }
        }

        public void RefreshDecalsLive(
            string decalKey,
            float brightness,
            Color tint,
            float contrast,
            float saturation,
            float opacity,
            float albedoMix,
            float normalStrength,
            float roughness,
            float metallic,
            string blendMode,
            bool animateOpacity = false,
            float opacityPulseSpeed = 1.0f,
            float minOpacity = 0.2f,
            float maxOpacity = 1.0f,
            bool animateEmission = false,
            float emissionPulseSpeed = 1.0f,
            float minEmission = 0.0f,
            float maxEmission = 2.0f,
            bool animateScale = false,
            float scalePulseSpeed = 1.0f,
            float minScaleRatio = 0.8f,
            float maxScaleRatio = 1.2f,
            float upperFade = 0.3f,
            float lowerFade = 0.3f)
        {
            if (string.IsNullOrEmpty(decalKey) || AllDecals == null) return;
            string baseKey = System.IO.Path.GetFileNameWithoutExtension(decalKey);

            foreach (var decal in AllDecals)
            {
                if (decal != null && GodotObject.IsInstanceValid(decal))
                {
                    string dId = decal is Realm.Client.Decal3D d3d ? d3d.DecalId : "";
                    string dBase = System.IO.Path.GetFileNameWithoutExtension(dId);
                    if (dId.Equals(decalKey, StringComparison.OrdinalIgnoreCase) || dBase.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyDecalRenderingProperties(
                            decal,
                            brightness,
                            tint,
                            contrast,
                            saturation,
                            opacity,
                            albedoMix,
                            normalStrength,
                            roughness,
                            metallic,
                            blendMode,
                            decalKey
                        );

                        if (decal is Realm.Client.Decal3D targetD3d)
                        {
                            targetD3d.SetPropertyAnimation(
                                animateOpacity, opacityPulseSpeed, minOpacity, maxOpacity,
                                animateEmission, emissionPulseSpeed, minEmission, maxEmission,
                                animateScale, scalePulseSpeed, minScaleRatio, maxScaleRatio,
                                upperFade, lowerFade
                            );
                        }
                        else
                        {
                            decal.UpperFade = upperFade;
                            decal.LowerFade = lowerFade;
                        }
                    }
                }
            }
        }

        private void UpdateDecalSelectionRing(Decal decal, bool selected)
        {
            if (!GodotObject.IsInstanceValid(decal)) return;
            var existingNodes = decal.GetChildren()
                .Where(child => child is MeshInstance3D mesh && (mesh.Name == "_selection_ring_decal" || mesh.Name == "EditorSelectionRing"))
                .ToList();
            foreach (var node in existingNodes)
            {
                decal.RemoveChild(node);
                node.QueueFree();
            }
            if (selected)
            {
                UpdateDecalHoverRing(decal, false);
                var ring = new MeshInstance3D();
                ring.Name = "_selection_ring_decal";
                ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                ring.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
                var torusMesh = new TorusMesh();
                torusMesh.InnerRadius = 2.5f;
                torusMesh.OuterRadius = 2.8f;
                ring.Mesh = torusMesh;
                ring.Position = new Vector3(0, 0.05f, 0);
                var material = new StandardMaterial3D();
                material.AlbedoColor = new Color(0.22f, 0.54f, 0.26f);
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                material.DisableReceiveShadows = true;
                material.EmissionEnabled = false;
                material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                ring.MaterialOverride = material;
                decal.AddChild(ring);
            }
        }

        private void UpdateDecalHoverRing(Decal decal, bool hovered)
        {
            if (!GodotObject.IsInstanceValid(decal)) return;
            var existingNodes = decal.GetChildren()
                .Where(child => child is MeshInstance3D mesh && mesh.Name == "_hover_ring_decal")
                .ToList();
            foreach (var node in existingNodes)
            {
                decal.RemoveChild(node);
                node.QueueFree();
            }
            if (hovered && SelectedEditorObject != decal)
            {
                var ring = new MeshInstance3D();
                ring.Name = "_hover_ring_decal";
                ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                ring.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
                var torusMesh = new TorusMesh();
                torusMesh.InnerRadius = 2.5f;
                torusMesh.OuterRadius = 2.8f;
                ring.Mesh = torusMesh;
                ring.Position = new Vector3(0, 0.05f, 0);
                var material = new StandardMaterial3D();
                material.AlbedoColor = new Color(0.88f, 0.88f, 0.88f, 0.22f);
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                material.DisableReceiveShadows = true;
                material.EmissionEnabled = false;
                material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                ring.MaterialOverride = material;
                decal.AddChild(ring);
            }
        }

        internal void UpdateEditorCoverageOverlay()
        {
            EnsureCoverageOverlayRootExists();
            ClearCoverageOverlayChildren();

            if (!EditorCoverageOverlayEnabled || !TryGetSelectedUnit(out var unit))
            {
                _editorCoverageOverlayRoot.Visible = false;
                return;
            }

            float scanRadius = EcsWorld.Has<ScanRadius>(unit.Entity) ? EcsWorld.Get<ScanRadius>(unit.Entity).Value : 0f;
            float range = EcsWorld.Has<Attack>(unit.Entity) ? EcsWorld.Get<Attack>(unit.Entity).Range : 0f;

            _editorCoverageOverlayRoot.Visible = scanRadius > 0f || range > 0f;
            if (!_editorCoverageOverlayRoot.Visible) return;

            _editorCoverageOverlayRoot.Position = unit.Position;
            if (scanRadius > 0f) CreateCoverageRing(scanRadius, new Color(0.3f, 0.7f, 1.0f, 0.6f));
            if (range > 0f) CreateCoverageRing(range, new Color(1.0f, 0.5f, 0.1f, 0.7f));
        }

        private void EnsureCoverageOverlayRootExists()
        {
            if (_editorCoverageOverlayRoot != null) return;
            
            _editorCoverageOverlayRoot = new Node3D();
            _editorCoverageOverlayRoot.Name = "EditorCoverageOverlay";
            AddChild(_editorCoverageOverlayRoot);
        }

        private void ClearCoverageOverlayChildren()
        {
            if (_editorCoverageOverlayRoot == null) return;
            
            foreach (Node child in _editorCoverageOverlayRoot.GetChildren())
            {
                child.QueueFree();
            }
        }

        private bool TryGetSelectedUnit(out Realm.Client.Unit3D unit)
        {
            unit = _selectedEditorObject as Realm.Client.Unit3D;
            return unit != null && EcsWorld.IsAlive(unit.Entity);
        }

        private void CreateCoverageRing(float radius, Color color)
        {
            var meshInstance = new MeshInstance3D();
            meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            meshInstance.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
            var torusMesh = new TorusMesh();
            torusMesh.InnerRadius = Mathf.Max(radius - 0.25f, 0.05f);
            torusMesh.OuterRadius = radius + 0.25f;
            torusMesh.Rings = 32;
            meshInstance.Mesh = torusMesh;
            meshInstance.Position = new Vector3(0, 0.3f, 0);
            meshInstance.Scale = new Vector3(1f, 0.04f, 1f);

            var material = new StandardMaterial3D();
            material.AlbedoColor = color;
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            material.DisableReceiveShadows = true;
            material.EmissionEnabled = true;
            material.Emission = new Color(color.R, color.G, color.B);
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            meshInstance.MaterialOverride = material;

            _editorCoverageOverlayRoot.AddChild(meshInstance);
        }

        public void UpdateMeasureVisuals(Vector3 start, Vector3 end)
        {
            if (_measureMeshInstance == null)
            {
                _measureMeshInstance = new MeshInstance3D();
                _measureMeshInstance.Name = "TapeMeasureIndicator";
                _measureImmediateMesh = new ImmediateMesh();
                _measureMeshInstance.Mesh = _measureImmediateMesh;
                var mat = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = new Color(1.0f, 0.85f, 0.1f, 0.95f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled
                };
                _measureMeshInstance.MaterialOverride = mat;
                AddChild(_measureMeshInstance);
            }

            _measureImmediateMesh.ClearSurfaces();
            _measureImmediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            _measureImmediateMesh.SurfaceAddVertex(start + new Vector3(0, 0.2f, 0));
            _measureImmediateMesh.SurfaceAddVertex(end + new Vector3(0, 0.2f, 0));

            float markerSize = 0.5f;
            _measureImmediateMesh.SurfaceAddVertex(start + new Vector3(-markerSize, 0.2f, 0));
            _measureImmediateMesh.SurfaceAddVertex(start + new Vector3(markerSize, 0.2f, 0));
            _measureImmediateMesh.SurfaceAddVertex(start + new Vector3(0, 0.2f, -markerSize));
            _measureImmediateMesh.SurfaceAddVertex(start + new Vector3(0, 0.2f, markerSize));

            _measureImmediateMesh.SurfaceAddVertex(end + new Vector3(-markerSize, 0.2f, 0));
            _measureImmediateMesh.SurfaceAddVertex(end + new Vector3(markerSize, 0.2f, 0));
            _measureImmediateMesh.SurfaceAddVertex(end + new Vector3(0, 0.2f, -markerSize));
            _measureImmediateMesh.SurfaceAddVertex(end + new Vector3(0, 0.2f, markerSize));

            _measureImmediateMesh.SurfaceEnd();
            _measureMeshInstance.Visible = true;
        }

        public void ShowScaleMapSilhouette(int previewWidth, int previewDepth)
        {
            if (GroundTerrain == null) return;

            float targetWidthSize = previewWidth * GroundTerrain.QuadSize;
            float targetDepthSize = previewDepth * GroundTerrain.QuadSize;

            if (_scaleMapSilhouetteMesh != null && GodotObject.IsInstanceValid(_scaleMapSilhouetteMesh))
            {
                if (_scaleMapSilhouetteMesh.Mesh is PlaneMesh existingPlane)
                {
                    existingPlane.Size = new Godot.Vector2(targetWidthSize, targetDepthSize);
                    return;
                }
            }

            HideScaleMapSilhouette();

            _scaleMapSilhouetteMesh = new MeshInstance3D();
            _scaleMapSilhouetteMesh.Name = "ScaleMapSilhouette";

            var plane = new PlaneMesh();
            plane.Size = new Godot.Vector2(targetWidthSize, targetDepthSize);
            plane.SubdivideWidth = 0;
            plane.SubdivideDepth = 0;
            _scaleMapSilhouetteMesh.Mesh = plane;

            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(0.8f, 0.5f, 0.05f, 0.25f);
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            mat.NoDepthTest = true;
            mat.RenderPriority = 10;
            mat.EmissionEnabled = true;
            mat.Emission = new Color(1.0f, 0.6f, 0.1f) * 0.4f;
            _scaleMapSilhouetteMesh.MaterialOverride = mat;

            _scaleMapSilhouetteMesh.Position = new Godot.Vector3(0f, 1.0f, 0f);

            AddChild(_scaleMapSilhouetteMesh);
        }

        public void HideScaleMapSilhouette()
        {
            if (_scaleMapSilhouetteMesh != null)
            {
                _scaleMapSilhouetteMesh.QueueFree();
                _scaleMapSilhouetteMesh = null;
            }
        }

        public void RebuildCameraBoundsOverlay()
        {
            if (_cameraBoundsOverlayMesh == null || GroundTerrain == null || GroundTerrain.Cells == null) return;
            if (!EditorCameraBoundsVisible) return;

            int width = GroundTerrain.Width;
            int depth = GroundTerrain.Depth;
            float quadSize = GroundTerrain.QuadSize;

            float halfW = width / 2.0f;
            float halfD = depth / 2.0f;

            float minWorldX = -halfW * quadSize;
            float maxWorldX = halfW * quadSize;
            float minWorldZ = -halfD * quadSize;
            float maxWorldZ = halfD * quadSize;

            float left = Mathf.Clamp(EditorCameraBoundsLeft, minWorldX, maxWorldX);
            float right = Mathf.Clamp(EditorCameraBoundsRight, minWorldX, maxWorldX);
            float top = Mathf.Clamp(EditorCameraBoundsTop, minWorldZ, maxWorldZ);
            float bottom = Mathf.Clamp(EditorCameraBoundsBottom, minWorldZ, maxWorldZ);

            var linePoints = new List<Vector3>();

            float GetTerrainHeightAtCoord(float worldX, float worldZ)
            {
                if (GroundTerrain == null || GroundTerrain.Cells == null) return 0f;
                float gridX = worldX / quadSize + halfW;
                float gridZ = worldZ / quadSize + halfD;
                int x0 = Mathf.Clamp((int)Mathf.Floor(gridX), 0, width - 1);
                int x1 = Mathf.Clamp(x0 + 1, 0, width - 1);
                int z0 = Mathf.Clamp((int)Mathf.Floor(gridZ), 0, depth - 1);
                int z1 = Mathf.Clamp(z0 + 1, 0, depth - 1);

                float tx = gridX - x0;
                float tz = gridZ - z0;

                var cells = GroundTerrain.Cells;
                float h00 = Realm.Client.EditableTerrain.GetGridNodeHeight(x0, z0, cells, width, depth);
                float h10 = Realm.Client.EditableTerrain.GetGridNodeHeight(x1, z0, cells, width, depth);
                float h01 = Realm.Client.EditableTerrain.GetGridNodeHeight(x0, z1, cells, width, depth);
                float h11 = Realm.Client.EditableTerrain.GetGridNodeHeight(x1, z1, cells, width, depth);

                float h0 = Mathf.Lerp(h00, h10, tx);
                float h1 = Mathf.Lerp(h01, h11, tx);
                return Mathf.Lerp(h0, h1, tz);
            }

            void AddSegmentedLine(float x1, float z1, float x2, float z2)
            {
                float dist = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
                int segments = Mathf.Max(1, (int)Mathf.Ceil(dist / quadSize));
                for (int i = 0; i < segments; i++)
                {
                    float t1 = (float)i / segments;
                    float t2 = (float)(i + 1) / segments;

                    float lx1 = Mathf.Lerp(x1, x2, t1);
                    float lz1 = Mathf.Lerp(z1, z2, t1);
                    float lx2 = Mathf.Lerp(x1, x2, t2);
                    float lz2 = Mathf.Lerp(z1, z2, t2);

                    float y1 = GetTerrainHeightAtCoord(lx1, lz1) + 0.2f;
                    float y2 = GetTerrainHeightAtCoord(lx2, lz2) + 0.2f;

                    linePoints.Add(new Vector3(lx1, y1, lz1));
                    linePoints.Add(new Vector3(lx2, y2, lz2));
                }
            }

            AddSegmentedLine(left, top, right, top);
            AddSegmentedLine(right, top, right, bottom);
            AddSegmentedLine(right, bottom, left, bottom);
            AddSegmentedLine(left, bottom, left, top);

            int totalVertices = linePoints.Count * 3;
            var vertices = new Vector3[totalVertices];
            var colors = new Color[totalVertices];
            int idx = 0;

            Color boundsColor = new Color(0.9f, 0.1f, 0.8f, 0.95f);

            for (int i = 0; i < linePoints.Count; i += 2)
            {
                Vector3 p1 = linePoints[i];
                Vector3 p2 = linePoints[i + 1];

                vertices[idx] = p1;
                colors[idx] = boundsColor;
                idx++;
                vertices[idx] = p2;
                colors[idx] = boundsColor;
                idx++;

                Vector3 dir = (p2 - p1).Normalized();
                Vector3 ortho = new Vector3(-dir.Z, 0, dir.X) * 0.08f;

                vertices[idx] = p1 + ortho;
                colors[idx] = boundsColor;
                idx++;
                vertices[idx] = p2 + ortho;
                colors[idx] = boundsColor;
                idx++;

                vertices[idx] = p1 - ortho;
                colors[idx] = boundsColor;
                idx++;
                vertices[idx] = p2 - ortho;
                colors[idx] = boundsColor;
                idx++;
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.Color] = colors;

            var arrayMesh = new ArrayMesh();
            arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
            _cameraBoundsOverlayMesh.Mesh = arrayMesh;
        }

        private void InitializeCameraBoundsOverlay()
        {
            if (_cameraBoundsOverlayMesh != null) return;

            _cameraBoundsOverlayMesh = new MeshInstance3D();
            _cameraBoundsOverlayMesh.Name = "CameraBoundsOverlay";

            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.NoDepthTest = false;
            mat.VertexColorUseAsAlbedo = true;
            _cameraBoundsOverlayMesh.MaterialOverride = mat;

            AddChild(_cameraBoundsOverlayMesh);
            _cameraBoundsOverlayMesh.Visible = false;
        }

        private void RebuildPathingOverlay()
        {
            if (GroundTerrain == null || GroundTerrain.PathingCodes == null || GroundTerrain.Cells == null) return;
            GroundTerrain.UpdatePathingTexture();
        }

        public void RebuildGridOverlayMeshExternal()
        {
            // No longer needed, grid is on shader
        }

        public void UpdateGridOverlayVisibility()
        {
            if (GroundTerrain != null)
            {
                bool gridVisible = IsMapEditorMode && (EditorGridMode == GridOverlayMode.Grid || EditorGridMode == GridOverlayMode.Both);
                GroundTerrain.SetGridVisible(gridVisible);
                bool polarVisible = IsMapEditorMode && (EditorMirrorMode == MirrorMode.Rotational || EditorGridMode == GridOverlayMode.Polar || EditorGridMode == GridOverlayMode.Both);
                GroundTerrain.SetPolarOverlayVisible(polarVisible);
                EditorPolarOverlayVisible = polarVisible;
            }
            UpdateSymmetryPivotVisuals();
        }

        public void UpdatePolarOverlayVisibility()
        {
            UpdateGridOverlayVisibility();
        }

        private MeshInstance3D GetOrCreateSymmetryHighlightMesh(int index)
        {
            while (_symmetryHighlightMeshes.Count <= index)
            {
                var meshInst = new MeshInstance3D();
                meshInst.Name = $"SymmetrySelectionHighlight_{_symmetryHighlightMeshes.Count}";
                var mat = new StandardMaterial3D();
                mat.AlbedoColor = new Color(0.0f, 0.6f, 1.0f, 0.35f);
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                meshInst.MaterialOverride = mat;
                AddChild(meshInst);
                meshInst.Visible = false;
                _symmetryHighlightMeshes.Add(meshInst);
            }
            return _symmetryHighlightMeshes[index];
        }

        private void CreateSelectionHighlight()
        {
            if (_selectionHighlightMesh != null) return;
            _selectionHighlightMesh = new MeshInstance3D();
            _selectionHighlightMesh.Name = "SelectionHighlight";
            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(0.0f, 0.6f, 1.0f, 0.35f);
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            _selectionHighlightMesh.MaterialOverride = mat;
            AddChild(_selectionHighlightMesh);
            _selectionHighlightMesh.Visible = false;
        }

        public void InvalidateSelectionHighlightMesh()
        {
            _lastSelectionMinX = -1;
            _lastSelectionMinZ = -1;
            _lastSelectionMaxX = -1;
            _lastSelectionMaxZ = -1;
        }

        public void RebuildSelectionHighlightMeshExternal(int minX, int minZ, int maxX, int maxZ)
        {
            _lastSelectionMinX = -1;
            if (ActiveEditorTool == EditorTool.PasteArea && _editorService.HasCopiedArea)
            {
                int w = maxX - minX + 1;
                int d = maxZ - minZ + 1;
                UpdatePasteSelectionHighlights(minX, minZ, w, d);
            }
            else
            {
                RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
            }
        }
        private void BuildHighlightMeshForBounds(MeshInstance3D meshInst, int minX, int minZ, int maxX, int maxZ)
        {
            if (meshInst == null || GroundTerrain == null || GroundTerrain.Cells == null) return;
            int selWidth = maxX - minX + 1;
            int selDepth = maxZ - minZ + 1;
            if (selWidth < 2 || selDepth < 2)
            {
                meshInst.Visible = false;
                return;
            }

            var vertices = BuildHighlightMeshVertices(selWidth, selDepth, minX, minZ);
            var indices = BuildHighlightMeshIndices(selWidth, selDepth, minX, minZ, maxX, maxZ);

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.Index] = indices;

            var arrayMesh = new ArrayMesh();
            arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            meshInst.Mesh = arrayMesh;
            meshInst.Visible = true;
        }

        private int[] BuildHighlightMeshIndices(int selWidth, int selDepth, int minX, int minZ, int maxX, int maxZ)
        {
            int cellWidth = selWidth - 1;
            int cellDepth = selDepth - 1;
            int indexCount = cellWidth * cellDepth * 6;
            var indices = new int[indexCount];
            int iIdx = 0;

            float selCenterX = (minX + maxX) * 0.5f;
            float selCenterZ = (minZ + maxZ) * 0.5f;
            float rx = Math.Max(0.5f, (maxX - minX) * 0.5f);
            float rz = Math.Max(0.5f, (maxZ - minZ) * 0.5f);

            for (int sz = 0; sz < cellDepth; sz++)
            {
                int row0 = sz * selWidth;
                int row1 = (sz + 1) * selWidth;
                for (int sx = 0; sx < cellWidth; sx++)
                {
                    if (ShouldIncludeQuad(sx, sz, minX, minZ, selCenterX, selCenterZ, rx, rz))
                    {
                        AddQuadIndices(indices, ref iIdx, row0, row1, sx);
                    }
                }
            }

            if (iIdx < indexCount) System.Array.Resize(ref indices, iIdx);
            return indices;
        }

        private void RebuildSelectionHighlightMesh(int minX, int minZ, int maxX, int maxZ)
        {
            if (!CanRebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ)) return;

            _lastSelectionMinX = minX;
            _lastSelectionMinZ = minZ;
            _lastSelectionMaxX = maxX;
            _lastSelectionMaxZ = maxZ;
            _lastSelectionBrushIsSquare = EditorBrushIsSquare;

            BuildHighlightMeshForBounds(_selectionHighlightMesh, minX, minZ, maxX, maxZ);
        }

        private bool IsSelectionUnchanged(int minX, int minZ, int maxX, int maxZ)
        {
            return minX == _lastSelectionMinX
                && minZ == _lastSelectionMinZ
                && maxX == _lastSelectionMaxX
                && maxZ == _lastSelectionMaxZ
                && _lastSelectionBrushIsSquare == EditorBrushIsSquare
                && _selectionHighlightMesh.Visible;
        }

        private bool CanRebuildSelectionHighlightMesh(int minX, int minZ, int maxX, int maxZ)
        {
            if (_selectionHighlightMesh == null) return false;
            if (GroundTerrain == null) return false;
            if (GroundTerrain.Cells == null) return false;
            
            int selWidth = maxX - minX + 1;
            int selDepth = maxZ - minZ + 1;
            if (selWidth < 2 || selDepth < 2)
            {
                _selectionHighlightMesh.Visible = false;
                _lastSelectionMinX = -1;
                return false;
            }

            if (IsSelectionUnchanged(minX, minZ, maxX, maxZ))
            {
                return false;
            }

            return true;
        }

        public void HideCoordinatePreviewMesh()
        {
            if (_coordinatePreviewMesh != null) _coordinatePreviewMesh.Visible = false;
        }

        private void UpdateCoordinateOutline(EditorCoordinate coord)
        {
            if (_coordinateSelectionOutlineMesh == null)
            {
                CreateCoordinateSelectionOutlineMesh();
            }

            int width = GroundTerrain.Width;
            int depth = GroundTerrain.Depth;
            float quadSize = GroundTerrain.QuadSize;

            int minX = Mathf.Clamp((int)Mathf.Round(coord.MinX / quadSize + width / 2.0f), 0, width);
            int minZ = Mathf.Clamp((int)Mathf.Round(coord.MinZ / quadSize + depth / 2.0f), 0, depth);
            int maxX = Mathf.Clamp((int)Mathf.Round(coord.MaxX / quadSize + width / 2.0f), 0, width);
            int maxZ = Mathf.Clamp((int)Mathf.Round(coord.MaxZ / quadSize + depth / 2.0f), 0, depth);

            _coordinateSelectionOutlineMesh.Visible = true;
            RebuildCoordinateMeshInstance(_coordinateSelectionOutlineMesh, minX, minZ, maxX, maxZ, new Color(1.0f, 0.6f, 0.0f, 0.45f), 0.25f);
        }

        private void CreateCoordinateSelectionOutlineMesh()
        {
            _coordinateSelectionOutlineMesh = new MeshInstance3D();
            _coordinateSelectionOutlineMesh.Name = "CoordinateSelectionOutline";

            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(1.0f, 0.6f, 0.0f, 0.45f);
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

            _coordinateSelectionOutlineMesh.MaterialOverride = mat;
            AddChild(_coordinateSelectionOutlineMesh);
        }

        public void HideCoordinateSelectionOutline()
        {
            if (_coordinateSelectionOutlineMesh != null && GodotObject.IsInstanceValid(_coordinateSelectionOutlineMesh))
            {
                _coordinateSelectionOutlineMesh.Visible = false;
                _coordinateSelectionOutlineMesh.Mesh = null;
            }
        }

        public void UpdateCameraBoundsOverlayVisibility()
        {
            if (_cameraBoundsOverlayMesh == null) return;
            _cameraBoundsOverlayMesh.Visible = IsMapEditorMode && EditorCameraBoundsVisible;
            if (_cameraBoundsOverlayMesh.Visible)
            {
                RebuildCameraBoundsOverlay();
            }
        }

        public void UpdatePathingOverlay()
        {
            bool isPathingTool = ActiveEditorTool == EditorTool.PaintPathing || ActiveEditorTool == EditorTool.FloodFillPathing;
            bool isClipboardTool = ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea;

            bool shouldBeVisible = IsMapEditorMode && PathingOverlayVisible && (isPathingTool || (isClipboardTool && PasteOptionPathing));

            if (GroundTerrain != null)
            {
                GroundTerrain.SetPathingVisible(shouldBeVisible);
                if (shouldBeVisible)
                {
                    RebuildPathingOverlay();
                }
            }
        }

        public void BeginMinimapCapture()
        {
            _wasSelectionHighlightVisible = HideAndCacheNodeVisibility(_selectionHighlightMesh);
            HideAndCacheSymmetryHighlights();
            _wasCameraBoundsVisible = HideAndCacheNodeVisibility(_cameraBoundsOverlayMesh);
            _wasMeasureMeshVisible = HideAndCacheNodeVisibility(_measureMeshInstance);
            _wasSymmetryPivotVisible = HideAndCacheNodeVisibility(_symmetryPivotMarkerMesh);
            _wasCoordinatePreviewVisible = HideAndCacheNodeVisibility(_coordinatePreviewMesh);
            HideAndCacheNodeVisibility(_coordinateSelectionOutlineMesh, out _wasCoordinateOutlineVisible);
            HideAndCacheNodeVisibility(_scaleMapSilhouetteMesh, out _wasScaleSilhouetteVisible);
            _wasCoverageOverlayVisible = HideAndCacheNodeVisibility(_editorCoverageOverlayRoot);
            HideVfxEditorBaseRings();
        }

        private bool HideAndCacheNodeVisibility(Node3D node)
        {
            bool wasVisible = false;
            if (node == null || !GodotObject.IsInstanceValid(node)) return wasVisible;
            
            wasVisible = node.Visible;
            if (wasVisible)
            {
                node.Visible = false;
            }
            return wasVisible;
        }

        private void HideAndCacheNodeVisibility(Node3D node, out bool wasVisible)
        {
            wasVisible = HideAndCacheNodeVisibility(node);
        }

        private void HideAndCacheSymmetryHighlights()
        {
            _wasSymmetryHighlightsVisible.Clear();
            if (_symmetryHighlightMeshes == null) return;

            foreach (var symMesh in _symmetryHighlightMeshes)
            {
                bool vis = symMesh != null && GodotObject.IsInstanceValid(symMesh) && symMesh.Visible;
                _wasSymmetryHighlightsVisible.Add(vis);
                if (vis)
                {
                    symMesh.Visible = false;
                }
            }
        }

        private void HideVfxEditorBaseRings()
        {
            if (AllVfx == null) return;
            foreach (var vfx in AllVfx)
            {
                if (vfx != null && GodotObject.IsInstanceValid(vfx))
                {
                    vfx.SetEditorBaseRingVisible(false);
                }
            }
        }

        public void EndMinimapCapture()
        {
            ShowVfxEditorBaseRings();
            RestoreNodeVisibility(_selectionHighlightMesh, _wasSelectionHighlightVisible);
            RestoreSymmetryHighlights();
            RestoreNodeVisibility(_cameraBoundsOverlayMesh, _wasCameraBoundsVisible);
            RestoreNodeVisibility(_measureMeshInstance, _wasMeasureMeshVisible);
            RestoreNodeVisibility(_symmetryPivotMarkerMesh, _wasSymmetryPivotVisible);
            RestoreNodeVisibility(_coordinatePreviewMesh, _wasCoordinatePreviewVisible);
            RestoreNodeVisibility(_coordinateSelectionOutlineMesh, _wasCoordinateOutlineVisible);
            RestoreNodeVisibility(_scaleMapSilhouetteMesh, _wasScaleSilhouetteVisible);
            RestoreNodeVisibility(_editorCoverageOverlayRoot, _wasCoverageOverlayVisible);
        }

        private void RestoreNodeVisibility(Node3D node, bool wasVisible)
        {
            if (node != null && GodotObject.IsInstanceValid(node))
            {
                node.Visible = wasVisible;
            }
        }

        private void RestoreSymmetryHighlights()
        {
            if (_symmetryHighlightMeshes == null || _wasSymmetryHighlightsVisible == null) return;
            
            for (int i = 0; i < _wasSymmetryHighlightsVisible.Count && i < _symmetryHighlightMeshes.Count; i++)
            {
                var symMesh = _symmetryHighlightMeshes[i];
                if (symMesh != null && GodotObject.IsInstanceValid(symMesh))
                {
                    symMesh.Visible = _wasSymmetryHighlightsVisible[i];
                }
            }
        }

        private void ShowVfxEditorBaseRings()
        {
            if (AllVfx == null) return;
            foreach (var vfx in AllVfx)
            {
                if (vfx != null && GodotObject.IsInstanceValid(vfx))
                {
                    vfx.SetEditorBaseRingVisible(true);
                }
            }
        }
    }
}
