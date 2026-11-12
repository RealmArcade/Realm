using Realm.AdminServer.Helpers;
using Realm.AdminServer.Models;
using Realm.AdminServer.Services;
using Realm.Shared.Distribution;

namespace Realm.AdminServer;

public static class DiscoveryEndpoints
{
	public static void MapDiscoveryEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapGet("/api/discovery/maps", (DataStoreService db, ContentAddressableStorage cas) =>
		{
			var discoveryList = PublishedMapCatalogHelper
				.BuildCatalog(db, cas, includeTestVersions: false)
				.Select(entry => entry.Dto)
				.OrderByDescending(m => m.GamesPlayed)
				.ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
				.ToList();

			return Results.Ok(discoveryList);
		});
	}
}
