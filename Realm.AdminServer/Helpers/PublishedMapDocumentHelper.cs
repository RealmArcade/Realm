using System.Text.Json.Nodes;

namespace Realm.AdminServer.Helpers;

public static class PublishedMapDocumentHelper
{
	public static JsonObject Build(string manifestJson, bool isTestVersion)
	{
		JsonObject document;
		try
		{
			document = JsonNode.Parse(manifestJson)?.AsObject() ?? new JsonObject();
		}
		catch
		{
			document = new JsonObject();
		}

		document["IsTestVersion"] = isTestVersion;
		return document;
	}
}
