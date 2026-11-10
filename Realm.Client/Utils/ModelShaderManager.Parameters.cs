using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Realm.Client.Utils;

public static partial class ModelShaderManager
{
	private static MaterialParameters ExtractBaseMaterialParameters(BaseMaterial3D baseMat)
	{
		var p = MaterialParameters.Default;
		p.RawAlbedoTexture = baseMat.AlbedoTexture;
		p.OrmTexture = baseMat is OrmMaterial3D ormMat ? ormMat.OrmTexture : (baseMat.RoughnessTexture ?? baseMat.MetallicTexture);
		p.NormalTexture = baseMat.NormalEnabled ? baseMat.NormalTexture : null;
		p.EmissionTexture = baseMat.EmissionEnabled ? baseMat.EmissionTexture : null;
		p.AlbedoColor = baseMat.AlbedoColor;
		p.EmissionColor = baseMat.EmissionEnabled ? baseMat.Emission : new Color(0f, 0f, 0f, 1f);
		p.EmissionEnergy = baseMat.EmissionEnabled ? baseMat.EmissionEnergyMultiplier : 1f;
		p.Roughness = baseMat.Roughness;
		p.Metallic = baseMat.Metallic;
		p.Specular = baseMat.MetallicSpecular;
		p.Uv1Scale = baseMat.Uv1Scale;
		p.Uv1Offset = baseMat.Uv1Offset;

		if (baseMat.Transparency == BaseMaterial3D.TransparencyEnum.Alpha || baseMat.Transparency == BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass)
		{
			p.UseAlphaBlend = true;
		}
		else if (baseMat.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
		{
			p.UseAlphaScissor = true;
			p.AlphaScissorThreshold = baseMat.AlphaScissorThreshold;
		}

		return p;
	}

	private static MaterialParameters ExtractShaderMaterialParameters(ShaderMaterial sm)
	{
		var p = MaterialParameters.Default;
		p.RawAlbedoTexture = GetShaderTexture(sm, "texture_albedo");
		p.OrmTexture = GetShaderTexture(sm, "texture_orm");
		p.NormalTexture = GetShaderTexture(sm, "texture_normal");
		p.EmissionTexture = GetShaderTexture(sm, "texture_emission");

		p.AlbedoColor = GetShaderColor(sm, "albedo_color", p.AlbedoColor);
		p.EmissionColor = GetShaderColor(sm, "emission_color", p.EmissionColor);

		p.EmissionEnergy = GetShaderFloat(sm, "emission_energy", p.EmissionEnergy);
		p.Roughness = GetShaderFloat(sm, "roughness_value", p.Roughness);
		p.Metallic = GetShaderFloat(sm, "metallic_value", p.Metallic);
		p.Specular = GetShaderFloat(sm, "specular_value", p.Specular);
		p.AlphaScissorThreshold = GetShaderFloat(sm, "alpha_scissor_threshold", p.AlphaScissorThreshold);

		p.Uv1Scale = GetShaderVector3(sm, "uv1_scale", p.Uv1Scale);
		p.Uv1Offset = GetShaderVector3(sm, "uv1_offset", p.Uv1Offset);

		p.UseAlphaBlend = GetShaderBool(sm, "use_alpha_blend", p.UseAlphaBlend);
		p.UseAlphaScissor = GetShaderBool(sm, "use_alpha_scissor", p.UseAlphaScissor);

		return p;
	}

	private static Texture2D? GetShaderTexture(ShaderMaterial sm, string paramName)
	{
		var variant = sm.GetShaderParameter(paramName);
		return variant.VariantType != Variant.Type.Nil ? variant.As<Texture2D>() : null;
	}

	private static Color GetShaderColor(ShaderMaterial sm, string paramName, Color defaultValue)
	{
		var variant = sm.GetShaderParameter(paramName);
		return variant.VariantType != Variant.Type.Nil ? variant.As<Color>() : defaultValue;
	}

	private static float GetShaderFloat(ShaderMaterial sm, string paramName, float defaultValue)
	{
		var variant = sm.GetShaderParameter(paramName);
		return variant.VariantType != Variant.Type.Nil ? variant.As<float>() : defaultValue;
	}

	private static Vector3 GetShaderVector3(ShaderMaterial sm, string paramName, Vector3 defaultValue)
	{
		var variant = sm.GetShaderParameter(paramName);
		return variant.VariantType != Variant.Type.Nil ? variant.As<Vector3>() : defaultValue;
	}

	private static bool GetShaderBool(ShaderMaterial sm, string paramName, bool defaultValue)
	{
		var variant = sm.GetShaderParameter(paramName);
		return variant.VariantType != Variant.Type.Nil ? variant.As<bool>() : defaultValue;
	}

	private static MaterialParameters ExtractMaterialParameters(Material? sourceMaterial)
	{
		if (sourceMaterial is BaseMaterial3D baseMat)
		{
			return ExtractBaseMaterialParameters(baseMat);
		}
		else if (sourceMaterial is ShaderMaterial sm)
		{
			return ExtractShaderMaterialParameters(sm);
		}

		return MaterialParameters.Default;
	}

	private static void ApplyMaterialParametersToShader(ShaderMaterial material, MaterialParameters p, Texture2D? albedoTexture, bool hasPlayerMask)
	{
		if (albedoTexture != null)
		{
			material.SetShaderParameter(_paramTextureAlbedo, albedoTexture);
		}

		if (p.OrmTexture != null)
		{
			material.SetShaderParameter(_paramTextureOrm, p.OrmTexture);
			material.SetShaderParameter(_paramHasOrmTexture, true);
			material.SetShaderParameter(_paramHasPlayerMask, hasPlayerMask);
		}
		else
		{
			material.SetShaderParameter(_paramHasOrmTexture, false);
			material.SetShaderParameter(_paramHasPlayerMask, false);
		}

		if (p.NormalTexture != null)
		{
			material.SetShaderParameter(_paramTextureNormal, p.NormalTexture);
			material.SetShaderParameter(_paramHasNormalTexture, true);
		}
		else
		{
			material.SetShaderParameter(_paramHasNormalTexture, false);
		}

		if (p.EmissionTexture != null)
		{
			material.SetShaderParameter(_paramTextureEmission, p.EmissionTexture);
			material.SetShaderParameter(_paramHasEmissionTexture, true);
		}
		else
		{
			material.SetShaderParameter(_paramHasEmissionTexture, false);
		}

		material.SetShaderParameter(_paramUseAlphaBlend, p.UseAlphaBlend);
		material.SetShaderParameter(_paramUseAlphaScissor, p.UseAlphaScissor);
		material.SetShaderParameter(_paramAlphaScissorThreshold, p.AlphaScissorThreshold);

		material.SetShaderParameter(_paramAlbedoColor, p.AlbedoColor);
		material.SetShaderParameter(_paramEmissionColor, p.EmissionColor);
		material.SetShaderParameter(_paramEmissionEnergy, p.EmissionEnergy);
		material.SetShaderParameter(_paramRoughnessValue, p.Roughness);
		material.SetShaderParameter(_paramMetallicValue, p.Metallic);
		material.SetShaderParameter(_paramSpecularValue, p.Specular);
		material.SetShaderParameter(_paramUv1Scale, p.Uv1Scale);
		material.SetShaderParameter(_paramUv1Offset, p.Uv1Offset);
	}

}
