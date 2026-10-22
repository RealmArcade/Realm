using Realm.Shared.Distribution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Realm.AdminServer.Models;
using Realm.AdminServer.Services;
using Realm.Shared;

namespace Realm.AdminServer.Helpers
{
    public static class AdminEndpointsHelper
    {
        public static IResult HandleRemoveManifest(
            string mapTitle,
            string? mapVersion,
            string? callerPublicKey,
            string? callerSignature,
            HttpRequest request,
            ClusterEventService clusterEvents,
            DataStoreService db,
            Realm.Shared.Distribution.ContentAddressableStorage cas,
            PeerRegistry registeredPeers,
            System.Net.Http.IHttpClientFactory httpClientFactory,
            List<string> adminPublicKeys)
        {
            string? bypassHeader = request.Headers["X-Admin-Bypass"];

            if (!IsRemoveManifestAuthorized(adminPublicKeys, mapTitle, mapVersion, callerPublicKey, callerSignature, bypassHeader))
            {
                return Results.Json(new RemoveManifestResponseDto { Success = false, Message = "Unauthorized admin request." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = clusterEvents.RemoveManifest(cas, db, mapTitle, mapVersion);

            string replicationSignature = !string.IsNullOrWhiteSpace(callerSignature) ? callerSignature : bypassHeader ?? "";
            string eventPublicKey = !string.IsNullOrWhiteSpace(callerPublicKey) ? callerPublicKey.Trim() : (adminPublicKeys.FirstOrDefault() ?? "");

            var removeEvent = new ClusterEventDto
            {
                EventType = "admin_remove_manifest",
                PublicKey = eventPublicKey,
                Signature = replicationSignature,
                PayloadJson = JsonSerializer.Serialize(new AdminRemoveManifestEventPayload
                {
                    MapTitle = mapTitle,
                    MapVersion = mapVersion,
                    AdminPublicKey = callerPublicKey ?? "",
                    Signature = replicationSignature
                })
            };
            clusterEvents.MarkEventProcessed(removeEvent.EventId, db);
            clusterEvents.BroadcastEvent(removeEvent, registeredPeers, httpClientFactory);

            return Results.Ok(result);
        }

        private static bool IsRemoveManifestAuthorized(
            List<string> adminPublicKeys,
            string mapTitle,
            string? mapVersion,
            string? callerPublicKey,
            string? callerSignature,
            string? bypassToken)
        {
            if (adminPublicKeys.Count == 0) return false;

            string trimmedTitle = mapTitle.Trim();
            string targetVersion = string.IsNullOrWhiteSpace(mapVersion) ? "all" : mapVersion.Trim();

            if (!string.IsNullOrWhiteSpace(bypassToken) &&
                (AdminBypassAuth.VerifyBypassToken(adminPublicKeys, trimmedTitle, targetVersion, bypassToken) ||
                 AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "remove_manifest", bypassToken) ||
                 AdminBypassAuth.VerifyBypassToken(adminPublicKeys, trimmedTitle, "*", bypassToken)))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(callerSignature)) return false;

            string versionedPayload = $"remove_manifest:{trimmedTitle.ToLowerInvariant()}:{targetVersion.ToLowerInvariant()}";
            string unversionedPayload = $"remove_manifest:{trimmedTitle.ToLowerInvariant()}";

            string trimmedPublicKey = callerPublicKey?.Trim() ?? "";
            if (trimmedPublicKey.Length > 0 && adminPublicKeys.Contains(trimmedPublicKey))
            {
                return AuthorSignatureHelper.VerifySignature(trimmedPublicKey, versionedPayload, callerSignature) ||
                       AuthorSignatureHelper.VerifySignature(trimmedPublicKey, unversionedPayload, callerSignature) ||
                       AdminBypassAuth.VerifyBypassToken(trimmedPublicKey, trimmedTitle, targetVersion, callerSignature);
            }

            return AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, versionedPayload, callerSignature) ||
                   AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, unversionedPayload, callerSignature) ||
                   AdminBypassAuth.VerifyBypassToken(adminPublicKeys, trimmedTitle, targetVersion, callerSignature);
        }

        public static async Task<bool> VerifyClusterSyncRequest(
            HttpRequest request,
            string body,
            List<string> adminPublicKeys)
        {
            string? sigHeader = request.Headers["X-Cluster-Signature"];
            string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];
            string? bypassHeader = request.Headers["X-Admin-Bypass"];

            return (!string.IsNullOrEmpty(bypassHeader) && adminPublicKeys.Count > 0 && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "cluster", "sync", bypassHeader))
                   || (!string.IsNullOrEmpty(sigHeader) && (
                       (!string.IsNullOrEmpty(pubKeyHeader) && adminPublicKeys.Contains(pubKeyHeader.ToString().Trim()) && (AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), "sync_metrics", sigHeader.ToString()) || AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), body, sigHeader.ToString())))
                       || (adminPublicKeys.Count > 0 && adminPublicKeys.Any(pk => AuthorSignatureHelper.VerifySignature(pk, "sync_metrics", sigHeader.ToString()) || AuthorSignatureHelper.VerifySignature(pk, body, sigHeader.ToString())))
                   ));
        }
    }
}
