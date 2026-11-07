using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using NSec.Cryptography;
using Realm.AdminServer.Accounts;
using Realm.AdminServer.Models;
using Realm.AdminServer.Services;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.AdminServer.Helpers;

namespace Realm.AdminServer;

/// <summary>The generic collection CRUD surface behind the editor and admin tools.</summary>
internal static class DataEndpoints
{
	internal static void MapDataEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapGet("/api/data/{collection}/{id}", (string collection, string id, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", collection)) return Results.Unauthorized();
			var item = db.Get<JsonDocument>(collection, id);
			if (item != null)
			{
				return Results.Ok(item);
			}

			return Results.NotFound(new { Message = "Not found" });
		});

		app.MapGet("/api/data/{collection}", (string collection, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", collection)) return Results.Unauthorized();
			var items = db.GetAll<JsonDocument>(collection);
			return Results.Ok(items);
		});

		app.MapPost("/api/data/{collection}", (string collection, JsonDocument data, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", collection)) return Results.Unauthorized();
			var id = Guid.NewGuid().ToString("N");
			db.Upsert(collection, id, data);
			return Results.Ok(new { Id = id });
		});

		app.MapPut("/api/data/{collection}/{id}", (string collection, string id, JsonDocument data, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", collection)) return Results.Unauthorized();
			db.Upsert(collection, id, data);
			return Results.Ok(new { Status = "Updated" });
		});

		app.MapDelete("/api/data/{collection}/{id}", (string collection, string id, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", collection)) return Results.Unauthorized();
			db.Delete(collection, id);
			return Results.Ok(new { Status = "Deleted" });
		});

		app.MapGet("/api/players", (DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "players")) return Results.Unauthorized();
			return Results.Ok(db.GetAll<JsonDocument>("players"));
		});
		app.MapGet("/api/players/{id}", (string id, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "players")) return Results.Unauthorized();
			var p = db.Get<JsonDocument>("players", id);
			return p != null ? Results.Ok(p) : Results.NotFound();
		});
		app.MapPost("/api/players", (JsonDocument data, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "players")) return Results.Unauthorized();
			var id = Guid.NewGuid().ToString("N");
			db.Upsert("players", id, data);
			return Results.Ok(new { Id = id });
		});
		app.MapPut("/api/players/{id}", (string id, JsonDocument data, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "players")) return Results.Unauthorized();
			db.Upsert("players", id, data);
			return Results.Ok();
		});
		app.MapDelete("/api/players/{id}", (string id, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "players")) return Results.Unauthorized();
			db.Delete("players", id);
			return Results.Ok();
		});

		app.MapGet("/api/maps", (DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "maps")) return Results.Unauthorized();
			return Results.Ok(db.GetAll<JsonDocument>("maps"));
		});
		app.MapGet("/api/maps/{id}", (string id, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "maps")) return Results.Unauthorized();
			var m = db.Get<JsonDocument>("maps", id);
			return m != null ? Results.Ok(m) : Results.NotFound();
		});
		app.MapPost("/api/maps", (JsonDocument data, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "maps")) return Results.Unauthorized();
			var id = Guid.NewGuid().ToString("N");
			db.Upsert("maps", id, data);
			return Results.Ok(new { Id = id });
		});
		app.MapPut("/api/maps/{id}", (string id, JsonDocument data, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "maps")) return Results.Unauthorized();
			db.Upsert("maps", id, data);
			return Results.Ok();
		});
		app.MapDelete("/api/maps/{id}", (string id, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "maps")) return Results.Unauthorized();
			db.Delete("maps", id);
			return Results.Ok();
		});
	}
}
