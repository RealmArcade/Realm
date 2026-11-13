using Realm.Shared.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

[SchemaDescription("A named point on a placeable's model that attachments and visual effects can be parented to, for a mesh that has no real socket there.")]
public class PseudoSocketAnchor
{
	[SchemaDescription("The name a unit's object attachment table names this point by.")]
	[SchemaSensitive]
	public string SocketId { get; set; } = string.Empty;

	[SchemaDescription("Where the point sits in the model's own space, in metres.")]
	public Vector3Data Position { get; set; }

	[SchemaDescription("How the point is turned in the model's own space, in degrees around each axis.")]
	public Vector3Data RotationDegrees { get; set; }

	public PseudoSocketAnchor Clone()
	{
		return new PseudoSocketAnchor
		{
			SocketId = SocketId,
			Position = new Vector3Data(Position.X, Position.Y, Position.Z),
			RotationDegrees = new Vector3Data(RotationDegrees.X, RotationDegrees.Y, RotationDegrees.Z)
		};
	}
}
