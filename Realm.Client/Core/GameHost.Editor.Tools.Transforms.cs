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
		private List<Node3D> CollectSelectedTransformNodes()
		{
			var nodes = new List<Node3D>();
			for (int i = 0; i < _editorObjectSelection.Count; i++)
			{
				if (_editorObjectSelection[i] is Node3D node3D && GodotObject.IsInstanceValid(node3D))
				{
					nodes.Add(node3D);
				}
			}

			return nodes;
		}

		private void RecordSelectionTransformActions(List<IEditorAction> actions)
		{
			if (actions.Count == 0) return;
			EditorHistoryManager.RecordAction(actions.Count == 1 ? actions[0] : new CompositeAction(actions));
		}

		private void RefreshSelectionTransformVisuals(Node3D node3D)
		{
			if (node3D is PropEntityProxyNode proxy)
			{
				if (GameContext.World is { } proxyWorld && proxyWorld.IsAlive(proxy.Entity))
				{
					GameContext.PropMultiMesh?.UpdateInstanceTransform(proxy.Entity, proxy.Position, proxy.RotationDegrees.Y, proxy.Scale.X);
				}

				return;
			}

			if (node3D is Prop3D prop)
			{
				GameContext.PropMultiMesh?.MarkDirty(prop.PropId);
			}
		}

		private Node3D ResolveReportedTransformNode(List<Node3D> nodes)
		{
			if (_selectedEditorObject is Node3D primary && GodotObject.IsInstanceValid(primary) && nodes.Contains(primary))
			{
				return primary;
			}

			return nodes[nodes.Count - 1];
		}

		public bool ResetScaleSelectedObject()
		{
			var nodes = CollectSelectedTransformNodes();
			if (nodes.Count == 0) return false;

			var actions = new List<IEditorAction>();
			for (int i = 0; i < nodes.Count; i++)
			{
				Node3D node3D = nodes[i];
				Vector3 oldScale = node3D.Scale;
				actions.Add(new ObjectTransformAction(
					node3D,
					node3D.Position, node3D.Position,
					node3D.RotationDegrees, node3D.RotationDegrees,
					oldScale, Vector3.One
				));
				node3D.Scale = Vector3.One;
				ObjectTransformAction.PersistTransformToEcs(node3D);
				RefreshSelectionTransformVisuals(node3D);
			}

			RecordSelectionTransformActions(actions);
			return true;
		}

		public (bool success, float newScaleVal) ScaleSelectedObject(float scaleDelta)
		{
			var nodes = CollectSelectedTransformNodes();
			if (nodes.Count == 0) return (false, 1.0f);

			var actions = new List<IEditorAction>();
			for (int i = 0; i < nodes.Count; i++)
			{
				Node3D node3D = nodes[i];
				Vector3 oldScale = node3D.Scale;
				float newScaleVal = Mathf.Clamp(oldScale.X + scaleDelta, 0.2f, 4.0f);
				Vector3 newScale = Vector3.One * newScaleVal;
				actions.Add(new ObjectTransformAction(
					node3D,
					node3D.Position, node3D.Position,
					node3D.RotationDegrees, node3D.RotationDegrees,
					oldScale, newScale
				));
				node3D.Scale = newScale;
				ObjectTransformAction.PersistTransformToEcs(node3D);
				RefreshSelectionTransformVisuals(node3D);
			}

			RecordSelectionTransformActions(actions);
			return (true, ResolveReportedTransformNode(nodes).Scale.X);
		}

		public (bool success, float newRotY) RotateSelectedObject(float angleDelta)
		{
			var nodes = CollectSelectedTransformNodes();
			if (nodes.Count == 0) return (false, 0f);

			var actions = new List<IEditorAction>();
			for (int i = 0; i < nodes.Count; i++)
			{
				Node3D node3D = nodes[i];
				Vector3 oldRotation = node3D.RotationDegrees;
				Vector3 newRotation = oldRotation;
				newRotation.Y = (newRotation.Y + angleDelta + 360.0f) % 360.0f;
				actions.Add(new ObjectTransformAction(
					node3D,
					node3D.Position, node3D.Position,
					oldRotation, newRotation,
					node3D.Scale, node3D.Scale
				));
				node3D.RotationDegrees = newRotation;
				ObjectTransformAction.PersistTransformToEcs(node3D);
				RefreshSelectionTransformVisuals(node3D);
			}

			RecordSelectionTransformActions(actions);
			return (true, ResolveReportedTransformNode(nodes).RotationDegrees.Y);
		}

		public (bool success, string error) DeleteSelectedObject()
		{
			var (success, _) = DeleteSelectedObjects();
			return (success, "");
		}

		public (bool success, int deletedCount) DeleteSelectedObjects()
		{
			if (_editorObjectSelection.Count == 0) return (false, 0);

			var targets = new List<Node>(_editorObjectSelection);
			var actions = new List<IEditorAction>();
			ClearEditorObjectSelection();

			for (int i = 0; i < targets.Count; i++)
			{
				Node target = targets[i];
				if (target == null || !GodotObject.IsInstanceValid(target)) continue;

				Vector3 position = target is Node3D node3D ? node3D.Position : Vector3.Zero;
				var action = DeleteObjectAtWithUndo(target, position);
				if (action != null)
				{
					actions.Add(action);
				}
				else
				{
					DeleteNodeExternal(target);
				}
			}

			if (actions.Count == 0) return (false, 0);

			EditorHistoryManager.RecordAction(actions.Count == 1 ? actions[0] : new CompositeAction(actions));
			EditorHasUnsavedChanges = true;
			return (true, actions.Count);
		}

		public void DeleteNodeExternal(Node node)
		{
			if (node == null || !GodotObject.IsInstanceValid(node))
			{
				GameContext.MapEditorHud?.ShowFeedbackExternal("[Debug] DeleteNodeExternal: node is NULL or invalid");
				return;
			}

			if (DeleteUnitExternal(node)) return;
			if (DeletePropExternal(node)) return;
			if (DeleteDecalExternal(node)) return;
			if (DeleteVfxExternal(node)) return;

			if (_selectedEditorObject == node)
			{
				SelectedEditorObject = null;
			}

			node.QueueFree();
		}

		private bool DeleteUnitExternal(Node node)
		{
			var unit = (node as Unit3D) ?? FindUnit3DInParentChain(node);
			if (unit != null && GodotObject.IsInstanceValid(unit))
			{
				if (unit == _selectedEditorObject || FindUnit3DInParentChain(_selectedEditorObject) == unit)
				{
					SelectedEditorObject = null;
				}

				SelectedUnits.Remove(unit);
				AllUnits.Remove(unit);
				EntityToUnit3D.Remove(unit.Entity);
				EntityIdToUnit3D.Remove(unit.Entity.Id);
				DestroyMapEntity(unit.Entity);

				unit.QueueFree();
				return true;
			}

			return false;
		}

		private bool DeletePropExternal(Node node)
		{
			var proxy = node as PropEntityProxyNode;
			if (proxy != null && GodotObject.IsInstanceValid(proxy))
			{
				if (proxy == _selectedEditorObject) SelectedEditorObject = null;
				string propId = proxy.PropId;
				DestroyMapEntity(proxy.Entity);

				proxy.QueueFree();
				GameContext.PropMultiMesh?.MarkDirty(propId);
				return true;
			}

			var prop = (node as Prop3D) ?? FindProp3DInParentChain(node);
			if (prop != null && GodotObject.IsInstanceValid(prop))
			{
				if (prop == _selectedEditorObject || FindProp3DInParentChain(_selectedEditorObject) == prop)
				{
					SelectedEditorObject = null;
				}

				string propId = prop.PropId;
				AllProps.Remove(prop);
				EntityToProp3D.Remove(prop.Entity);
				DestroyMapEntity(prop.Entity);

				prop.QueueFree();
				GameContext.PropMultiMesh?.MarkDirty(propId);
				return true;
			}

			return false;
		}

		private bool DeleteDecalExternal(Node node)
		{
			var decal = (node as Decal) ?? FindDecalInParentChain(node);
			if (decal != null && GodotObject.IsInstanceValid(decal))
			{
				if (decal == _selectedEditorObject || FindDecalInParentChain(_selectedEditorObject) == decal)
				{
					SelectedEditorObject = null;
				}

				AllDecals.Remove(decal);
				if (decal is Decal3D decal3D)
				{
					DestroyMapEntity(decal3D.Entity);
				}

				decal.QueueFree();
				return true;
			}

			return false;
		}

		private bool DeleteVfxExternal(Node node)
		{
			var vfx = (node as ProceduralVfxInstance3D) ?? FindVfxInParentChain(node);
			if (vfx != null && GodotObject.IsInstanceValid(vfx))
			{
				if (vfx == _selectedEditorObject || FindVfxInParentChain(_selectedEditorObject) == vfx)
				{
					SelectedEditorObject = null;
				}

				AllVfx.Remove(vfx);
				EntityToVfx3D.Remove(vfx.Entity);
				DestroyMapVfxEntity(vfx);

				vfx.QueueFree();
				return true;
			}

			return false;
		}

		private static void DestroyMapEntity(Entity entity)
		{
			if (entity.IsNull || entity == GameContext.Simulation.WorldEntity) return;
			if (GameContext.World is { } destroyWorld && destroyWorld.IsAlive(entity))
			{
				destroyWorld.Destroy(entity);
			}
		}

		private static void DestroyMapVfxEntity(ProceduralVfxInstance3D vfx)
		{
			if (vfx.IsPreview) return;
			DestroyMapEntity(vfx.Entity);
		}

		public IEditorAction? DeleteObjectAtWithUndo(Node? collider, Vector3 hitPos)
		{
			if (collider == null || !GodotObject.IsInstanceValid(collider))
			{
				GameContext.MapEditorHud?.ShowFeedbackExternal("[Debug] DeleteObjectAtWithUndo: collider is NULL or invalid");
				return null;
			}

			var directAction = DeleteByDirectParentChain(collider);
			if (directAction != null) return directAction;

			var proxAction = DeleteByProximitySearch(hitPos);
			if (proxAction != null) return proxAction;

			GameContext.MapEditorHud?.ShowFeedbackExternal("[Debug] DeleteObjectAtWithUndo returned NULL (no direct or proximity match)");
			return null;
		}

		private IEditorAction? DeleteByDirectParentChain(Node collider)
		{
			var unitAction = TryDeleteUnitFromParent(collider);
			if (unitAction != null) return unitAction;

			var propAction = TryDeletePropFromParent(collider);
			if (propAction != null) return propAction;

			var decalAction = TryDeleteDecalFromParent(collider);
			if (decalAction != null) return decalAction;

			var vfxAction = TryDeleteVfxFromParent(collider);
			if (vfxAction != null) return vfxAction;

			return null;
		}

		private IEditorAction? TryDeleteUnitFromParent(Node collider)
		{
			var unit = (collider as Unit3D) ?? FindUnit3DInParentChain(collider);
			if (unit != null && GodotObject.IsInstanceValid(unit))
			{
				if (unit == _selectedEditorObject || FindUnit3DInParentChain(_selectedEditorObject) == unit) SelectedEditorObject = null;
				var action = new ObjectDeleteAction("unit", unit.UnitId, unit.Position, unit.RotationDegrees.Y, unit.Scale.X, unit, unit.Player);
				SelectedUnits.Remove(unit);
				AllUnits.Remove(unit);
				EntityToUnit3D.Remove(unit.Entity);
				EntityIdToUnit3D.Remove(unit.Entity.Id);
				DestroyMapEntity(unit.Entity);
				unit.QueueFree();
				return action;
			}

			return null;
		}

		private IEditorAction? TryDeletePropFromParent(Node collider)
		{
			var proxy = collider as PropEntityProxyNode;
			if (proxy != null && GodotObject.IsInstanceValid(proxy))
			{
				if (proxy == _selectedEditorObject) SelectedEditorObject = null;
				string propId = proxy.PropId;
				var action = new ObjectDeleteAction("prop", propId, proxy.Position, proxy.RotationDegrees.Y, proxy.Scale.X, false, null);
				DestroyMapEntity(proxy.Entity);
				proxy.QueueFree();
				GameContext.PropMultiMesh?.MarkDirty(propId);
				return action;
			}

			var prop = (collider as Prop3D) ?? FindProp3DInParentChain(collider);
			if (prop != null && GodotObject.IsInstanceValid(prop))
			{
				if (prop == _selectedEditorObject || FindProp3DInParentChain(_selectedEditorObject) == prop) SelectedEditorObject = null;
				string propId = prop.PropId;
				var action = new ObjectDeleteAction("prop", propId, prop.Position, prop.RotationDegrees.Y, prop.Scale.X, false, prop);
				AllProps.Remove(prop);
				EntityToProp3D.Remove(prop.Entity);
				DestroyMapEntity(prop.Entity);
				prop.QueueFree();
				GameContext.PropMultiMesh?.MarkDirty(propId);
				return action;
			}

			return null;
		}

		private IEditorAction? TryDeleteDecalFromParent(Node collider)
		{
			var decal = (collider as Decal) ?? FindDecalInParentChain(collider);
			if (decal != null && GodotObject.IsInstanceValid(decal))
			{
				if (decal == _selectedEditorObject || FindDecalInParentChain(_selectedEditorObject) == decal) SelectedEditorObject = null;
				string decalId = decal is Decal3D d3d ? d3d.DecalId : "logo";
				var action = new ObjectDeleteAction("decal", decalId, decal.Position, decal.RotationDegrees.Y, decal.Scale.X, false, decal);
				AllDecals.Remove(decal);
				if (decal is Decal3D d3) DestroyMapEntity(d3.Entity);
				decal.QueueFree();
				return action;
			}

			return null;
		}

		private IEditorAction? TryDeleteVfxFromParent(Node collider)
		{
			var vfx = (collider as ProceduralVfxInstance3D) ?? FindVfxInParentChain(collider);
			if (vfx != null && GodotObject.IsInstanceValid(vfx))
			{
				if (vfx == _selectedEditorObject || FindVfxInParentChain(_selectedEditorObject) == vfx) SelectedEditorObject = null;
				string vfxId = vfx.Config?.VfxId ?? "vfx";
				var action = new ObjectDeleteAction("vfx", vfxId, vfx.Position, vfx.RotationDegrees.Y, vfx.Scale.X, false, vfx);
				AllVfx.Remove(vfx);
				EntityToVfx3D.Remove(vfx.Entity);
				DestroyMapVfxEntity(vfx);
				vfx.QueueFree();
				return action;
			}

			return null;
		}

		private IEditorAction? DeleteByProximitySearch(Vector3 hitPos)
		{
			var (closestUnit, closestUnitDist) = FindClosestUnitDistance(hitPos);
			var (closestProp, closestPropDist) = FindClosestPropDistance(hitPos);
			var (closestStaticPropEntity, closestStaticPropId, closestStaticPropPos, closestStaticPropRotY, closestStaticPropScale, closestStaticPropDist) = FindClosestStaticPropDistance(hitPos);
			var (closestDecal, closestDecalDist) = FindClosestDecalDistance(hitPos);
			var (closestVfx, closestVfxDist) = FindClosestVfxDistance(hitPos);

			float minDistance = Mathf.Min(closestUnitDist, Mathf.Min(closestPropDist, Mathf.Min(closestStaticPropDist, Mathf.Min(closestDecalDist, closestVfxDist))));
			if (minDistance >= 2.0f) return null;

			var dynAction = TryExecuteDynamicProximityDelete(minDistance, closestUnit, closestUnitDist, closestProp, closestPropDist, closestDecal, closestDecalDist, closestVfx, closestVfxDist);
			if (dynAction != null) return dynAction;

			if (closestStaticPropEntity != Entity.Null && minDistance == closestStaticPropDist)
				return ExecuteStaticPropProximityDelete(closestStaticPropEntity, closestStaticPropId!, closestStaticPropPos, closestStaticPropRotY, closestStaticPropScale);

			return null;
		}

		private IEditorAction? TryExecuteDynamicProximityDelete(float minDistance, Unit3D? closestUnit, float closestUnitDist, Prop3D? closestProp, float closestPropDist, Decal? closestDecal, float closestDecalDist, ProceduralVfxInstance3D? closestVfx, float closestVfxDist)
		{
			if (closestUnit != null && minDistance == closestUnitDist)
				return ExecuteUnitProximityDelete(closestUnit);
			if (closestProp != null && minDistance == closestPropDist)
				return ExecutePropProximityDelete(closestProp);
			if (closestDecal != null && minDistance == closestDecalDist)
				return ExecuteDecalProximityDelete(closestDecal);
			if (closestVfx != null && minDistance == closestVfxDist)
				return ExecuteVfxProximityDelete(closestVfx);
			return null;
		}

		private (Unit3D?, float) FindClosestUnitDistance(Vector3 hitPos)
		{
			Unit3D? closestUnit = null;
			float closestDist = 2.0f;
			foreach (var u in AllUnits)
			{
				if (GodotObject.IsInstanceValid(u))
				{
					float d = u.Position.DistanceTo(hitPos);
					if (d < closestDist)
					{
						closestDist = d;
						closestUnit = u;
					}
				}
			}

			return (closestUnit, closestDist);
		}

		private (Prop3D?, float) FindClosestPropDistance(Vector3 hitPos)
		{
			Prop3D? closestProp = null;
			float closestDist = 2.0f;
			foreach (var p in AllProps)
			{
				if (GodotObject.IsInstanceValid(p))
				{
					float d = p.Position.DistanceTo(hitPos);
					if (d < closestDist)
					{
						closestDist = d;
						closestProp = p;
					}
				}
			}

			return (closestProp, closestDist);
		}

		private (Entity, string?, Vector3, float, float, float) FindClosestStaticPropDistance(Vector3 hitPos)
		{
			Entity closestEntity = Entity.Null;
			string? closestId = null;
			Vector3 closestPos = Vector3.Zero;
			float closestRotY = 0f;
			float closestScale = 1f;
			float closestDist = 2.0f;

			if (GameContext.World is { } staticPropWorld)
			{
				var propQuery = QueryCache.AllPropIdentityAndPositionQuery;
				staticPropWorld.Query(in propQuery, (Entity entity, ref PropIdentity pId, ref Position pPos) =>
				{
					if (EntityToProp3D.ContainsKey(entity)) return;
					Vector3 wPos = new Vector3(pPos.Value.X, pPos.Value.Y, pPos.Value.Z);
					float d = wPos.DistanceTo(hitPos);
					if (d < closestDist)
					{
						closestDist = d;
						closestEntity = entity;
						closestId = pId.PropId;
						closestPos = wPos;
						closestRotY = staticPropWorld.Has<RotationY>(entity) ? staticPropWorld.Get<RotationY>(entity).Value : 0f;
						closestScale = staticPropWorld.Has<ModelScale>(entity) ? staticPropWorld.Get<ModelScale>(entity).Value : 1f;
					}
				});
			}

			return (closestEntity, closestId, closestPos, closestRotY, closestScale, closestDist);
		}

		private (Decal?, float) FindClosestDecalDistance(Vector3 hitPos)
		{
			Decal? closestDecal = null;
			float closestDist = 2.0f;
			foreach (var decalObj in AllDecals)
			{
				if (GodotObject.IsInstanceValid(decalObj))
				{
					float d = decalObj.GlobalPosition.DistanceTo(hitPos);
					if (d < closestDist)
					{
						closestDist = d;
						closestDecal = decalObj;
					}
				}
			}

			return (closestDecal, closestDist);
		}

		private (ProceduralVfxInstance3D?, float) FindClosestVfxDistance(Vector3 hitPos)
		{
			ProceduralVfxInstance3D? closestVfx = null;
			float closestDist = 2.0f;
			foreach (var vfxObj in AllVfx)
			{
				if (GodotObject.IsInstanceValid(vfxObj))
				{
					float d = vfxObj.GlobalPosition.DistanceTo(hitPos);
					if (d < closestDist)
					{
						closestDist = d;
						closestVfx = vfxObj;
					}
				}
			}

			return (closestVfx, closestDist);
		}

		private IEditorAction ExecuteUnitProximityDelete(Unit3D closestUnit)
		{
			if (closestUnit == _selectedEditorObject) SelectedEditorObject = null;
			var action = new ObjectDeleteAction("unit", closestUnit.UnitId, closestUnit.Position, closestUnit.RotationDegrees.Y, closestUnit.Scale.X, closestUnit, closestUnit.Player);
			SelectedUnits.Remove(closestUnit);
			AllUnits.Remove(closestUnit);
			EntityToUnit3D.Remove(closestUnit.Entity);
			EntityIdToUnit3D.Remove(closestUnit.Entity.Id);
			if (World.IsAlive(closestUnit.Entity)) World.Destroy(closestUnit.Entity);
			closestUnit.QueueFree();
			return action;
		}

		private IEditorAction ExecutePropProximityDelete(Prop3D closestProp)
		{
			if (closestProp == _selectedEditorObject) SelectedEditorObject = null;
			string propId = closestProp.PropId;
			var action = new ObjectDeleteAction("prop", propId, closestProp.Position, closestProp.RotationDegrees.Y, closestProp.Scale.X, false, closestProp);
			AllProps.Remove(closestProp);
			EntityToProp3D.Remove(closestProp.Entity);
			if (World.IsAlive(closestProp.Entity)) World.Destroy(closestProp.Entity);
			closestProp.QueueFree();
			GameContext.PropMultiMesh?.MarkDirty(propId);
			return action;
		}

		private IEditorAction ExecuteStaticPropProximityDelete(Entity closestStaticPropEntity, string closestStaticPropId, Vector3 closestStaticPropPos, float closestStaticPropRotY, float closestStaticPropScale)
		{
			var action = new ObjectDeleteAction("prop", closestStaticPropId, closestStaticPropPos, closestStaticPropRotY, closestStaticPropScale, false, null);
			World.Destroy(closestStaticPropEntity);
			GameContext.PropMultiMesh?.MarkDirty(closestStaticPropId);
			return action;
		}

		private IEditorAction ExecuteDecalProximityDelete(Decal closestDecal)
		{
			if (closestDecal == _selectedEditorObject) SelectedEditorObject = null;
			string decalId = closestDecal is Decal3D d3d ? d3d.DecalId : "logo";
			var action = new ObjectDeleteAction("decal", decalId, closestDecal.Position, closestDecal.RotationDegrees.Y, closestDecal.Scale.X, false, closestDecal);
			AllDecals.Remove(closestDecal);
			if (closestDecal is Decal3D d3 && World.IsAlive(d3.Entity)) World.Destroy(d3.Entity);
			closestDecal.QueueFree();
			return action;
		}

		private IEditorAction ExecuteVfxProximityDelete(ProceduralVfxInstance3D closestVfx)
		{
			if (closestVfx == _selectedEditorObject) SelectedEditorObject = null;
			string vfxId = closestVfx.Config?.VfxId ?? "vfx";
			var action = new ObjectDeleteAction("vfx", vfxId, closestVfx.Position, closestVfx.RotationDegrees.Y, closestVfx.Scale.X, false, closestVfx);
			AllVfx.Remove(closestVfx);
			EntityToVfx3D.Remove(closestVfx.Entity);
			DestroyMapVfxEntity(closestVfx);
			closestVfx.QueueFree();
			return action;
		}

	}
}
