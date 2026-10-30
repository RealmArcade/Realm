using System.Text.Json.Nodes;

namespace Realm.AdminServer.Helpers;

public static class PublishedMapDocumentHelper
{
	public static JsonObject Build(string manifestJson, bool isTestVersion)
	{
		JsonObject document = JsonNode.Parse(manifestJson)?.AsObject()
			?? throw new InvalidOperationException("A published map manifest must be a JSON object.");

		document["IsTestVersion"] = isTestVersion;
		return document;
	}
}
