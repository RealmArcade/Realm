using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class PseudoSocketAnchor
{
	public string SocketId { get; set; } = string.Empty;
	public Vector3Data Position { get; set; }
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
