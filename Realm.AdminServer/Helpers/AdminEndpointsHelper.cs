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
            string mapVersion,
            HttpRequest request,
            ClusterEventService clusterEvents,
            DataStoreService db,
            Realm.Shared.Distribution.ContentAddressableStorage cas,
            PeerRegistry registeredPeers,
            System.Net.Http.IHttpClientFactory httpClientFactory,
            List<string> adminPublicKeys)
        {
            string? callerPubKey = request.Headers["X-Admin-PublicKey"];
            string? callerSig = request.Headers["X-Admin-Signature"];
            if (string.IsNullOrEmpty(callerSig)) callerSig = request.Headers["X-Cluster-Signature"];
            string? bypassHeader = request.Headers["X-Admin-Bypass"];

            bool isAuth = false;
            if (!string.IsNullOrEmpty(bypassHeader) && adminPublicKeys.Count > 0)
            {
                isAuth = AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, mapVersion, bypassHeader);
            }
            else if (adminPublicKeys.Count > 0 && !string.IsNullOrEmpty(callerPubKey) && !string.IsNullOrEmpty(callerSig))
            {
                if (adminPublicKeys.Contains(callerPubKey.Trim()))
                {
                    string targetVerStr = string.IsNullOrEmpty(mapVersion) ? "all" : mapVersion;
                    isAuth = AuthorSignatureHelper.VerifySignature(callerPubKey.Trim(), $"{mapTitle}:{targetVerStr}", callerSig)
                             || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, targetVerStr, callerSig);
                }
            }

            if (!isAuth)
            {
                return Results.Json(new RemoveManifestResponseDto { Success = false, Message = "Unauthorized admin request." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = clusterEvents.RemoveManifest(cas, db, mapTitle, mapVersion);

            var removeEvent = new ClusterEventDto
            {
                EventType = "admin_remove_manifest",
                PublicKey = !string.IsNullOrEmpty(callerPubKey) ? callerPubKey.Trim() : (adminPublicKeys.FirstOrDefault() ?? ""),
                Signature = callerSig,
                PayloadJson = JsonSerializer.Serialize(new AdminRemoveManifestEventPayload
                {
                    MapTitle = mapTitle,
                    MapVersion = mapVersion,
                    AdminPublicKey = callerPubKey,
                    Signature = callerSig
                })
            };
            clusterEvents.MarkEventProcessed(removeEvent.EventId, db);
            clusterEvents.BroadcastEvent(removeEvent, registeredPeers, httpClientFactory);

            return Results.Ok(result);
        }

        public static async Task<bool> VerifyClusterSyncRequest(
            HttpRequest request,
            string body,
            List<string> adminPublicKeys,
            string syncType = "sync_metrics")
        {
            string? sigHeader = request.Headers["X-Cluster-Signature"];
            string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];
            string? bypassHeader = request.Headers["X-Admin-Bypass"];

            return (!string.IsNullOrEmpty(bypassHeader) && adminPublicKeys.Count > 0 && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "cluster", "sync", bypassHeader))
                   || (!string.IsNullOrEmpty(sigHeader) && (
                       (!string.IsNullOrEmpty(pubKeyHeader) && adminPublicKeys.Contains(pubKeyHeader.ToString().Trim()) && (AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), syncType, sigHeader.ToString()) || AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), body, sigHeader.ToString())))
                       || (adminPublicKeys.Count > 0 && adminPublicKeys.Any(pk => AuthorSignatureHelper.VerifySignature(pk, syncType, sigHeader.ToString()) || AuthorSignatureHelper.VerifySignature(pk, body, sigHeader.ToString())))
                   ));
        }
    }
}
