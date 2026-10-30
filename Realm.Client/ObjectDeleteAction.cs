using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Vector3 = Godot.Vector3;

namespace Realm.Client;

public class ObjectDeleteAction : IEditorAction
{
	private readonly string _objectType;
	private readonly string _objectId;
	private readonly Vector3 _position;
	private readonly Vector3 _rotationDegrees;
	private readonly float _rotationY;
	private readonly float _scale;
	private readonly int _player;
	private Node _spawnedNode;

	public ObjectDeleteAction(string objectType, string objectId, Vector3 position, float rotationY, float scale, Node deletedNode, int player = -1)
		: this(objectType, objectId, position, (deletedNode as Node3D)?.RotationDegrees ?? new Vector3(0f, rotationY, 0f), scale, deletedNode, player)
	{
	}

	public ObjectDeleteAction(string objectType, string objectId, Vector3 position, float rotationY, float scale, bool isEnemy, Node deletedNode, int player = -1)
		: this(objectType, objectId, position, (deletedNode as Node3D)?.RotationDegrees ?? new Vector3(0f, rotationY, 0f), scale, deletedNode, player >= 0 ? player : (isEnemy ? 1 : 0))
	{
	}

	public ObjectDeleteAction(string objectType, string objectId, Vector3 position, Vector3 rotationDegrees, float scale, Node deletedNode, int player = -1)
	{
		_objectType = objectType;
		_objectId = objectId;
		_position = position;
		_rotationDegrees = rotationDegrees;
		_rotationY = rotationDegrees.Y;
		_scale = scale;
		_player = player >= 0 ? player : ((deletedNode as Unit3D)?.Player ?? 0);
		_spawnedNode = deletedNode;
	}

	public ObjectDeleteAction(string objectType, string objectId, Vector3 position, Vector3 rotationDegrees, float scale, bool isEnemy, Node deletedNode, int player = -1)
		: this(objectType, objectId, position, rotationDegrees, scale, deletedNode, player >= 0 ? player : (isEnemy ? 1 : 0))
	{
	}

	public void Undo()
	{
		_spawnedNode = GameContext.Host?.SpawnObjectExternal(_objectType, _objectId, _position, _rotationY, _rotationDegrees, _scale, _player);
	}

	public void Redo()
	{
		if (GodotObject.IsInstanceValid(_spawnedNode))
		{
			GameContext.Host?.DeleteNodeExternal(_spawnedNode);
		}
		else if (_objectType == "prop")
		{
			GameContext.Host?.DeleteStaticPropAtPosition(_objectId, _position);
		}
	}
}