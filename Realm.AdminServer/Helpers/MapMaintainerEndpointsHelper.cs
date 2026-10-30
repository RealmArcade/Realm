using Realm.Shared.Distribution;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Realm.AdminServer.Models;
using Realm.AdminServer.Services;
using Realm.Shared;

namespace Realm.AdminServer.Helpers
{
    public static class MapMaintainerEndpointsHelper
    {
                
        private static IResult? FindMaintainerRequestError(
            string reqMapTitle,
            string routeMapTitle,
            string actionPrefix,
            string maintainerPublicKey,
            string requesterPublicKey,
            string signature,
            DataStoreService db,
            HashSet<string> adminPublicKeys,
            out string targetMap)
        {
            targetMap = !string.IsNullOrWhiteSpace(reqMapTitle) ? reqMapTitle : routeMapTitle;
            if (string.IsNullOrWhiteSpace(targetMap) || string.IsNullOrWhiteSpace(maintainerPublicKey) || string.IsNullOrWhiteSpace(requesterPublicKey) || string.IsNullOrWhiteSpace(signature))
            {
                return Results.BadRequest(new MapMaintainersResponseDto
                {
                    Success = false,
                    MapTitle = targetMap,
                    Message = "MapTitle, MaintainerPublicKey, RequesterPublicKey, and Signature are required."
                });
            }

            bool isAdmin = adminPublicKeys.Count > 0 && adminPublicKeys.Contains(requesterPublicKey.Trim());
            bool isMaintainer = MapMaintainerHelper.IsAuthorizedMaintainer(db, targetMap, requesterPublicKey);

            if (!isAdmin && !isMaintainer)
            {
                return Results.Json(new MapMaintainersResponseDto
                {
                    Success = false,
                    MapTitle = targetMap,
                    Message = $"Requester public key is not an authorized maintainer of map '{targetMap}'."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            string canonicalPayload = $"{actionPrefix}_maintainer:{targetMap.ToLowerInvariant()}:{maintainerPublicKey.Trim()}";
            string fallbackPayload1 = $"{actionPrefix}_maintainer:{targetMap}:{maintainerPublicKey}";
            string fallbackPayload2 = $"{actionPrefix}:{targetMap}:{maintainerPublicKey}";

            bool isSigValid = AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), canonicalPayload, signature)
                              || AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), fallbackPayload1, signature)
                              || AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), fallbackPayload2, signature);

            if (!isSigValid)
            {
                return Results.BadRequest(new MapMaintainersResponseDto
                {
                    Success = false,
                    MapTitle = targetMap,
                    Message = $"Invalid signature for {actionPrefix}ing maintainer."
                });
            }

            return null;
        }

        public static IResult HandleAddMapMaintainer(
            string reqMapTitle,
            string routeMapTitle,
            Realm.Shared.Distribution.AddMapMaintainerRequest req,
            DataStoreService db,
            ClusterEventService clusterEvents,
            PeerRegistry registeredPeers,
            System.Net.Http.IHttpClientFactory httpClientFactory,
            HashSet<string> adminPublicKeys)
        {
            try
            {
                var requestError = FindMaintainerRequestError(reqMapTitle, routeMapTitle, "add", req.MaintainerPublicKey, req.RequesterPublicKey, req.Signature, db, adminPublicKeys, out string targetMap);
                if (requestError != null)
                {
                    return requestError;
                }

                var updatedMaintainers = MapMaintainerHelper.AddMaintainer(db, targetMap, req.MaintainerPublicKey);
                string owner = MapMaintainerHelper.GetOwner(db, targetMap);

                var clusterEvt = new ClusterEventDto
                {
                    EventType = "map_maintainers_updated",
                    PublicKey = req.RequesterPublicKey,
                    Signature = req.Signature,
                    PayloadJson = JsonSerializer.Serialize(new MapMaintainersUpdatedEventPayload
                    {
                        MapTitle = targetMap,
                        Action = "add",
                        MaintainerPublicKey = req.MaintainerPublicKey,
                        MaintainerUsername = req.MaintainerUsername,
                        RequesterPublicKey = req.RequesterPublicKey,
                        Signature = req.Signature
                    })
                };
                clusterEvents.RecordEvent(clusterEvt, db);
                clusterEvents.BroadcastEvent(clusterEvt, registeredPeers, httpClientFactory);

                return Results.Ok(new MapMaintainersResponseDto
                {
                    Success = true,
                    MapTitle = targetMap,
                    OwnerPublicKey = owner,
                    Maintainers = updatedMaintainers,
                    Message = $"Successfully added maintainer '{req.MaintainerPublicKey}' to map '{targetMap}'."
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new MapMaintainersResponseDto
                {
                    Success = false,
                    MapTitle = routeMapTitle,
                    Message = $"Error adding maintainer: {ex.Message}"
                });
            }
        }

        public static IResult HandleRemoveMapMaintainer(
            string reqMapTitle,
            string routeMapTitle,
            Realm.Shared.Distribution.RemoveMapMaintainerRequest req,
            DataStoreService db,
            ClusterEventService clusterEvents,
            PeerRegistry registeredPeers,
            System.Net.Http.IHttpClientFactory httpClientFactory,
            HashSet<string> adminPublicKeys)
        {
            try
            {
                var requestError = FindMaintainerRequestError(reqMapTitle, routeMapTitle, "remove", req.MaintainerPublicKey, req.RequesterPublicKey, req.Signature, db, adminPublicKeys, out string targetMap);
                if (requestError != null)
                {
                    return requestError;
                }

                bool isAdmin = adminPublicKeys.Count > 0 && adminPublicKeys.Contains(req.RequesterPublicKey.Trim());
                var currentMaintainers = MapMaintainerHelper.GetMaintainers(db, targetMap);
                if (!isAdmin && currentMaintainers.Count <= 1 && currentMaintainers.Any(m => string.Equals(m, req.MaintainerPublicKey.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.BadRequest(new MapMaintainersResponseDto
                    {
                        Success = false,
                        MapTitle = targetMap,
                        Message = "Cannot remove the only remaining maintainer of the map. Transfer ownership or add another maintainer first."
                    });
                }

                var updatedMaintainers = MapMaintainerHelper.RemoveMaintainer(db, targetMap, req.MaintainerPublicKey);
                string owner = MapMaintainerHelper.GetOwner(db, targetMap);

                var clusterEvt = new ClusterEventDto
                {
                    EventType = "map_maintainers_updated",
                    PublicKey = req.RequesterPublicKey,
                    Signature = req.Signature,
                    PayloadJson = JsonSerializer.Serialize(new MapMaintainersUpdatedEventPayload
                    {
                        MapTitle = targetMap,
                        Action = "remove",
                        MaintainerPublicKey = req.MaintainerPublicKey,
                        RequesterPublicKey = req.RequesterPublicKey,
                        Signature = req.Signature
                    })
                };
                clusterEvents.RecordEvent(clusterEvt, db);
                clusterEvents.BroadcastEvent(clusterEvt, registeredPeers, httpClientFactory);

                return Results.Ok(new MapMaintainersResponseDto
                {
                    Success = true,
                    MapTitle = targetMap,
                    OwnerPublicKey = owner,
                    Maintainers = updatedMaintainers,
                    Message = $"Successfully removed maintainer '{req.MaintainerPublicKey}' from map '{targetMap}'."
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new MapMaintainersResponseDto
                {
                    Success = false,
                    MapTitle = routeMapTitle,
                    Message = ex.Message
                });
            }
        }
    }
}
