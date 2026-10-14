using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
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
	private readonly bool _isEnemy;
	private readonly int _player;
	private Node _spawnedNode;

	public Vector3 Position => _position;
	public float Scale => _scale;
	public Node SpawnedNode => _spawnedNode;

	public ObjectSpawnAction(string objectType, string objectId, Vector3 position, float rotationY, float scale, bool isEnemy, Node spawnedNode, int player = -1)
		: this(objectType, objectId, position, new Vector3(0f, rotationY, 0f), scale, isEnemy, spawnedNode, player)
	{
	}

	public ObjectSpawnAction(string objectType, string objectId, Vector3 position, Vector3 rotationDegrees, float scale, bool isEnemy, Node spawnedNode, int player = -1)
	{
		_objectType = objectType;
		_objectId = objectId;
		_position = position;
		_rotationDegrees = rotationDegrees;
		_rotationY = rotationDegrees.Y;
		_scale = scale;
		_player = player >= 0 ? player : ((spawnedNode as Unit3D)?.Player ?? 0);
		_isEnemy = NetworkService.ArePlayerIndicesEnemies(GameHost.Instance?.LocalPlayerIndex ?? 0, _player);
		_spawnedNode = spawnedNode;
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
		_spawnedNode = SpawnEditorObject(_objectType, _objectId, _position, _rotationY, _rotationDegrees, _scale, _isEnemy, _player);
	}

	public static Node SpawnEditorObject(string objectType, string objectId, Vector3 position, float rotationY, Vector3 rotationDegrees, float scale, bool isEnemy, int player)
	{
		if (objectType == "unit")
		{
			return GameHost.Instance?.SpawnUnitExternal(objectId, position, isEnemy, rotationY, scale, player);
		}
		else if (objectType == "prop")
		{
			return GameHost.Instance?.SpawnPropExternalWithParams(objectId, position, rotationY, scale);
		}
		else if (objectType == "decal")
		{
			return GameHost.Instance?.SpawnDecalExternalWithParams(objectId, position, rotationDegrees, scale);
		}
		else if (objectType == "vfx")
		{
			return GameHost.Instance?.SpawnVfxExternalWithParams(objectId, position, rotationDegrees, Vector3.One * (scale <= 0f ? 1.0f : scale));
		}
		return null;
	}
}