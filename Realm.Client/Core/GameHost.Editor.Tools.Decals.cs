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
		private Decal3D? FindDecal3DInParentChain(Node? node)
		{
			if (!GameContext.SessionState.IsMapEditorMode) return null;
			Node? current = node;
			while (current != null && current != this)
			{
				if (current is Decal3D d) return d;
				current = current.GetParent();
			}

			return null;
		}

		private void ProcessMapEditorPhysics(float fDelta)
		{
			if (GameContext.SimulationService == null || GameContext.World == null) return;

			GameContext.SimulationService.TickEditorPhysics(fDelta);
			var query = QueryCache.AllPositionAndMoveToAndMovementStatsNoneDeadQuery;
			var arrivedUnits = GameContext.SimulationService.GetEditorArrivedUnits();
			arrivedUnits.Clear();
			GameContext.World.Query(in query, GameContext.SimulationService.EditorMovementQueryDelegate);

			foreach (var entity in arrivedUnits)
			{
				if (GameContext.World.IsAlive(entity) && GameContext.World.Has<MoveTo>(entity))
				{
					GameContext.World.Remove<MoveTo>(entity);
				}
			}
		}

		private ImageTexture? GenerateNormalMap(Image albedoImg, int sKey)
		{
			int w = Math.Min(albedoImg.GetWidth(), 512);
			int h = Math.Min(albedoImg.GetHeight(), 512);

			var workImg = albedoImg.Duplicate() as Image;
			if (workImg == null) return null;

			if (workImg.GetWidth() != w || workImg.GetHeight() != h) workImg.Resize(w, h, Image.Interpolation.Bilinear);
			workImg.Convert(Image.Format.Rgba8);

			var normImg = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
			float scale = (sKey / 20f) * 4.0f;

			for (int y = 0; y < h; y++)
			{
				ProcessNormalMapRow(w, h, y, workImg, normImg, scale);
			}

			return ImageTexture.CreateFromImage(normImg);
		}

		private void ProcessNormalMapRow(int w, int h, int y, Image workImg, Image normImg, float scale)
		{
			int yPrev = y > 0 ? y - 1 : y;
			int yNext = y < h - 1 ? y + 1 : y;

			for (int x = 0; x < w; x++)
			{
				int xPrev = x > 0 ? x - 1 : x;
				int xNext = x < w - 1 ? x + 1 : x;

				float lLeft = workImg.GetPixel(xPrev, y).Luminance;
				float lRight = workImg.GetPixel(xNext, y).Luminance;
				float lUp = workImg.GetPixel(x, yPrev).Luminance;
				float lDown = workImg.GetPixel(x, yNext).Luminance;

				float dx = (lRight - lLeft) * scale;
				float dy = (lDown - lUp) * scale;
				float dz = 1.0f;
				float len = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

				float nx = (-dx / len) * 0.5f + 0.5f;
				float ny = (-dy / len) * 0.5f + 0.5f;
				float nz = (dz / len) * 0.5f + 0.5f;

				normImg.SetPixel(x, y, new Color(nx, ny, nz, 1.0f));
			}
		}

		public void ApplyDecalPropertiesFromMetadata(Decal decal, string decalId)
		{
			if (decal == null || !GodotObject.IsInstanceValid(decal) || string.IsNullOrEmpty(decalId)) return;
			try
			{
				ApplyDecalPropertiesFromMetadataInternal(decal, decalId);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"could not apply the metadata of decal {decalId} to the decal node: {ex.GetType().Name}: {ex.Message}");
			}
		}

		private void ApplyDecalPropertiesFromMetadataInternal(Decal decal, string decalId)
		{
			ApplyDecalTextures(decal, decalId, out bool hasNormal);
			DecalMetadata? meta = LoadAndGetDecalMetadata(decalId);
			ApplyDecalRendering(decal, decalId, meta, hasNormal);
			ApplyDecalAnimation(decal, meta);
			ApplyDecalPlacement(decal, meta);
		}

		private void ApplyDecalPlacement(Decal decal, DecalMetadata? meta)
		{
			ApplyDecalPlacement(
				decal,
				meta?.PlacementMode ?? VfxPlacementMode.Free,
				meta?.ProjectionSize ?? 0.0f,
				meta?.ProjectionHeight ?? 0.0f,
				meta?.GroundOffset ?? 0.05f,
				meta?.NormalFade ?? 0.0f,
				meta?.DistanceFadeEnabled ?? false,
				meta?.DistanceFadeBegin ?? 10.0f,
				meta?.DistanceFadeEnd ?? 30.0f);
		}

		private void ApplyDecalPlacement(Decal decal, VfxPlacementMode placementMode, float projectionSize, float projectionHeight, float groundOffset, float normalFade, bool distanceFadeEnabled, float distanceFadeBegin, float distanceFadeEnd)
		{
			if (decal == null || !GodotObject.IsInstanceValid(decal)) return;

			if (decal is Decal3D decal3D)
			{
				decal3D.SetNormalFade(normalFade);
				decal3D.SetDistanceFade(distanceFadeEnabled, distanceFadeBegin, distanceFadeEnd);
				decal3D.SetProjectionBox(projectionSize, projectionHeight);
				decal3D.SetGroundProjection(placementMode == VfxPlacementMode.GroundProjected, groundOffset);
				return;
			}

			decal.NormalFade = Mathf.Clamp(normalFade, 0.0f, 1.0f);
			decal.DistanceFadeEnabled = distanceFadeEnabled;
			decal.DistanceFadeBegin = Mathf.Max(0.0f, distanceFadeBegin);
			decal.DistanceFadeLength = Mathf.Max(0.1f, distanceFadeEnd - decal.DistanceFadeBegin);
		}

		private void ApplyDecalTextures(Decal decal, string decalId, out bool hasNormal)
		{
			var assetData = LoadDecalAsset(decalId);
			decal.TextureAlbedo = assetData.PrimaryTexture;
			hasNormal = assetData.PrimaryNormal != null;
			if (hasNormal) decal.TextureNormal = assetData.PrimaryNormal;
		}

		private DecalMetadata? LoadAndGetDecalMetadata(string decalId)
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			var metadata = MapFileService.LoadMetadata(wsPath);
			return TryGetDecalMetadata(metadata, decalId);
		}

		private void ApplyDecalRendering(Decal decal, string decalId, DecalMetadata? meta, bool hasNormal)
		{
			float defaultNormalStrength = hasNormal ? 1.0f : 0.0f;

			if (meta == null)
			{
				ApplyDecalRenderingProperties(
					decal, 1.0f, Colors.White, 1.0f, 1.0f, 1.0f, 1.0f,
					defaultNormalStrength, 1.0f, 0.0f, "Mix", decalId
				);
				return;
			}

			float brightness = GetDecalRenderingFloat(meta.Brightness, 1.0f);
			Color tint = GetDecalColorTint(meta);
			float contrast = GetDecalRenderingFloat(meta.Contrast, 1.0f);
			float saturation = GetDecalRenderingFloat(meta.Saturation, 1.0f);
			float opacity = GetDecalRenderingFloat(meta.Opacity, 1.0f);
			float albedoMix = GetDecalRenderingFloat(meta.AlbedoMix, 1.0f);
			float normalStrength = GetDecalRenderingFloat(meta.NormalStrength, defaultNormalStrength);
			float roughness = GetDecalRenderingFloat(meta.Roughness, 1.0f);
			float metallic = meta.Metallic;
			string blendMode = meta.BlendMode ?? "Mix";

			ApplyDecalRenderingProperties(
				decal, brightness, tint, contrast, saturation, opacity, albedoMix,
				normalStrength, roughness, metallic, blendMode, decalId
			);
		}

		private void ApplyDecalAnimation(Decal decal, DecalMetadata? meta)
		{
			float upperFade = GetDecalRenderingFloat(meta?.UpperFade, 0.3f);
			float lowerFade = GetDecalRenderingFloat(meta?.LowerFade, 0.3f);
			ApplyDecalAnimationProperties(decal, meta, upperFade, lowerFade);
		}

		private float GetDecalRenderingFloat(float? val, float def)
		{
			return val > 0f ? val.Value : def;
		}

		private Color GetDecalColorTint(DecalMetadata meta)
		{
			if (meta != null && !string.IsNullOrEmpty(meta.Tint) && meta.Tint.StartsWith("#"))
				return Color.FromHtml(meta.Tint);
			return Colors.White;
		}

		private DecalMetadata? TryGetDecalMetadata(MapMetadata metadata, string decalId)
		{
			if (metadata?.Decals == null) return null;

			if (metadata.Decals.TryGetValue(decalId, out var d0)) return d0;

			string key = Path.GetFileName(decalId);
			if (metadata.Decals.TryGetValue(key, out var d1)) return d1;

			string baseKey = Path.GetFileNameWithoutExtension(decalId);
			if (metadata.Decals.TryGetValue(baseKey, out var d2)) return d2;
			if (metadata.Decals.TryGetValue($"{baseKey}.rtex", out var d3)) return d3;
			if (metadata.Decals.TryGetValue($"{baseKey}.png", out var d4)) return d4;

			return null;
		}

		private void ApplyDecalAnimationProperties(Decal decal, DecalMetadata? meta, float upperFade, float lowerFade)
		{
			if (decal is Decal3D d3d)
			{
				ApplyDecal3DAnimationProperties(d3d, meta, upperFade, lowerFade);
			}
			else
			{
				decal.UpperFade = upperFade;
				decal.LowerFade = lowerFade;
			}
		}

		private void ApplyDecal3DAnimationProperties(Decal3D d3d, DecalMetadata? meta, float upperFade, float lowerFade)
		{
			if (meta == null) return;

			bool animateOpacity = meta.AnimateOpacity;
			float opacityPulseSpeed = GetDecalRenderingFloat(meta.OpacityPulseSpeed, 1.0f);
			float minOpacity = GetDecalRenderingFloat(meta.MinOpacity, 0.2f);
			float maxOpacity = GetDecalRenderingFloat(meta.MaxOpacity, 1.0f);

			bool animateEmission = meta.AnimateEmission;
			float emissionPulseSpeed = GetDecalRenderingFloat(meta.EmissionPulseSpeed, 1.0f);
			float minEmission = meta.MinEmission;
			float maxEmission = GetDecalRenderingFloat(meta.MaxEmission, 2.0f);

			bool animateScale = meta.AnimateScale;
			float scalePulseSpeed = GetDecalRenderingFloat(meta.ScalePulseSpeed, 1.0f);
			float minScaleRatio = GetDecalRenderingFloat(meta.MinScaleRatio, 0.8f);
			float maxScaleRatio = GetDecalRenderingFloat(meta.MaxScaleRatio, 1.2f);

			d3d.SetPropertyAnimation(
				animateOpacity, opacityPulseSpeed, minOpacity, maxOpacity,
				animateEmission, emissionPulseSpeed, minEmission, maxEmission,
				animateScale, scalePulseSpeed, minScaleRatio, maxScaleRatio,
				upperFade, lowerFade
			);
		}

	}
}
