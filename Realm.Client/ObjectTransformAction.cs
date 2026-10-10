using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
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
	private readonly bool _beforeIsEnemy;
	private readonly bool _afterIsEnemy;
	private readonly int _beforePlayer;
	private readonly int _afterPlayer;

	public ObjectTransformAction(Node3D targetNode, Vector3 beforePos, Vector3 afterPos, Vector3 beforeRot, Vector3 afterRot, Vector3 beforeScale, Vector3 afterScale, bool beforeIsEnemy, bool afterIsEnemy)
		: this(targetNode, beforePos, afterPos, beforeRot, afterRot, beforeScale, afterScale, beforeIsEnemy, afterIsEnemy, (targetNode as Realm.Client.Unit3D)?.Player ?? (beforeIsEnemy ? 1 : 0), (targetNode as Realm.Client.Unit3D)?.Player ?? (afterIsEnemy ? 1 : 0))
	{
	}

	public ObjectTransformAction(Node3D targetNode, Vector3 beforePos, Vector3 afterPos, Vector3 beforeRot, Vector3 afterRot, Vector3 beforeScale, Vector3 afterScale, bool beforeIsEnemy, bool afterIsEnemy, int beforePlayer, int afterPlayer)
	{
		_targetNode = targetNode;
		_beforePos = beforePos;
		_afterPos = afterPos;
		_beforeRot = beforeRot;
		_afterRot = afterRot;
		_beforeScale = beforeScale;
		_afterScale = afterScale;
		_beforeIsEnemy = beforeIsEnemy;
		_afterIsEnemy = afterIsEnemy;
		_beforePlayer = beforePlayer;
		_afterPlayer = afterPlayer;
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

		if (_targetNode is Realm.Client.Unit3D unit)
		{
			Realm.Client.Core.GameHost.Instance?.SetUnitPlayerExternal(unit, player);
			UpdateEcsEntity(unit.Entity, pos, rot, scale);
		}
		else if (_targetNode is Prop3D prop)
		{
			UpdateEcsEntity(prop.Entity, pos, rot, scale);
			PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
		}
		else if (_targetNode is Decal3D decal3D)
		{
			UpdateEcsEntity(decal3D.Entity, pos, rot, scale);
		}

		UI.MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
	}

	private static void UpdateEcsEntity(Arch.Core.Entity entity, Vector3 pos, Vector3 rot, Vector3 scale)
	{
		var ecsWorld = Realm.Client.Core.GameHost.Instance?.EcsWorld;
		if (ecsWorld == null || entity == Arch.Core.Entity.Null || !ecsWorld.IsAlive(entity))
			return;

		ecsWorld.Set(entity, new Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));

		if (ecsWorld.Has<RotationY>(entity))
			ecsWorld.Set(entity, new RotationY(rot.Y));

		if (ecsWorld.Has<ModelScale>(entity))
			ecsWorld.Set(entity, new ModelScale(scale.X));
	}
}