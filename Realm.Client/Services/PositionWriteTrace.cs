using System.Numerics;
using Realm.Ecs;

namespace Realm.Client.Services;

public enum PositionWriter : byte
{
	None,
	Interpolation,
	Movement,
	Spawn,
	Input
}

public static class PositionWriteTrace
{
	private static readonly byte[] _lastWriter = new byte[EcsWorld.MAX_ENTITIES];
	private static readonly float[] _lastPresentationFraction = new float[EcsWorld.MAX_ENTITIES];
	private static readonly Vector3[] _lastPresentationWrite = new Vector3[EcsWorld.MAX_ENTITIES];
	private static readonly ulong[] _lastWriteFrame = new ulong[EcsWorld.MAX_ENTITIES];

	public static bool IsEnabled;

	public static void Mark(Entity entity, PositionWriter writer)
	{
		if (!IsEnabled) return;
		if (entity.Id < 0 || entity.Id >= _lastWriter.Length) return;

		_lastWriter[entity.Id] = (byte)writer;
		_lastWriteFrame[entity.Id] = Godot.Engine.GetPhysicsFrames();
	}

	private static readonly Vector3[] _lastPresentationFrom = new Vector3[EcsWorld.MAX_ENTITIES];
	private static readonly Vector3[] _lastLatestDescription = new Vector3[EcsWorld.MAX_ENTITIES];
	private static readonly Vector3[] _lastReadBack = new Vector3[EcsWorld.MAX_ENTITIES];

	public static void MarkPresentation(Entity entity, float fraction, Vector3 from, Vector3 target, Vector3 written, Vector3 readBack)
	{
		if (!IsEnabled) return;
		if (entity.Id < 0 || entity.Id >= _lastWriter.Length) return;

		_lastReadBack[entity.Id] = readBack;

		_lastWriter[entity.Id] = (byte)PositionWriter.Interpolation;
		_lastPresentationFraction[entity.Id] = fraction;
		_lastPresentationFrom[entity.Id] = from;
		_lastLatestDescription[entity.Id] = target;
		_lastPresentationWrite[entity.Id] = written;
		_lastWriteFrame[entity.Id] = Godot.Engine.GetPhysicsFrames();
	}

	public static Vector3 LastPresentationFrom(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastPresentationFrom.Length) return Vector3.Zero;

		return _lastPresentationFrom[entity.Id];
	}

	public static Vector3 LastReadBack(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastReadBack.Length) return Vector3.Zero;

		return _lastReadBack[entity.Id];
	}

	public static Vector3 LastNewestDescription(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastLatestDescription.Length) return Vector3.Zero;

		return _lastLatestDescription[entity.Id];
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

	public static float LastPresentationFraction(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastPresentationFraction.Length) return -1f;

		return _lastPresentationFraction[entity.Id];
	}

	public static Vector3 LastPresentationWrite(Entity entity)
	{
		if (entity.Id < 0 || entity.Id >= _lastPresentationWrite.Length) return Vector3.Zero;

		return _lastPresentationWrite[entity.Id];
	}
}
