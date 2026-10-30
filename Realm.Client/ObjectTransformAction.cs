using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Services;
using Vector3 = Godot.Vector3;

namespace Realm.Client;

public class ObjectTransformAction : IEditorAction
{
	private readonly Node3D _targetNode;
	private readonly Vector3 _beforePos;
	private readonly Vector3 _afterPos;
	private readonly Vector3 _beforeRot;
	private readonly Vector3 _afterRot;
	private readonly Vector3 _beforeScale;
	private readonly Vector3 _afterScale;
	private readonly int _beforePlayer;
	private readonly int _afterPlayer;

	public ObjectTransformAction(Node3D targetNode, Vector3 beforePos, Vector3 afterPos, Vector3 beforeRot, Vector3 afterRot, Vector3 beforeScale, Vector3 afterScale, int beforePlayer = -1, int afterPlayer = -1)
	{
		_targetNode = targetNode;
		_beforePos = beforePos;
		_afterPos = afterPos;
		_beforeRot = beforeRot;
		_afterRot = afterRot;
		_beforeScale = beforeScale;
		_afterScale = afterScale;
		_beforePlayer = beforePlayer >= 0 ? beforePlayer : ((targetNode as Unit3D)?.Player ?? 0);
		_afterPlayer = afterPlayer >= 0 ? afterPlayer : ((targetNode as Unit3D)?.Player ?? 0);
	}

	public ObjectTransformAction(Node3D targetNode, Vector3 beforePos, Vector3 afterPos, Vector3 beforeRot, Vector3 afterRot, Vector3 beforeScale, Vector3 afterScale, bool beforeIsEnemy, bool afterIsEnemy, int beforePlayer = -1, int afterPlayer = -1)
		: this(targetNode, beforePos, afterPos, beforeRot, afterRot, beforeScale, afterScale, beforePlayer >= 0 ? beforePlayer : (beforeIsEnemy ? 1 : 0), afterPlayer >= 0 ? afterPlayer : (afterIsEnemy ? 1 : 0))
	{
	}

	public void Undo()
	{
		ApplyState(_beforePos, _beforeRot, _beforeScale, _beforePlayer);
	}

	public void Redo()
	{
		ApplyState(_afterPos, _afterRot, _afterScale, _afterPlayer);
	}

	private void ApplyState(Vector3 pos, Vector3 rot, Vector3 scale, int player)
	{
		if (!GodotObject.IsInstanceValid(_targetNode))
			return;

		_targetNode.Position = pos;
		_targetNode.RotationDegrees = rot;
		_targetNode.Scale = scale;

		if (_targetNode is Unit3D unit)
		{
			GameContext.Host?.SetUnitPlayerExternal(unit, player);
			UpdateEcsEntity(unit.Entity, pos, rot, scale);
		}
		else if (_targetNode is Prop3D prop)
		{
			UpdateEcsEntity(prop.Entity, pos, rot, scale);
			GameContext.PropMultiMesh?.MarkDirty(prop.PropId);
		}
		else if (_targetNode is PropEntityProxyNode proxy)
		{
			UpdateEcsEntity(proxy.Entity, pos, rot, scale);
			GameContext.PropMultiMesh?.MarkDirty(proxy.PropId);
		}
		else if (_targetNode is Decal3D decal3D)
		{
			UpdateEcsEntity(decal3D.Entity, pos, rot, scale);
		}
		else if (_targetNode is ProceduralVfxInstance3D vfxInstance)
		{
			UpdateEcsEntity(vfxInstance.Entity, pos, rot, scale);
		}

		GameContext.MapEditorHud?.UpdateSelectedObjectInfo();
	}

	private static void UpdateEcsEntity(Entity entity, Vector3 pos, Vector3 rot, Vector3 scale)
	{
		WriteEcsTransform(entity, pos, rot, scale, true);
	}

	public static void PersistTransformToEcs(Node3D node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return;

		if (node is Unit3D unit)
		{
			WriteEcsTransform(unit.Entity, unit.Position, unit.RotationDegrees, unit.Scale, true);
		}
		else if (node is Prop3D prop)
		{
			WriteEcsTransform(prop.Entity, prop.Position, prop.RotationDegrees, prop.Scale, true);
		}
		else if (node is Decal3D decal)
		{
			WriteEcsTransform(decal.Entity, decal.Position, decal.RotationDegrees, decal.Scale, false);
		}
		else if (node is PropEntityProxyNode proxy)
		{
			WriteEcsTransform(proxy.Entity, proxy.Position, proxy.RotationDegrees, proxy.Scale, true);
		}
		else if (node is ProceduralVfxInstance3D vfxInstance)
		{
			WriteEcsTransform(vfxInstance.Entity, vfxInstance.Position, vfxInstance.RotationDegrees, vfxInstance.Scale, true);
		}
	}

	private static void WriteEcsTransform(Entity entity, Vector3 pos, Vector3 rot, Vector3 scale, bool writeScale)
	{
		GameHost tempQualifier = GameContext.Host;
		var ecsWorld = (tempQualifier != null ? GameContext.World : null);
		if (ecsWorld == null || entity == Entity.Null || !ecsWorld.IsAlive(entity))
			return;

		ecsWorld.Set(entity, new Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));

		if (ecsWorld.Has<RenderPosition>(entity))
			ecsWorld.Set(entity, new RenderPosition(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));

		if (ecsWorld.Has<RotationY>(entity))
			ecsWorld.Set(entity, new RotationY(rot.Y));

		if (ecsWorld.Has<Rotation3D>(entity))
			ecsWorld.Set(entity, new Rotation3D(new System.Numerics.Vector3(rot.X, rot.Y, rot.Z)));

		GameHost.SyncPreviousTransform(ecsWorld, entity);

		if (!writeScale)
			return;

		if (ecsWorld.Has<ModelScale>(entity))
			ecsWorld.Set(entity, new ModelScale(scale.X));

		if (ecsWorld.Has<CollisionScale>(entity))
			ecsWorld.Set(entity, new CollisionScale(scale.X));
	}
}