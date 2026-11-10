using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DBreeze.Transactions;
using Realm.AdminServer.Accounts;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.AdminServer.Services;

/// <summary>map_maintainers_updated events.</summary>
public partial class ClusterEventService
	{
	private bool ApplyMapMaintainersUpdated(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys)
	{
		if (!TryGetMaintainersContext(evt, db, adminPublicKeys, out var action, out var mapTitle, out var maintainerPublicKey))
			return false;

		if (action == "remove")
			MapMaintainerHelper.RemoveMaintainer(db, mapTitle, maintainerPublicKey);
		else
			MapMaintainerHelper.AddMaintainer(db, mapTitle, maintainerPublicKey);

		return true;
	}

	private bool TryGetMaintainersContext(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys, out string action, out string mapTitle, out string maintainerPublicKey)
	{
		PopulateMaintainersValues(evt, out action, out mapTitle, out maintainerPublicKey, out var requesterPublicKey, out var signature);

		if (!ValidateMaintainersRequiredFields(mapTitle, maintainerPublicKey, requesterPublicKey, signature))
			return false;

		if (!ValidateMaintainersAuthorization(db, adminPublicKeys, mapTitle, requesterPublicKey))
			return false;

		return VerifyMapMaintainersUpdatedSignature(action, mapTitle, maintainerPublicKey, requesterPublicKey, signature, evt.PayloadJson);
	}

	private void PopulateMaintainersValues(ClusterEventDto evt, out string action, out string mapTitle, out string maintainerPublicKey, out string requesterPublicKey, out string signature)
	{
		action = "add";
		mapTitle = "";
		maintainerPublicKey = "";
		requesterPublicKey = evt.PublicKey;
		signature = evt.Signature;

		var payload = ParseMapMaintainersUpdatedPayload(evt);
		if (payload == null) return;

		if (payload.Action != null) action = payload.Action.ToLowerInvariant();
		if (payload.MapTitle != null) mapTitle = payload.MapTitle;
		if (payload.MaintainerPublicKey != null) maintainerPublicKey = payload.MaintainerPublicKey;
		if (payload.RequesterPublicKey != null) requesterPublicKey = payload.RequesterPublicKey;
		if (payload.Signature != null) signature = payload.Signature;
	}

	private bool ValidateMaintainersRequiredFields(string mapTitle, string maintainerPublicKey, string requesterPublicKey, string signature)
	{
		return !string.IsNullOrWhiteSpace(mapTitle)
		       && !string.IsNullOrWhiteSpace(maintainerPublicKey)
		       && !string.IsNullOrWhiteSpace(requesterPublicKey)
		       && !string.IsNullOrWhiteSpace(signature);
	}

	private bool ValidateMaintainersAuthorization(DataStoreService db, HashSet<string> adminPublicKeys, string mapTitle, string requesterPublicKey)
	{
		bool isAdmin = adminPublicKeys.Count > 0 && adminPublicKeys.Contains(requesterPublicKey.Trim());
		bool isMaintainer = MapMaintainerHelper.IsAuthorizedMaintainer(db, mapTitle, requesterPublicKey);

		if (!isAdmin && !isMaintainer)
		{
			Console.WriteLine($"[ClusterEventService] Rejected map_maintainers_updated: Key {requesterPublicKey} is neither admin nor authorized maintainer of '{mapTitle}'");
			return false;
		}

		return true;
	}


	private MapMaintainersUpdatedEventPayload? ParseMapMaintainersUpdatedPayload(ClusterEventDto evt)
	{
		if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
		try
		{
			return JsonSerializer.Deserialize<MapMaintainersUpdatedEventPayload>(evt.PayloadJson, JsonOpts);
		}
		catch
		{
			return null;
		}
	}

	private bool VerifyMapMaintainersUpdatedSignature(string action, string mapTitle, string maintainerPublicKey, string requesterPublicKey, string signature, string? payloadJson)
	{
		string canonicalPayload = $"{action}_maintainer:{mapTitle.ToLowerInvariant()}:{maintainerPublicKey.Trim()}";
		string fallbackPayload1 = $"{action}_maintainer:{mapTitle}:{maintainerPublicKey}";
		string fallbackPayload2 = $"{action}:{mapTitle}:{maintainerPublicKey}";

		bool sigValid = AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), canonicalPayload, signature)
		                || AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), fallbackPayload1, signature)
		                || AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), fallbackPayload2, signature)
		                || (!string.IsNullOrWhiteSpace(payloadJson) && AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), payloadJson, signature));

		if (!sigValid)
			Console.WriteLine($"[ClusterEventService] Rejected map_maintainers_updated: Invalid signature for requester {requesterPublicKey}");

		return sigValid;
	}
}
