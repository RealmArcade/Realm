using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.AdminServer.Helpers
{
    public static class MapMetadataHelper
    {
        public static void ParseMapMetadataDetails(
            Realm.Shared.Distribution.ContentAddressableStorage cas, 
            string metadataFileHash, 
            ref string description,
            List<string> tags,
            out string engineVersion,
            out string maxPlayers,
            out string genre)
        {
            engineVersion = "Godot Realm Engine v1.0";
            maxPlayers = "8 Players";
            genre = tags.Count > 0 ? tags[0] : "Custom Map";

            if (!string.IsNullOrEmpty(metadataFileHash))
            {
                string? metaFilePath = cas.FindAssetFilePath(metadataFileHash);
                if (metaFilePath != null && File.Exists(metaFilePath))
                {
                    try
                    {
                        var metaDoc = JsonDocument.Parse(File.ReadAllText(metaFilePath));
                        var metaRoot = metaDoc.RootElement;
                        if (metaRoot.TryGetProperty("MapProperties", out var mp) && mp.ValueKind == JsonValueKind.Object)
                        {
                            if (string.IsNullOrEmpty(description) && mp.TryGetProperty("MapDescription", out var md))
                            {
                                string? mdStr = md.GetString();
                                if (!string.IsNullOrEmpty(mdStr)) description = mdStr;
                            }

                            if (string.IsNullOrEmpty(description) && mp.TryGetProperty("HowToPlayObjective", out var htp))
                            {
                                string? htpStr = htp.GetString();
                                if (!string.IsNullOrEmpty(htpStr)) description = htpStr;
                            }

                            if (mp.TryGetProperty("Tags", out var metaTags) && metaTags.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var t in metaTags.EnumerateArray())
                                {
                                    string? s = t.GetString();
                                    if (!string.IsNullOrEmpty(s) && !tags.Contains(s)) tags.Add(s);
                                }
                            }

                            if (mp.TryGetProperty("PlayerSlots", out var slotsProp) && slotsProp.ValueKind == JsonValueKind.Array && slotsProp.GetArrayLength() > 0)
                            {
                                maxPlayers = $"{slotsProp.GetArrayLength()} Players";
                            }
                            else if (mp.TryGetProperty("SuggestedPlayers", out var sp) && !string.IsNullOrEmpty(sp.GetString()))
                            {
                                maxPlayers = sp.GetString()!;
                            }

                            if (mp.TryGetProperty("MapType", out var mt) && !string.IsNullOrEmpty(mt.GetString()))
                            {
                                genre = mt.GetString()!;
                            }
                        }

                        if (metaRoot.TryGetProperty("Templates", out var templatesObj) && templatesObj.ValueKind == JsonValueKind.Object)
                        {
                            if (templatesObj.TryGetProperty("Units", out var cu) && cu.ValueKind == JsonValueKind.Array && cu.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Units")) tags.Add("Custom Units");
                            }

                            if (templatesObj.TryGetProperty("Buildings", out var cb) && cb.ValueKind == JsonValueKind.Array && cb.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Buildings")) tags.Add("Custom Buildings");
                            }

                            if (templatesObj.TryGetProperty("Abilities", out var ca) && ca.ValueKind == JsonValueKind.Array && ca.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Abilities")) tags.Add("Custom Abilities");
                            }

                            if (templatesObj.TryGetProperty("Weapons", out var cw) && cw.ValueKind == JsonValueKind.Array && cw.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Weapons")) tags.Add("Custom Weapons");
                            }

                            if (templatesObj.TryGetProperty("Upgrades", out var cup) && cup.ValueKind == JsonValueKind.Array && cup.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Upgrades")) tags.Add("Custom Upgrades");
                            }

                            if (templatesObj.TryGetProperty("Props", out var cpr) && cpr.ValueKind == JsonValueKind.Array && cpr.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Props")) tags.Add("Custom Props");
                            }

                            if (templatesObj.TryGetProperty("Items", out var ci) && ci.ValueKind == JsonValueKind.Array && ci.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Items")) tags.Add("Custom Items");
                            }

                            if (templatesObj.TryGetProperty("Attachments", out var cat) && cat.ValueKind == JsonValueKind.Array && cat.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Attachments")) tags.Add("Custom Attachments");
                            }

                            if (templatesObj.TryGetProperty("Vfx", out var cvfx) && cvfx.ValueKind == JsonValueKind.Array && cvfx.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom VFX")) tags.Add("Custom VFX");
                            }
                        }

                        if (metaRoot.TryGetProperty("EngineVersion", out var ev) && !string.IsNullOrEmpty(ev.GetString()))
                        {
                            engineVersion = ev.GetString()!;
                        }
                        else if (metaRoot.TryGetProperty("GameBuildNumber", out var gb) && !string.IsNullOrEmpty(gb.GetString()))
                        {
                            engineVersion = $"Build {gb.GetString()}";
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
