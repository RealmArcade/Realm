using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Vector3 = Godot.Vector3;

namespace Realm.Client;

public class ObjectSpawnAction : IEditorAction
{
	private readonly string _objectType;
	private readonly string _objectId;
	private readonly Vector3 _position;
	private readonly Vector3 _rotationDegrees;
	private readonly float _rotationY;
	private readonly float _scale;
	private readonly int _player;
	private Node _spawnedNode;

	public Vector3 Position => _position;
	public float Scale => _scale;
	public Node SpawnedNode => _spawnedNode;

	public ObjectSpawnAction(string objectType, string objectId, Vector3 position, float rotationY, float scale, Node spawnedNode, int player = -1)
		: this(objectType, objectId, position, new Vector3(0f, rotationY, 0f), scale, spawnedNode, player)
	{
	}

	public ObjectSpawnAction(string objectType, string objectId, Vector3 position, float rotationY, float scale, bool isEnemy, Node spawnedNode, int player = -1)
		: this(objectType, objectId, position, new Vector3(0f, rotationY, 0f), scale, spawnedNode, player >= 0 ? player : (isEnemy ? 1 : 0))
	{
	}

	public ObjectSpawnAction(string objectType, string objectId, Vector3 position, Vector3 rotationDegrees, float scale, Node spawnedNode, int player = -1)
	{
		_objectType = objectType;
		_objectId = objectId;
		_position = position;
		_rotationDegrees = rotationDegrees;
		_rotationY = rotationDegrees.Y;
		_scale = scale;
		_player = player >= 0 ? player : ((spawnedNode as Unit3D)?.Player ?? 0);
		_spawnedNode = spawnedNode;
	}

	public ObjectSpawnAction(string objectType, string objectId, Vector3 position, Vector3 rotationDegrees, float scale, bool isEnemy, Node spawnedNode, int player = -1)
		: this(objectType, objectId, position, rotationDegrees, scale, spawnedNode, player >= 0 ? player : (isEnemy ? 1 : 0))
	{
	}

	public void Undo()
	{
		if (GodotObject.IsInstanceValid(_spawnedNode))
		{
			GameHost.Instance?.DeleteNodeExternal(_spawnedNode);
		}
		else if (_objectType == "prop")
		{
			GameHost.Instance?.DeleteStaticPropAtPosition(_objectId, _position);
		}
	}

	public void Redo()
	{
		if (_objectType == "unit")
		{
			_spawnedNode = GameHost.Instance?.SpawnUnitExternal(_objectId, _position, _rotationY, _scale, _player);
		}
		else if (_objectType == "prop")
		{
			_spawnedNode = GameHost.Instance?.SpawnPropExternalWithParams(_objectId, _position, _rotationY, _scale);
		}
		else if (_objectType == "decal")
		{
			_spawnedNode = GameHost.Instance?.SpawnDecalExternalWithParams(_objectId, _position, _rotationDegrees, _scale);
		}
		else if (_objectType == "vfx")
		{
			_spawnedNode = GameHost.Instance?.SpawnVfxExternalWithParams(_objectId, _position, _rotationDegrees, Vector3.One * (_scale <= 0f ? 1.0f : _scale));
		}
	}
}