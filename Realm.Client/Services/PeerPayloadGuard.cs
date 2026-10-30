using System;
using MemoryPack;

namespace Realm.Client.Services;

public static class PeerPayloadGuard
{
	public static bool TryDeserialize<T>(byte[]? payload, int maximumBytes, out T value) where T : struct
	{
		value = default;
		if (payload == null || payload.Length == 0 || payload.Length > maximumBytes) return false;

		try
		{
			value = MemoryPackSerializer.Deserialize<T>(payload);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
