using Godot;
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
		_player = player >= 0 ? player : ((spawnedNode as Realm.Client.Unit3D)?.Player ?? 0);
		_isEnemy = NetworkService.ArePlayerIndicesEnemies(Realm.Client.Core.GameHost.Instance?.LocalPlayerIndex ?? 0, _player);
		_spawnedNode = spawnedNode;
	}

	public void Undo()
	{
		if (GodotObject.IsInstanceValid(_spawnedNode))
		{
			Realm.Client.Core.GameHost.Instance?.DeleteNodeExternal(_spawnedNode);
		}
		else if (_objectType == "prop")
		{
			Realm.Client.Core.GameHost.Instance?.DeleteStaticPropAtPosition(_objectId, _position);
		}
	}

	public void Redo()
	{
		if (_objectType == "unit")
		{
			_spawnedNode = Realm.Client.Core.GameHost.Instance?.SpawnUnitExternal(_objectId, _position, _isEnemy, _rotationY, _scale, _player);
		}
		else if (_objectType == "prop")
		{
			_spawnedNode = Realm.Client.Core.GameHost.Instance?.SpawnPropExternalWithParams(_objectId, _position, _rotationY, _scale);
		}
		else if (_objectType == "decal")
		{
			_spawnedNode = Realm.Client.Core.GameHost.Instance?.SpawnDecalExternalWithParams(_objectId, _position, _rotationDegrees, _scale);
		}
		else if (_objectType == "vfx")
		{
			_spawnedNode = Realm.Client.Core.GameHost.Instance?.SpawnVfxExternalWithParams(_objectId, _position, _rotationDegrees, global::Godot.Vector3.One * (_scale <= 0f ? 1.0f : _scale));
		}
	}
}