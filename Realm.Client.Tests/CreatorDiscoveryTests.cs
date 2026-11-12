using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GdUnit4;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Shared.Distribution;

namespace Realm.Client.Tests;

[TestSuite]
public partial class CreatorDiscoveryTests
{
	private static int GetFreePort()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		return ((IPEndPoint)listener.LocalEndpoint).Port;
	}

	[TestCase]
	public void LoadPlaytimeCreatorsIgnoresZeroAndNegativeAndMergesCase()
	{
		string tempFile = Path.Combine(Path.GetTempPath(), $"realm_playtime_{Guid.NewGuid():N}.json");
		try
		{
			File.WriteAllText(tempFile, """{"Alice": 45, "alice": 15, "ZeroCreator": 0, "Negative": -5, " ": 20}""");
			Dictionary<string, int> result = CreatorDiscoveryService.LoadPlaytimeCreators(tempFile);

			Assertions.AssertThat(result.Count).IsEqual(1);
			Assertions.AssertThat(result["Alice"]).IsEqual(60);
		}
		finally
		{
			if (File.Exists(tempFile)) File.Delete(tempFile);
		}
	}

	[TestCase]
	public async Task LoadPortfolioCollapsesLegacyAssetSignaturesIntoOneAssetPackAndMergesMaps()
	{
		int port = GetFreePort();
		string baseUrl = $"http://127.0.0.1:{port}";
		using var server = new HttpListener();
		server.Prefixes.Add(baseUrl + "/");
		server.Start();

		Task respondTask = Task.Run(async () =>
		{
			var ctx = await server.GetContextAsync();
			var legacyAssets = new List<object>();
			for (int i = 0; i < 50; i++)
			{
				legacyAssets.Add(new { Hash = $"hash_{i % 25:D4}.webp", AuthorUsername = "Realm" });
			}

			byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(legacyAssets));
			ctx.Response.ContentType = "application/json";
			ctx.Response.ContentLength64 = payload.Length;
			await ctx.Response.OutputStream.WriteAsync(payload);
			ctx.Response.Close();
		});

		var creator = new CreatorInfo
		{
			PublicKey = "realm_pub_key",
			Username = "Realm",
			IsRegistered = true
		};

		var catalogMaps = new List<DiscoveryMapDto>
		{
			new()
			{
				MapId = "ChaosArena_1.0.0",
				Title = "ChaosArena",
				Version = "1.0.0",
				Creator = "Realm",
				IsGreenlit = true,
				GamesPlayed = 100
			},
			new()
			{
				MapId = "ChaosArena_1.1.0",
				Title = "ChaosArena",
				Version = "1.1.0",
				Creator = "Realm",
				IsGreenlit = true,
				GamesPlayed = 250
			},
			new()
			{
				MapId = "ChaosArena_test_1.2.0",
				Title = "ChaosArena",
				Version = "1.2.0",
				Creator = "Realm",
				IsTestVersion = true
			},
			new()
			{
				MapId = "OtherMap_1.0.0",
				Title = "OtherMap",
				Version = "1.0.0",
				Creator = "AnotherCreator"
			}
		};

		CreatorPortfolioResult result = await CreatorDiscoveryService.LoadPortfolioAsync(baseUrl, creator, catalogMaps);
		await respondTask;

		Assertions.AssertThat(result.RequestSucceeded).IsTrue();
		Assertions.AssertThat(result.AssetPack).IsNotNull();
		Assertions.AssertThat(result.AssetPack!.AssetCount).IsEqual(25);
		Assertions.AssertThat(result.Maps.Count).IsEqual(1);
		Assertions.AssertThat(result.Maps[0].Title).IsEqual("ChaosArena");
		Assertions.AssertThat(result.Maps[0].Version).IsEqual("1.1.0");
	}

	[TestCase]
	public async Task LoadPortfolioReadsStructuredDtoAndExcludesZeroAssetPack()
	{
		int port = GetFreePort();
		string baseUrl = $"http://127.0.0.1:{port}";
		using var server = new HttpListener();
		server.Prefixes.Add(baseUrl + "/");
		server.Start();

		Task respondTask = Task.Run(async () =>
		{
			var ctx = await server.GetContextAsync();
			var dto = new CreatorPortfolioDto
			{
				PublicKey = "alice_key",
				Username = "Alice",
				Maps = new List<DiscoveryMapDto>
				{
					new()
					{
						MapId = "Valley_2.0.0",
						Title = "Valley",
						Version = "2.0.0",
						Creator = "Alice",
						ThumbnailHash = "abc123.webp",
						IsGreenlit = true,
						RatingStars = 4.8f,
						TotalReviews = 12,
						GamesPlayed = 40
					}
				},
				AssetPack = null
			};

			byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto));
			ctx.Response.ContentType = "application/json";
			ctx.Response.ContentLength64 = payload.Length;
			await ctx.Response.OutputStream.WriteAsync(payload);
			ctx.Response.Close();
		});

		var creator = new CreatorInfo
		{
			PublicKey = "alice_key",
			Username = "Alice",
			IsRegistered = true
		};

		CreatorPortfolioResult result = await CreatorDiscoveryService.LoadPortfolioAsync(baseUrl, creator);
		await respondTask;

		Assertions.AssertThat(result.RequestSucceeded).IsTrue();
		Assertions.AssertThat(result.AssetPack).IsNull();
		Assertions.AssertThat(result.Maps.Count).IsEqual(1);
		Assertions.AssertThat(result.Maps[0].Title).IsEqual("Valley");
		Assertions.AssertThat(result.Maps[0].ThumbnailHash).IsEqual("abc123.webp");
		Assertions.AssertThat(result.Maps[0].IsGreenlit).IsTrue();
		Assertions.AssertThat(result.Maps[0].TotalReviews).IsEqual(12);
		Assertions.AssertThat(result.Maps[0].GamesPlayed).IsEqual(40);
	}
}
