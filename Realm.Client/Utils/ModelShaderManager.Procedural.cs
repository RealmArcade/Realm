using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Realm.Client.Utils;

public static partial class ModelShaderManager
{
	public static void SetProceduralAnimation(Node rootNode, ProceduralAnimationConfig? config, Vector3 velocity = default, float impulseStrength = 0f, float impulseTime = 0f, bool showDebugMask = false, string? modelKey = null)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		if (config == null)
		{
			DisableProceduralAnimation(rootNode);
			return;
		}

		ProceduralShaderParameters parameters = ProceduralAnimationManager.BuildShaderParameters(config);
		Vector4 p1 = parameters.Parameters1;
		Vector4 p2 = parameters.Parameters2;
		Vector4 p3 = parameters.Parameters3;
		Vector4 pImp = new Vector4(impulseStrength, config.ImpulseFrequency, impulseTime, showDebugMask ? 1.0f : 0.0f);
		Vector3 regionMin = new Vector3(config.MaskRegionMin.X, config.MaskRegionMin.Y, config.MaskRegionMin.Z);
		Vector3 regionMax = new Vector3(config.MaskRegionMax.X, config.MaskRegionMax.Y, config.MaskRegionMax.Z);

		ProceduralSwayRegionBinder? regionBinder = rootNode is Node3D modelRoot3D
			? new ProceduralSwayRegionBinder(modelRoot3D, config, modelKey)
			: null;

		SetProceduralAnimationRecursive(rootNode, p1, p2, p3, pImp, velocity, regionMin, regionMax, regionBinder);
	}

	private static void SetProceduralAnimationRecursive(Node node, Vector4 p1, Vector4 p2, Vector4 p3, Vector4 pImp, Vector3 velocity, Vector3 regionMin, Vector3 regionMax, ProceduralSwayRegionBinder? regionBinder)
	{
		if (node is GeometryInstance3D geom && !IsExcludedMesh(geom))
		{
			Vector4 meshParams1 = p1;
			Vector4 meshParams2 = p2;
			Vector3 meshRegionMin = regionMin;
			Vector3 meshRegionMax = regionMax;

			if (regionBinder != null)
			{
				ProceduralSwayMeshMask meshMask = regionBinder.BuildMeshMask(geom);
				meshParams1 = new Vector4(p1.X, (float)meshMask.MaskMode, meshMask.MaskMin, meshMask.MaskMax);
				meshParams2 = new Vector4(meshMask.SignedMaskPower, p2.Y, p2.Z, p2.W);
				meshRegionMin = meshMask.RegionMin;
				meshRegionMax = meshMask.RegionMax;
			}

			geom.SetInstanceShaderParameter(_paramProcAnimParams1, meshParams1);
			geom.SetInstanceShaderParameter(_paramProcAnimParams2, meshParams2);
			geom.SetInstanceShaderParameter(_paramProcAnimParams3, p3);
			geom.SetInstanceShaderParameter(_paramProcAnimImpulse, pImp);
			geom.SetInstanceShaderParameter(_paramProcAnimVelocity, velocity);
			geom.SetInstanceShaderParameter(_paramProcAnimRegionMin, meshRegionMin);
			geom.SetInstanceShaderParameter(_paramProcAnimRegionMax, meshRegionMax);
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			SetProceduralAnimationRecursive(node.GetChild(i), p1, p2, p3, pImp, velocity, regionMin, regionMax, regionBinder);
		}
	}

	public static void SetProceduralAnimationImpulse(IReadOnlyList<GeometryInstance3D> geometryInstances, float impulseStrength, float impulseFrequency, float impulseTime, bool showDebugMask = false)
	{
		if (geometryInstances == null) return;
		Vector4 pImp = new Vector4(impulseStrength, impulseFrequency, impulseTime, showDebugMask ? 1.0f : 0.0f);
		int count = geometryInstances.Count;
		for (int i = 0; i < count; i++)
		{
			var geom = geometryInstances[i];
			if (GodotObject.IsInstanceValid(geom))
			{
				geom.SetInstanceShaderParameter(_paramProcAnimImpulse, pImp);
			}
		}
	}

	public static void SetProceduralAnimationImpulse(Node rootNode, float impulseStrength, float impulseFrequency, float impulseTime, bool showDebugMask = false)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		Vector4 pImp = new Vector4(impulseStrength, impulseFrequency, impulseTime, showDebugMask ? 1.0f : 0.0f);
		SetProceduralAnimationImpulseRecursive(rootNode, pImp);
	}

	private static void SetProceduralAnimationImpulseRecursive(Node node, Vector4 pImp)
	{
		if (node is GeometryInstance3D geom && !IsExcludedMesh(geom))
		{
			geom.SetInstanceShaderParameter(_paramProcAnimImpulse, pImp);
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			SetProceduralAnimationImpulseRecursive(node.GetChild(i), pImp);
		}
	}

	public static void SetProceduralAnimationVelocity(IReadOnlyList<GeometryInstance3D> geometryInstances, Vector3 velocity)
	{
		if (geometryInstances == null) return;
		int count = geometryInstances.Count;
		for (int i = 0; i < count; i++)
		{
			var geom = geometryInstances[i];
			if (GodotObject.IsInstanceValid(geom))
			{
				geom.SetInstanceShaderParameter(_paramProcAnimVelocity, velocity);
			}
		}
	}

	public static void SetProceduralAnimationVelocity(Node rootNode, Vector3 velocity)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetProceduralAnimationVelocityRecursive(rootNode, velocity);
	}

	private static void SetProceduralAnimationVelocityRecursive(Node node, Vector3 velocity)
	{
		if (node is GeometryInstance3D geom && !IsExcludedMesh(geom))
		{
			geom.SetInstanceShaderParameter(_paramProcAnimVelocity, velocity);
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			SetProceduralAnimationVelocityRecursive(node.GetChild(i), velocity);
		}
	}

	public static void DisableProceduralAnimation(Node rootNode)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		Vector4 pZero = Vector4.Zero;
		Vector3 vZero = Vector3.Zero;
		SetProceduralAnimationRecursive(rootNode, pZero, pZero, pZero, pZero, vZero, vZero, vZero, null);
	}

	public static void ClearCache()
	{
		_materialCache.Clear();
		_normalizedAlbedoCache.Clear();
		_playerMaskCheckCache.Clear();
		_sharedShader = null;
	}
}
