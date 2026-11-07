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

/// <summary>Seeder registration and the public seeder listings.</summary>
internal static class SeederEndpoints
{
	internal static void MapSeederEndpoints(WebApplication app, ServerSetup setup)
	{
	app.MapPost("/seeders/register", (SeederRegisterRequest req, SeederRegistry registry, HttpContext context) =>
	{
		var ip = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
		if (!string.IsNullOrEmpty(req.ReportedIP) && req.ReportedIP != "0.0.0.0" && req.ReportedIP != "127.0.0.1")
		{
			ip = req.ReportedIP;
		}

		var info = new SeederInfo
		{
			SeederId = req.SeederId,
			IP = ip,
			Port = req.Port,
			MapIds = req.MapIds,
			CapacityPercentage = req.CapacityPercentage,
			AcceptingUploads = req.AcceptingUploads
		};
		registry.Register(info);
		return Results.Ok(new { Status = "Registered" });
	});

	app.MapPost("/seeders/unregister", (SeederUnregisterRequest req, SeederRegistry registry) =>
	{
		registry.Unregister(req.SeederId);
		return Results.Ok(new { Status = "Unregistered" });
	});

	app.MapGet("/seeders", (SeederRegistry registry) =>
	{
		var list = registry.GetAll().Select(s => new
		{
			ip = s.IP,
			port = s.Port,
			mapIds = s.MapIds,
			capacityPercentage = s.CapacityPercentage,
			acceptingUploads = s.AcceptingUploads
		});
		return Results.Ok(list);
	});

	app.MapGet("/api/seeders", (SeederRegistry registry) =>
	{
		var list = registry.GetAll().Select(s => new SeederNodeDto
		{
			SeederId = s.SeederId,
			IP = s.IP,
			Port = s.Port,
			CapacityPercentage = s.CapacityPercentage,
			AcceptingUploads = s.AcceptingUploads,
			MapIds = s.MapIds
		});
		return Results.Ok(list);
	});

	app.MapGet("/api/seeders/catalog", (ContentAddressableStorage cas) =>
	{
		return Results.Ok(new SeederCatalogResponseDto
		{
			SeederId = setup.SeederId,
			CapacityPercentage = setup.CapacityPercent,
			AssetHashes = cas.GetAllStoredHashes().ToList()
		});
	});
	}
}
