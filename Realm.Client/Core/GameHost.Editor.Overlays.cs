using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Array = Godot.Collections.Array;

namespace Realm.Client.Core
{
	public partial class GameHost
	{
		private bool IsMouseOverUI()
		{
			if (GameContext.IsSettingsMenuOpen)
			{
				return true;
			}

			if (WasmConsoleWindow.IsAnyConsoleUnderPointer)
			{
				return true;
			}

			if (GodotObject.IsInstanceValid(GameContext.MapEditorHud))
			{
				return GameContext.MapEditorHud.IsMouseOverUI(GetViewport().GetMousePosition());
			}

			if (GodotObject.IsInstanceValid(GameContext.InGameHud))
			{
				return GameContext.InGameHud.IsMouseOverUI(GetViewport().GetMousePosition());
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
			string baseKey = Path.GetFileNameWithoutExtension(decalId);
			GameContext.EditorService.Decals.Assets.Remove(decalId);
			GameContext.EditorService.Decals.Assets.Remove(baseKey);
			GameContext.EditorService.Decals.Assets.Remove($"{baseKey}.rtex");
			GameContext.EditorService.Decals.Assets.Remove($"{baseKey}.png");
		}

		public ImageTexture GetOrCreateOrmTexture(float roughness, float metallic)
		{
			int rKey = Mathf.RoundToInt(Mathf.Clamp(roughness, 0f, 1f) * 100f);
			int mKey = Mathf.RoundToInt(Mathf.Clamp(metallic, 0f, 1f) * 100f);
			if (GameContext.EditorService.Decals.OrmTextures.TryGetValue((rKey, mKey), out var cached) && GodotObject.IsInstanceValid(cached))
			{
				return cached;
			}

			var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
			img.Fill(new Color(1.0f, rKey / 100f, mKey / 100f, 1.0f));
			var tex = ImageTexture.CreateFromImage(img);
			GameContext.EditorService.Decals.OrmTextures[(rKey, mKey)] = tex;
			return tex;
		}

		public ImageTexture? GetOrCreateNormalTexture(Texture2D albedoTex, float normalStrength, string decalKey)
		{
			if (albedoTex == null || normalStrength <= 0.01f) return null;

			int sKey = Mathf.RoundToInt(Mathf.Clamp(normalStrength, 0.05f, 2.0f) * 20f);
			string cacheKey = $"{decalKey}_{albedoTex.GetInstanceId()}_{sKey}";
			if (GameContext.EditorService.Decals.NormalTextures.TryGetValue(cacheKey, out var cached) && GodotObject.IsInstanceValid(cached)) return cached;

			var albedoImg = albedoTex.GetImage();
			if (albedoImg == null) return null;

			var normTex = GenerateNormalMap(albedoImg, sKey);
			if (normTex != null)
			{
				GameContext.EditorService.Decals.NormalTextures[cacheKey] = normTex;
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

			decal.CullMask = RuntimeTerrain.TerrainDecalCullMask;

			ApplyDecalColorModulation(decal, brightness, tint, contrast, saturation, opacity);
			ApplyDecalORM(decal, roughness, metallic);
			ApplyDecalNormal(decal, normalStrength, decalKey);
			ApplyDecalBlendMode(decal, blendMode, brightness, albedoMix);

			if (decal is Decal3D decal3DNode)
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
				if (decal is Decal3D d3dDisable) d3dDisable.NormalEnabled = false;
				decal.TextureNormal = null;
				return;
			}

			if (decal is Decal3D d3dEnable) d3dEnable.NormalEnabled = true;
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
			float lowerFade = 0.3f,
			VfxPlacementMode placementMode = VfxPlacementMode.Free,
			float projectionSize = 0.0f,
			float projectionHeight = 0.0f,
			float groundOffset = 0.05f,
			float normalFade = 0.0f,
			bool distanceFadeEnabled = false,
			float distanceFadeBegin = 10.0f,
			float distanceFadeEnd = 30.0f)
		{
			if (string.IsNullOrEmpty(decalKey) || AllDecals == null) return;
			string baseKey = Path.GetFileNameWithoutExtension(decalKey);

			foreach (var decal in AllDecals)
			{
				if (decal != null && GodotObject.IsInstanceValid(decal))
				{
					string dId = decal is Decal3D d3d ? d3d.DecalId : "";
					string dBase = Path.GetFileNameWithoutExtension(dId);
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

						if (decal is Decal3D targetD3d)
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

						ApplyDecalPlacement(
							decal,
							placementMode,
							projectionSize,
							projectionHeight,
							groundOffset,
							normalFade,
							distanceFadeEnabled,
							distanceFadeBegin,
							distanceFadeEnd);
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

	}
}