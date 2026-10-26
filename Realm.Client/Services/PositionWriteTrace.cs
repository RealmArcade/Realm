using System.Numerics;
using Realm.Ecs;

namespace Realm.Client.Services;

public enum PositionWriter : byte
{
	None,
	Interpolation,
	Movement,
	Prediction,
	Placement,
	Spawn,
	Input
}

public static class PositionWriteTrace
{
	private static readonly byte[] _lastWriter = new byte[EcsWorld.MAX_ENTITIES];
	private static readonly float[] _lastInterpolationFraction = new float[EcsWorld.MAX_ENTITIES];
	private static readonly Vector3[] _lastInterpolationWrite = new Vector3[EcsWorld.MAX_ENTITIES];
	private static readonly ulong[] _lastWriteFrame = new ulong[EcsWorld.MAX_ENTITIES];

	public static bool IsEnabled;

	public static void Mark(Entity entity, PositionWriter writer)
	{
		if (!IsEnabled) return;
		if (entity.Id < 0 || entity.Id >= _lastWriter.Length) return;

		_lastWriter[entity.Id] = (byte)writer;
		_lastWriteFrame[entity.Id] = Godot.Engine.GetPhysicsFrames();
	}

	private static readonly Vector3[] _lastInterpolationFrom = new Vector3[EcsWorld.MAX_ENTITIES];
	private static readonly Vector3[] _lastInterpolationTarget = new Vector3[EcsWorld.MAX_ENTITIES];
	private static readonly Vector3[] _lastReadBack = new Vector3[EcsWorld.MAX_ENTITIES];

	public static void MarkInterpolation(Entity entity, float fraction, Vector3 from, Vector3 target, Vector3 written, Vector3 readBack)
	{
		if (!IsEnabled) return;
		if (entity.Id < 0 || entity.Id >= _lastWriter.Length) return;

		_lastReadBack[entity.Id] = readBack;

		_lastWriter[entity.Id] = (byte)PositionWriter.Interpolation;
		_lastInterpolationFraction[entity.Id] = fraction;
		_lastInterpolationFrom[entity.Id] = from;
		_lastInterpolationTarget[entity.Id] = target;
		_lastInterpolationWrite[entity.Id] = written;
		_lastWriteFrame[entity.Id] = Godot.Engine.GetPhysicsFrames();
	}

	public static Vector3 LastInterpolationFrom(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastInterpolationFrom.Length) return Vector3.Zero;

		return _lastInterpolationFrom[entity.Id];
	}

	public static Vector3 LastReadBack(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastReadBack.Length) return Vector3.Zero;

		return _lastReadBack[entity.Id];
	}

	public static Vector3 LastInterpolationTarget(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastInterpolationTarget.Length) return Vector3.Zero;

		return _lastInterpolationTarget[entity.Id];
	}

	public static ulong LastWriteFrame(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastWriteFrame.Length) return 0;

		return _lastWriteFrame[entity.Id];
	}

	public static PositionWriter Last(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastWriter.Length) return PositionWriter.None;

		return (PositionWriter)_lastWriter[entity.Id];
	}

	public static float LastInterpolationFraction(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastInterpolationFraction.Length) return -1f;

		return _lastInterpolationFraction[entity.Id];
	}

	public static Vector3 LastInterpolationWrite(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastInterpolationWrite.Length) return Vector3.Zero;

		return _lastInterpolationWrite[entity.Id];
	}
}
