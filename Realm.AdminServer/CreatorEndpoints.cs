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

/// <summary>Creator registration, presence checks, listings and portfolios.</summary>
public static class CreatorEndpoints
{
	public static void MapCreatorEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapPost("/api/creators/register", async (HttpRequest request, RegisterCreatorRequest req, DataStoreService db, AccountStoreService accounts, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
		{
			if (!NameNormalizationHelper.ValidateUsername(req.Username, out var usernameError))
			{
				return Results.BadRequest(new { Message = usernameError });
			}

			string username = req.Username.Trim();
			string pubKey = req.PublicKey.Trim();
			string signature = req.Signature.Trim();

			string? bypassToken = request.Headers["X-Admin-Bypass"];
			bool bypassValid = setup.AdminPublicKeys.Count > 0 &&
			                   AdminBypassAuth.VerifyBypassToken(setup.AdminPublicKeys, "admin", "override", bypassToken);

			bool sigValid = CreatorRegistrationProof.Verify(pubKey, username, req.DonationLink, req.ContactInfo, signature)
			                || (bypassValid && CreatorRegistrationProof.VerifyByAnyAuthority(setup.AdminPublicKeys, pubKey, username, req.DonationLink, req.ContactInfo, signature));

			if (!sigValid)
			{
				return Results.BadRequest(new { Message = "Invalid cryptographic signature for public key." });
			}

			if (!bypassValid)
			{
				string? sessionToken = AccountEndpoints.ReadSessionToken(request);
				if (!accounts.TryValidateSession(sessionToken, out AccountRecord? signingInAccount, out _, out _) || signingInAccount == null)
				{
					return Results.Json(
						AccountErrorDto.Of(AccountErrorCodes.SessionInvalid, "Sign in with Steam before registering a map key."),
						statusCode: StatusCodes.Status401Unauthorized);
				}

				if (!DisplayNameRules.TryCanonicalize(username, out string canonicalUsername, out _))
				{
					return Results.BadRequest(new { Message = "The username is not a valid creator name." });
				}

				if (!string.Equals(signingInAccount.UniquenessKey, DisplayNameRules.ComputeUniquenessKey(canonicalUsername), StringComparison.Ordinal))
				{
					return Results.Conflict(new { Message = "A map key can only be registered under the name this Steam account is signed in with." });
				}
			}

			string slug = CreatorNamePolicy.GetSlug(username);

			if (CreatorNamePolicy.TryReadNameLockOwner(db, username, out string existingOwner)
			    && !string.Equals(existingOwner, pubKey, StringComparison.OrdinalIgnoreCase))
			{
				if (!bypassValid && CreatorNamePolicy.HasGreenlitPublishedMap(db, existingOwner))
				{
					return Results.Conflict(new { Message = "This username belongs to a creator with a greenlit map. A Realm administrator has to release it before another key can claim it." });
				}

				CreatorNamePolicy.ReleaseCreatorKey(db, existingOwner, username);
			}

			var lockDoc = JsonSerializer.SerializeToDocument(new
			{
				username = username,
				owner_public_key = pubKey,
				registered_at = DateTime.UtcNow
			});
			db.Upsert(CreatorNamePolicy.NameLocksCollection, slug, lockDoc);

			var creatorDoc = JsonSerializer.SerializeToDocument(new
			{
				username = username,
				public_key = pubKey,
				donation_link = req.DonationLink ?? "",
				contact_info = req.ContactInfo ?? "",
				registered_at = DateTime.UtcNow
			});
			db.Upsert(CreatorNamePolicy.CreatorsCollection, pubKey, creatorDoc);

			var creatorEvent = new ClusterEventDto
			{
				EventType = "creator_registered",
				PublicKey = pubKey,
				Signature = signature,
				PayloadJson = JsonSerializer.Serialize(new CreatorRegisteredEventPayload
				{
					Username = username,
					PublicKey = pubKey,
					Signature = signature,
					DonationLink = req.DonationLink,
					ContactInfo = req.ContactInfo,
					AdminBypassToken = request.Headers["X-Admin-Bypass"].ToString()
				})
			};
			clusterEvents.MarkEventProcessed(creatorEvent.EventId, db);
			clusterEvents.BroadcastEvent(creatorEvent, registeredPeers, httpClientFactory);

			return Results.Ok(new { Status = "Registered", Username = username, PublicKey = pubKey });
		});

		app.MapGet("/api/creators/check/{pubKey}", (string pubKey, DataStoreService db) =>
		{
			var creator = db.Get<JsonDocument>("creators", pubKey);
			if (creator != null)
			{
				var root = creator.RootElement;
				string username = root.TryGetProperty("username", out var uProp) ? uProp.GetString() ?? "" : "";
				return Results.Ok(new { Registered = true, Username = username, PublicKey = pubKey });
			}

			return Results.NotFound(new { Registered = false, Message = "Creator key not registered." });
		});

		app.MapGet("/api/creators", (DataStoreService db, ContentAddressableStorage cas) =>
		{
			var catalog = PublishedMapCatalogHelper.GroupLatestByTitle(
				PublishedMapCatalogHelper.BuildCatalog(db, cas, includeTestVersions: false));
			var allSignatures = db.GetAll<JsonDocument>("asset_signatures");

			var rawCreators = db.GetAll<JsonDocument>("creators").Select(doc =>
			{
				var root = doc.RootElement;
				return new
				{
					PublicKey = root.TryGetProperty("public_key", out var pk) ? pk.GetString() ?? "" : "",
					Username = root.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "",
					DonationLink = root.TryGetProperty("donation_link", out var dl) ? dl.GetString() ?? "" : "",
					ContactInfo = root.TryGetProperty("contact_info", out var ci) ? ci.GetString() ?? "" : ""
				};
			}).Where(c => !string.IsNullOrEmpty(c.PublicKey) && !string.IsNullOrWhiteSpace(c.Username)).ToList();

			var creators = rawCreators
				.GroupBy(c => c.PublicKey, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.First())
				.Select(c =>
				{
					int mapCount = catalog.Count(entry => PublishedMapCatalogHelper.MatchesCreator(entry, c.PublicKey, c.Username));
					int assetCount = CollectCreatorAssetHashes(allSignatures, c.PublicKey, c.Username).Count;
					return new CreatorSummaryDto
					{
						PublicKey = c.PublicKey,
						Username = c.Username,
						DonationLink = c.DonationLink,
						ContactInfo = c.ContactInfo,
						PublishedMapCount = mapCount,
						AssetCount = assetCount
					};
				})
				.OrderBy(c => c.Username, StringComparer.OrdinalIgnoreCase)
				.ToList();

			return Results.Ok(creators);
		});

		app.MapGet("/api/creators/{pubKey}", (string pubKey, DataStoreService db) =>
		{
			var creator = db.Get<JsonDocument>("creators", pubKey);
			return creator != null ? Results.Ok(creator) : Results.NotFound();
		});

		app.MapGet("/api/creators/{pubKey}/portfolio", (string pubKey, DataStoreService db, ContentAddressableStorage cas) =>
		{
			var creator = db.Get<JsonDocument>("creators", pubKey);
			string creatorName = "";
			if (creator != null && creator.RootElement.TryGetProperty("username", out var uProp))
			{
				creatorName = uProp.GetString() ?? "";
			}

			var catalog = PublishedMapCatalogHelper.GroupLatestByTitle(
				PublishedMapCatalogHelper.BuildCatalog(db, cas, includeTestVersions: false));
			var creatorMaps = catalog
				.Where(entry => PublishedMapCatalogHelper.MatchesCreator(entry, pubKey, creatorName))
				.Select(entry => entry.Dto)
				.OrderByDescending(m => m.GamesPlayed)
				.ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
				.ToList();

			var allSignatures = db.GetAll<JsonDocument>("asset_signatures");
			var uniqueHashes = CollectCreatorAssetHashes(allSignatures, pubKey, creatorName);

			CreatorAssetPackDto? assetPack = null;
			if (uniqueHashes.Count > 0)
			{
				string displayName = string.IsNullOrWhiteSpace(creatorName) ? "Creator" : creatorName.Trim();
				long totalBytes = 0;
				string representativeHash = "";
				foreach (string hash in uniqueHashes)
				{
					if (string.IsNullOrEmpty(representativeHash))
					{
						representativeHash = hash;
					}

					string? filePath = cas.FindAssetFilePath(hash);
					if (filePath != null && File.Exists(filePath))
					{
						totalBytes += new FileInfo(filePath).Length;
					}
				}

				assetPack = new CreatorAssetPackDto
				{
					Title = $"{displayName} Asset Pack",
					AuthorUsername = creatorName,
					AuthorPublicKey = pubKey,
					RepresentativeHash = representativeHash,
					AssetCount = uniqueHashes.Count,
					TotalSizeBytes = totalBytes
				};
			}

			return Results.Ok(new CreatorPortfolioDto
			{
				PublicKey = pubKey,
				Username = creatorName,
				Maps = creatorMaps,
				AssetPack = assetPack
			});
		});
	}

	private static HashSet<string> CollectCreatorAssetHashes(
		IEnumerable<JsonDocument> allSignatures,
		string pubKey,
		string creatorName)
	{
		var uniqueHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var sig in allSignatures)
		{
			var root = sig.RootElement;
			string sigPubKey = root.TryGetProperty("PublicKey", out var pkProp) ? pkProp.GetString() ?? "" : "";
			string author = root.TryGetProperty("AuthorUsername", out var aProp) ? aProp.GetString() ?? "" : "";
			string rawHash = root.TryGetProperty("Hash", out var hProp) ? hProp.GetString() ?? "" : "";

			bool matches = (!string.IsNullOrWhiteSpace(pubKey) && string.Equals(sigPubKey, pubKey, StringComparison.OrdinalIgnoreCase)) ||
			               (!string.IsNullOrWhiteSpace(creatorName) && string.Equals(author, creatorName, StringComparison.OrdinalIgnoreCase));
			if (!matches || string.IsNullOrWhiteSpace(rawHash))
			{
				continue;
			}

			string normHash = ContentAddressableStorage.NormalizeBlake3Hash(rawHash);
			if (!string.IsNullOrWhiteSpace(normHash))
			{
				uniqueHashes.Add(normHash);
			}
		}

		return uniqueHashes;
	}
}
