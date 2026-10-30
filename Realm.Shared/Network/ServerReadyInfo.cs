using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Network;

public record ServerReadyInfo(int Port, string Build, string MapHash, string WireTableHash)
{
	private static readonly JsonSerializerOptions Format = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter() }
	};

	public string ToReadyLine() => RealmProtocol.ReadyLinePrefix + JsonSerializer.Serialize(this, Format);

	public static bool TryParseReadyLine(string? line, out ServerReadyInfo? readyInfo)
	{
		readyInfo = null;
		if (line == null || !line.StartsWith(RealmProtocol.ReadyLinePrefix, StringComparison.Ordinal)) return false;

		string payload = line[RealmProtocol.ReadyLinePrefix.Length..];
		readyInfo = JsonSerializer.Deserialize<ServerReadyInfo>(payload, Format);
		return readyInfo != null;
	}
}
