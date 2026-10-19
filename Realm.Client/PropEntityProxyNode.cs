using Godot;
using Realm.Ecs;

namespace Realm.Client;

public partial class PropEntityProxyNode : Node3D
{
	public Entity Entity { get; }
	public string PropId { get; }

	public PropEntityProxyNode(Entity entity, string propId, Vector3 position, float rotationY, float scale)
	{
		Entity = entity;
		PropId = propId;
		Position = position;
		RotationDegrees = new Vector3(0f, rotationY, 0f);
		Scale = Vector3.One * scale;
		Name = $"PropProxy_{propId}_{entity.Id}";
	}
}
