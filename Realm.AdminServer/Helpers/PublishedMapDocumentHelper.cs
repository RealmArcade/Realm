using System.Text.Json.Nodes;

namespace Realm.AdminServer.Helpers;

public static class PublishedMapDocumentHelper
{
	public static JsonObject Build(string manifestJson, bool isTestVersion, string officialVersion)
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
		document["OfficialVersion"] = officialVersion;
		return document;
	}
}
