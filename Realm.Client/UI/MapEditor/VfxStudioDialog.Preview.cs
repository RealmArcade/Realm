using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Client.VFX;

namespace Realm.Client.UI.MapEditor;

public partial class VfxStudioDialog
{
	private void CreatePreviewEnvironment()
	{
		_previewGroundGrid = new MeshInstance3D();
		_previewGroundGrid.Name = "GroundGrid";
		var planeMesh = new PlaneMesh { Size = new Vector2(10f, 10f), SubdivideWidth = 10, SubdivideDepth = 10 };
		_previewGroundGrid.Mesh = planeMesh;
		var gridMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.12f, 0.14f, 0.18f, 0.85f),
			Roughness = 0.8f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};
		_previewGroundGrid.MaterialOverride = gridMat;
		_previewGroundGrid.Position = new Vector3(0, -0.01f, 0);
		PreviewSceneRoot.AddChild(_previewGroundGrid);

		_previewVfxInstance = new ProceduralVfxInstance3D();
		_previewVfxInstance.IsPreview = true;
		_previewVfxInstance.Name = "PreviewVfx";
		PreviewSceneRoot.AddChild(_previewVfxInstance);
		_previewVfxInstance.Initialize(_currentConfig);
	}

	private enum VfxPreviewKind
	{
		VisualEffect,
		Shockwave,
		GroundRing,
		BurstSphere,
		ChainBeam,
		BarrageVolley
	}

	private const float ShockwavePreviewRadius = 6.0f;
	private const float ShockwavePreviewSpeed = 10.0f;
	private const float ShockwavePreviewDuration = 0.9f;
	private const float RingPreviewRadius = 5.0f;
	private const float RingPreviewSpeed = 8.0f;
	private const float RingPreviewDuration = 0.9f;
	private const float BurstPreviewRadius = 4.0f;
	private const float BurstPreviewSpeed = 10.0f;
	private const float BurstPreviewDuration = 0.7f;
	private const float ChainPreviewJumpDelay = 0.08f;
	private const int ChainPreviewForkCount = 2;
	private const float ChainPreviewFadeLifetime = 0.45f;
	private const float ChainPreviewWidth = 0.35f;
	private const float BarragePreviewRadius = 3.5f;
	private const int BarragePreviewCount = 8;
	private const float BarragePreviewInterval = 0.12f;

	private static readonly Vector3[] ChainPreviewPoints =
	{
		new Vector3(-2.5f, 0.5f, -1.5f),
		new Vector3(-0.5f, 1.2f, 0.5f),
		new Vector3(1.5f, 0.6f, -0.5f),
		new Vector3(2.5f, 1.0f, 1.5f)
	};

	private VfxPreviewKind _lastPreviewKind = VfxPreviewKind.VisualEffect;

	private void TriggerPreviewShockwave()
	{
		if (PreviewSceneRoot == null || !GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
		_lastPreviewKind = VfxPreviewKind.Shockwave;
		Color baseCol = VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange);
		Color col = new Color(baseCol.R, baseCol.G, baseCol.B, 0.9f);
		var shock = GroundShockwave3D.Create(PreviewSceneRoot, new Vector3(0, 0, -2.5f), ShockwaveType.PlanarWave, maxRadius: ShockwavePreviewRadius, speed: ShockwavePreviewSpeed, duration: ShockwavePreviewDuration, col);
		shock.ConformToTerrain = false;
		shock.Direction = Vector3.Back;
	}

	private void TriggerPreviewRing()
	{
		if (PreviewSceneRoot == null || !GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
		_lastPreviewKind = VfxPreviewKind.GroundRing;
		Color baseCol = VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange);
		Color col = new Color(baseCol.R, baseCol.G, baseCol.B, 0.9f);
		var ring = GroundShockwave3D.Create(PreviewSceneRoot, Vector3.Zero, ShockwaveType.ExpandingGroundRing, maxRadius: RingPreviewRadius, speed: RingPreviewSpeed, duration: RingPreviewDuration, col);
		ring.ConformToTerrain = false;
	}

	private void TriggerPreviewBurst()
	{
		if (PreviewSceneRoot == null || !GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
		_lastPreviewKind = VfxPreviewKind.BurstSphere;
		Color baseCol = VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange);
		Color col = new Color(baseCol.R, baseCol.G, baseCol.B, 0.8f);
		var burst = GroundShockwave3D.Create(PreviewSceneRoot, new Vector3(0, 0.8f, 0), ShockwaveType.ExpandingBurstSphere, maxRadius: BurstPreviewRadius, speed: BurstPreviewSpeed, duration: BurstPreviewDuration, col);
		burst.ConformToTerrain = false;
	}

	private void TriggerPreviewChain()
	{
		if (PreviewSceneRoot == null || !GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
		_lastPreviewKind = VfxPreviewKind.ChainBeam;
		Color baseCol = VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange);
		Color col = new Color(baseCol.R, baseCol.G, baseCol.B, 1.0f);
		ChainBeam3D.Create(PreviewSceneRoot, ChainPreviewPoints, jumpDelay: ChainPreviewJumpDelay, forkCount: ChainPreviewForkCount, fadeLifetime: ChainPreviewFadeLifetime, width: ChainPreviewWidth, col);
	}

	private void TriggerPreviewBarrage()
	{
		if (PreviewSceneRoot == null || !GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
		_lastPreviewKind = VfxPreviewKind.BarrageVolley;
		var rng = new Random();
		int count = BarragePreviewCount;
		float interval = BarragePreviewInterval;
		for (int i = 0; i < count; i++)
		{
			float delay = i * interval;
			var timer = GetTree().CreateTimer(delay);
			timer.Timeout += () =>
			{
				if (!GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
				float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
				float dist = (float)(rng.NextDouble() * BarragePreviewRadius);
				var impactPos = new Vector3(Mathf.Cos(angle) * dist, 0.05f, Mathf.Sin(angle) * dist);
				var subVfx = new ProceduralVfxInstance3D();
				subVfx.Name = "BarrageVfx_ProceduralVfxInstance3D";
				subVfx.IsPreview = true;
				PreviewSceneRoot.AddChild(subVfx);
				subVfx.Position = impactPos;
				subVfx.Initialize(_currentConfig);
				var cleanupTimer = GetTree().CreateTimer(1.2f);
				cleanupTimer.Timeout += () =>
				{
					if (GodotObject.IsInstanceValid(subVfx)) subVfx.QueueFree();
				};
			};
		}
	}

}
