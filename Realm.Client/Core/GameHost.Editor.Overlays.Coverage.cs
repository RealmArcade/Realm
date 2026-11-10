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
		public void UpdateEditorCoverageOverlay()
		{
			Node3D coverageRoot = EnsureCoverageOverlayRootExists();
			ClearCoverageOverlayChildren();

			if (!EditorCoverageOverlayEnabled || !TryGetSelectedUnit(out var unit))
			{
				coverageRoot.Visible = false;
				return;
			}

			EcsWorld world = World;
			float scanRadius = world.Has<ScanRadius>(unit.Entity) ? world.Get<ScanRadius>(unit.Entity).Value : 0f;
			float range = world.Has<Attack>(unit.Entity) ? world.Get<Attack>(unit.Entity).Range : 0f;

			coverageRoot.Visible = scanRadius > 0f || range > 0f;
			if (!coverageRoot.Visible) return;

			coverageRoot.Position = unit.Position;
			if (scanRadius > 0f) CreateCoverageRing(scanRadius, new Color(0.3f, 0.7f, 1.0f, 0.6f));
			if (range > 0f) CreateCoverageRing(range, new Color(1.0f, 0.5f, 0.1f, 0.7f));
		}

		private Node3D EnsureCoverageOverlayRootExists()
		{
			if (_editorCoverageOverlayRoot != null) return _editorCoverageOverlayRoot;

			_editorCoverageOverlayRoot = new Node3D();
			_editorCoverageOverlayRoot.Name = "EditorCoverageOverlay";
			AddChild(_editorCoverageOverlayRoot);
			return _editorCoverageOverlayRoot;
		}

		private void ClearCoverageOverlayChildren()
		{
			if (_editorCoverageOverlayRoot == null) return;

			foreach (Node child in _editorCoverageOverlayRoot.GetChildren())
			{
				child.QueueFree();
			}
		}

		private bool TryGetSelectedUnit([NotNullWhen(true)] out Unit3D? unit)
		{
			unit = _selectedEditorObject as Unit3D;
			return unit != null && GameContext.World is { } world && world.IsAlive(unit.Entity);
		}

		private void CreateCoverageRing(float radius, Color color)
		{
			var meshInstance = new MeshInstance3D();
			meshInstance.Name = "MeshInstance_MeshInstance3D";
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

			EnsureCoverageOverlayRootExists().AddChild(meshInstance);
		}

	}
}
