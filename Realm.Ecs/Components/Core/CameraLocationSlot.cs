using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents a saved camera location and zoom level.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct CameraLocationSlot
{
	public Vector3 Position;
	public float ZoomLevel;
	public byte IsSetByte;

	public bool IsSet
	{
		get => IsSetByte != 0;
		set => IsSetByte = value ? (byte)1 : (byte)0;
	}
}