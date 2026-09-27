using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Shared;

namespace Realm.Shared.ModelOptimization;

public static unsafe class GlbLodGenerator
{
	public static readonly float[] DefaultLodRatios = new float[] { 1.0f, 0.50f, 0.25f, 0.10f };
	public static readonly float[] DefaultVisibilityBegins = new float[] { 0f, 45f, 90f, 150f };
	public static readonly float[] DefaultVisibilityEnds = new float[] { 45f, 90f, 150f, 0f };

	public static (bool Success, byte[] OutputGlbBytes, string ErrorMessage) GenerateLods(
		byte[] inputGlbBytes,
		float[]? lodRatios = null,
		float[]? visBegins = null,
		float[]? visEnds = null)
	{
		if (inputGlbBytes == null || inputGlbBytes.Length < 20)
		{
			return (false, inputGlbBytes ?? Array.Empty<byte>(), "Invalid GLB buffer");
		}

		if (lodRatios == null || visBegins == null || visEnds == null ||
			lodRatios.Length < 2 || visBegins.Length != lodRatios.Length || visEnds.Length != lodRatios.Length ||
			!IsDecreasingRatios(lodRatios))
		{
			lodRatios = DefaultLodRatios;
			visBegins = DefaultVisibilityBegins;
			visEnds = DefaultVisibilityEnds;
		}

		try
		{
			var (jsonNode, binBytes, glbVer) = GlbManifestUtils.ParseGlb(inputGlbBytes);
			if (jsonNode is not JsonObject root)
			{
				return (false, inputGlbBytes, "Failed to parse glTF JSON root");
			}

			if (root["extensionsUsed"] is JsonArray extUsedEarly)
			{
				for (int i = extUsedEarly.Count - 1; i >= 0; i--)
				{
					if (extUsedEarly[i]?.GetValue<string>() == "MSFT_lod")
					{
						extUsedEarly.RemoveAt(i);
					}
				}
			}
			if (root["extensionsRequired"] is JsonArray extReqEarly)
			{
				for (int i = extReqEarly.Count - 1; i >= 0; i--)
				{
					if (extReqEarly[i]?.GetValue<string>() == "MSFT_lod")
					{
						extReqEarly.RemoveAt(i);
					}
				}
			}
			if (root["nodes"] is JsonArray existingNodes)
			{
				var removedIndices = new HashSet<int>();
				for (int i = existingNodes.Count - 1; i >= 0; i--)
				{
					if (existingNodes[i] is JsonObject nodeObj)
					{
						string nodeName = nodeObj["name"]?.GetValue<string>() ?? string.Empty;
						if (nodeName.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase) ||
							nodeName.EndsWith("_LOD2", StringComparison.OrdinalIgnoreCase) ||
							nodeName.EndsWith("_LOD3", StringComparison.OrdinalIgnoreCase))
						{
							existingNodes.RemoveAt(i);
							removedIndices.Add(i);
							continue;
						}
						if (nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
						{
							nodeObj["name"] = nodeName.Substring(0, nodeName.Length - 5);
						}
						if (nodeObj.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject nodeExts)
						{
							nodeExts.Remove("MSFT_lod");
						}
					}
				}

				if (root["scenes"] is JsonArray earlyScenes)
				{
					foreach (var sc in earlyScenes)
					{
						if (sc is JsonObject scObj && scObj["nodes"] is JsonArray scNodes)
						{
							var cleanNodes = new JsonArray();
							var seen = new HashSet<int>();
							foreach (var item in scNodes)
							{
								if (item is JsonValue jv && jv.TryGetValue(out int nodeIdx) && !removedIndices.Contains(nodeIdx) && nodeIdx >= 0 && nodeIdx < existingNodes.Count && seen.Add(nodeIdx))
								{
									cleanNodes.Add(nodeIdx);
								}
							}
							scObj["nodes"] = cleanNodes;
						}
					}
				}
			}

			if (root["meshes"] is not JsonArray meshes || meshes.Count == 0 ||
				root["accessors"] is not JsonArray accessors ||
				root["bufferViews"] is not JsonArray bufferViews ||
				root["buffers"] is not JsonArray buffers)
			{
				return (true, inputGlbBytes, string.Empty);
			}

			binBytes ??= Array.Empty<byte>();
			using var newBinStream = new MemoryStream();
			newBinStream.Write(binBytes, 0, binBytes.Length);

			var meshLodMap = new Dictionary<int, List<int>>(); // origMeshIdx -> list of LOD mesh indices [LOD1, LOD2, LOD3]

			for (int m = 0; m < meshes.Count; m++)
			{
				if (meshes[m] is not JsonObject meshObj) continue;
				if (meshObj["primitives"] is not JsonArray primitives || primitives.Count == 0) continue;

				string meshName = meshObj["name"]?.GetValue<string>() ?? $"Mesh_{m}";
				if (meshName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
				{
					meshName = meshName.Substring(0, meshName.Length - 5);
				}
				else if (meshName.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase))
				{
					meshName = meshName.Substring(0, meshName.Length - 4);
				}
				else if (meshName.Contains("_LOD", StringComparison.OrdinalIgnoreCase) ||
					meshName.Contains("LOD1", StringComparison.OrdinalIgnoreCase) ||
					meshName.Contains("LOD2", StringComparison.OrdinalIgnoreCase) ||
					meshName.Contains("LOD3", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var lodMeshIndices = new List<int>();

				for (int t = 1; t < lodRatios.Length; t++)
				{
					float ratio = lodRatios[t];
					var newPrimitives = new JsonArray();

					for (int p = 0; p < primitives.Count; p++)
					{
						if (primitives[p] is not JsonObject primObj) continue;

						// Only process standard TRIANGLES (mode 4 or default/omitted)
						if (primObj.TryGetPropertyValue("mode", out var modeVal) && modeVal != null && modeVal.GetValue<int>() != 4)
						{
							newPrimitives.Add(primObj.DeepClone());
							continue;
						}

						// Do not attempt manual decimation on compressed primitives (Draco, Meshopt, etc.)
						if (primObj.ContainsKey("extensions") && primObj["extensions"] != null)
						{
							newPrimitives.Add(primObj.DeepClone());
							continue;
						}

						if (primObj["attributes"] is not JsonObject attributes) continue;
						if (!attributes.ContainsKey("POSITION")) continue;

						int posAccIdx = attributes["POSITION"]!.GetValue<int>();
						if (posAccIdx < 0 || posAccIdx >= accessors.Count) continue;
						var posAcc = accessors[posAccIdx] as JsonObject;
						if (posAcc == null || posAcc.ContainsKey("sparse")) continue;

						string posType = posAcc["type"]?.GetValue<string>() ?? string.Empty;
						int posCompType = posAcc["componentType"]?.GetValue<int>() ?? 0;
						if (posType != "VEC3" || posCompType != 5126) continue;

						int posBvIdx = posAcc["bufferView"]?.GetValue<int>() ?? -1;
						if (posBvIdx < 0 || posBvIdx >= bufferViews.Count) continue;
						var posBv = bufferViews[posBvIdx] as JsonObject;
						if (posBv == null) continue;

						int posByteOffset = (posAcc["byteOffset"]?.GetValue<int>() ?? 0) +
											(posBv["byteOffset"]?.GetValue<int>() ?? 0);
						int posCount = posAcc["count"]?.GetValue<int>() ?? 0;
						int posStride = posBv["byteStride"]?.GetValue<int>() ?? 12;

						if (posByteOffset < 0 || posCount < 3 || posStride < 12 || (posStride % 4) != 0 || posStride > 256)
							continue;

						if ((long)posByteOffset + ((long)posCount * posStride) > binBytes.Length)
							continue;

						uint[] originalIndices;
						if (primObj.ContainsKey("indices"))
						{
							int indAccIdx = primObj["indices"]!.GetValue<int>();
							if (indAccIdx < 0 || indAccIdx >= accessors.Count) continue;
							var indAcc = accessors[indAccIdx] as JsonObject;
							if (indAcc == null || indAcc.ContainsKey("sparse")) continue;

							string indType = indAcc["type"]?.GetValue<string>() ?? string.Empty;
							if (indType != "SCALAR") continue;

							int indBvIdx = indAcc["bufferView"]?.GetValue<int>() ?? -1;
							if (indBvIdx < 0 || indBvIdx >= bufferViews.Count) continue;
							var indBv = bufferViews[indBvIdx] as JsonObject;
							if (indBv == null) continue;

							int indByteOffset = (indAcc["byteOffset"]?.GetValue<int>() ?? 0) +
												(indBv["byteOffset"]?.GetValue<int>() ?? 0);
							int indCount = indAcc["count"]?.GetValue<int>() ?? 0;
							int componentType = indAcc["componentType"]?.GetValue<int>() ?? 0;

							if (indByteOffset < 0 || indCount < 3 || (indCount % 3) != 0) continue;

							int elemSize = componentType switch
							{
								5121 => 1,
								5123 => 2,
								5125 => 4,
								_ => 0
							};
							if (elemSize == 0 || (long)indByteOffset + ((long)indCount * elemSize) > binBytes.Length) continue;

							originalIndices = new uint[indCount];
							if (componentType == 5123) // UNSIGNED_SHORT
							{
								for (int i = 0; i < indCount; i++)
								{
									originalIndices[i] = BitConverter.ToUInt16(binBytes, indByteOffset + (i * 2));
								}
							}
							else if (componentType == 5125) // UNSIGNED_INT
							{
								for (int i = 0; i < indCount; i++)
								{
									originalIndices[i] = BitConverter.ToUInt32(binBytes, indByteOffset + (i * 4));
								}
							}
							else if (componentType == 5121) // UNSIGNED_BYTE
							{
								for (int i = 0; i < indCount; i++)
								{
									originalIndices[i] = binBytes[indByteOffset + i];
								}
							}
							else
							{
								continue;
							}
						}
						else
						{
							if (posCount % 3 != 0) continue;
							originalIndices = new uint[posCount];
							for (uint i = 0; i < posCount; i++) originalIndices[i] = i;
						}

						if (originalIndices.Length < 12)
						{
							// Too small to simplify, clone primitive as is
							newPrimitives.Add(primObj.DeepClone());
							continue;
						}

						// Validate that all indices are within posCount bounds
						bool hasInvalidIndex = false;
						for (int i = 0; i < originalIndices.Length; i++)
						{
							if (originalIndices[i] >= (uint)posCount)
							{
								hasInvalidIndex = true;
								break;
							}
						}
						if (hasInvalidIndex)
						{
							newPrimitives.Add(primObj.DeepClone());
							continue;
						}

						int targetIndexCount = (int)(originalIndices.Length * ratio) / 3 * 3;
						if (targetIndexCount < 3 || targetIndexCount >= originalIndices.Length)
						{
							newPrimitives.Add(primObj.DeepClone());
							continue;
						}

						uint[] simplifiedIndices = new uint[originalIndices.Length];
						nuint simplifiedCount = 0;

						fixed (uint* pOrig = originalIndices)
						fixed (uint* pDest = simplifiedIndices)
						fixed (byte* pBin = binBytes)
						{
							float* pPos = (float*)(pBin + posByteOffset);
							float resultError = 0f;

							simplifiedCount = MeshOptimizerNative.meshopt_simplify(
								pDest,
								pOrig,
								(nuint)originalIndices.Length,
								pPos,
								(nuint)posCount,
								(nuint)posStride,
								(nuint)targetIndexCount,
								0.02f * t,
								0,
								&resultError);

							if (simplifiedCount >= (nuint)originalIndices.Length || simplifiedCount < 3)
							{
								simplifiedCount = MeshOptimizerNative.meshopt_simplifySloppy(
									pDest,
									pOrig,
									(nuint)originalIndices.Length,
									pPos,
									(nuint)posCount,
									(nuint)posStride,
									null,
									(nuint)targetIndexCount,
									0.05f * t,
									&resultError);
							}

							if (simplifiedCount >= 3 && (simplifiedCount % 3) == 0 && simplifiedCount < (nuint)originalIndices.Length)
							{
								MeshOptimizerNative.meshopt_optimizeVertexCache(pDest, pDest, simplifiedCount, (nuint)posCount);
								MeshOptimizerNative.meshopt_optimizeOverdraw(pDest, pDest, simplifiedCount, pPos, (nuint)posCount, (nuint)posStride, 1.05f);
							}
						}

						if (simplifiedCount < 3 || (simplifiedCount % 3) != 0 || simplifiedCount >= (nuint)originalIndices.Length)
						{
							newPrimitives.Add(primObj.DeepClone());
							continue;
						}

						// Align BIN stream to 4 bytes
						while ((newBinStream.Position % 4) != 0)
						{
							newBinStream.WriteByte(0);
						}

						uint maxSimplifiedIdx = 0;
						for (int i = 0; i < (int)simplifiedCount; i++)
						{
							if (simplifiedIndices[i] > maxSimplifiedIdx)
							{
								maxSimplifiedIdx = simplifiedIndices[i];
							}
						}

						int newIndByteOffset = (int)newBinStream.Position;
						bool useShort = maxSimplifiedIdx <= 65535;
						int newIndByteLength = (int)simplifiedCount * (useShort ? 2 : 4);

						for (int i = 0; i < (int)simplifiedCount; i++)
						{
							if (useShort)
							{
								byte[] bytes = BitConverter.GetBytes((ushort)simplifiedIndices[i]);
								newBinStream.Write(bytes, 0, 2);
							}
							else
							{
								byte[] bytes = BitConverter.GetBytes(simplifiedIndices[i]);
								newBinStream.Write(bytes, 0, 4);
							}
						}

						// Add bufferView for indices
						int newBvIdx = bufferViews.Count;
						var newBv = new JsonObject
						{
							["buffer"] = 0,
							["byteOffset"] = newIndByteOffset,
							["byteLength"] = newIndByteLength,
							["target"] = 34963 // ELEMENT_ARRAY_BUFFER
						};
						bufferViews.Add(newBv);

						// Add accessor for indices
						int newAccIdx = accessors.Count;
						var newAcc = new JsonObject
						{
							["bufferView"] = newBvIdx,
							["byteOffset"] = 0,
							["componentType"] = useShort ? 5123 : 5125,
							["count"] = (int)simplifiedCount,
							["type"] = "SCALAR"
						};
						accessors.Add(newAcc);

						var newPrim = new JsonObject
						{
							["attributes"] = primObj["attributes"]!.DeepClone(),
							["indices"] = newAccIdx
						};
						if (primObj.ContainsKey("material"))
						{
							newPrim["material"] = primObj["material"]!.GetValue<int>();
						}

						newPrimitives.Add(newPrim);
					}

					if (newPrimitives.Count > 0)
					{
						int newMeshIdx = meshes.Count;
						var newMesh = new JsonObject
						{
							["name"] = $"{meshName}_LOD{t}",
							["primitives"] = newPrimitives
						};
						meshes.Add(newMesh);
						lodMeshIndices.Add(newMeshIdx);
					}
				}

				if (lodMeshIndices.Count > 0)
				{
					meshLodMap[m] = lodMeshIndices;
					meshObj["name"] = $"{meshName}_LOD0";
				}
			}

			// Align final BIN stream to 4 bytes
			while ((newBinStream.Position % 4) != 0)
			{
				newBinStream.WriteByte(0);
			}

			byte[] finalBinBytes = newBinStream.ToArray();
			if (buffers[0] is JsonObject buf0)
			{
				buf0["byteLength"] = finalBinBytes.Length;
			}

			// Add MSFT_lod to extensionsUsed
			if (root["extensionsUsed"] is not JsonArray extUsed)
			{
				extUsed = new JsonArray();
				root["extensionsUsed"] = extUsed;
			}
			bool hasMsftLod = false;
			foreach (var item in extUsed)
			{
				if (item?.ToString() == "MSFT_lod") { hasMsftLod = true; break; }
			}
			if (!hasMsftLod)
			{
				extUsed.Add("MSFT_lod");
			}

			// Update scene nodes with LOD siblings & MSFT_lod
			if (root["nodes"] is JsonArray nodes && nodes.Count > 0)
			{
				int initialNodeCount = nodes.Count;
				for (int n = 0; n < initialNodeCount; n++)
				{
					if (nodes[n] is not JsonObject nodeObj) continue;
					if (!nodeObj.ContainsKey("mesh")) continue;

					int meshIdx = nodeObj["mesh"]!.GetValue<int>();
					if (meshLodMap.TryGetValue(meshIdx, out var lodMeshesList))
					{
						string nodeName = nodeObj["name"]?.GetValue<string>() ?? $"Node_{n}";
						if (nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
						{
							nodeName = nodeName.Substring(0, nodeName.Length - 5);
						}
						nodeObj["name"] = $"{nodeName}_LOD0";

						var lodNodeIndices = new JsonArray();

						for (int t = 0; t < lodMeshesList.Count; t++)
						{
							int lodMeshIdx = lodMeshesList[t];
							int lodTier = t + 1;

							var lodNode = new JsonObject
							{
								["name"] = $"{nodeName}_LOD{lodTier}",
								["mesh"] = lodMeshIdx
							};

							if (nodeObj.ContainsKey("skin")) lodNode["skin"] = nodeObj["skin"]!.GetValue<int>();
							if (nodeObj.ContainsKey("matrix")) lodNode["matrix"] = nodeObj["matrix"]!.DeepClone();
							if (nodeObj.ContainsKey("translation")) lodNode["translation"] = nodeObj["translation"]!.DeepClone();
							if (nodeObj.ContainsKey("rotation")) lodNode["rotation"] = nodeObj["rotation"]!.DeepClone();
							if (nodeObj.ContainsKey("scale")) lodNode["scale"] = nodeObj["scale"]!.DeepClone();

							var lodExtras = new JsonObject
							{
								["visibility_range_begin"] = visBegins[lodTier],
								["visibility_range_end"] = visEnds[lodTier]
							};
							lodNode["extras"] = lodExtras;

							int newLodNodeIdx = nodes.Count;
							nodes.Add(lodNode);
							lodNodeIndices.Add(newLodNodeIdx);
						}

						if (nodeObj["extensions"] is not JsonObject nodeExts)
						{
							nodeExts = new JsonObject();
							nodeObj["extensions"] = nodeExts;
						}
						nodeExts["MSFT_lod"] = new JsonObject
						{
							["ids"] = lodNodeIndices
						};

						if (nodeObj["extras"] is not JsonObject nExtras)
						{
							nExtras = new JsonObject();
							nodeObj["extras"] = nExtras;
						}
						nExtras["visibility_range_begin"] = visBegins[0];
						nExtras["visibility_range_end"] = visEnds[0];
					}
				}
			}

			// Final sanity check: ensure no duplicate or out-of-bounds node indices in any scene
			if (root["scenes"] is JsonArray finalScenes && root["nodes"] is JsonArray finalNodes)
			{
				foreach (var sc in finalScenes)
				{
					if (sc is JsonObject scObj && scObj["nodes"] is JsonArray scNodes)
					{
						var distinctNodes = new JsonArray();
						var seen = new HashSet<int>();
						foreach (var item in scNodes)
						{
							if (item is JsonValue jv && jv.TryGetValue(out int nodeIdx) && nodeIdx >= 0 && nodeIdx < finalNodes.Count && seen.Add(nodeIdx))
							{
								distinctNodes.Add(nodeIdx);
							}
						}
						scObj["nodes"] = distinctNodes;
					}
				}
			}

			byte[] outputGlb = GlbManifestUtils.BuildGlb(root, finalBinBytes, glbVer);
			return (true, outputGlb, string.Empty);
		}
		catch (Exception ex)
		{
			return (false, inputGlbBytes, $"Failed to generate LODs: {ex.Message}");
		}
	}

	private static bool IsDecreasingRatios(float[] ratios)
	{
		for (int i = 0; i < ratios.Length; i++)
		{
			if (float.IsNaN(ratios[i]) || float.IsInfinity(ratios[i]) || ratios[i] <= 0f || ratios[i] > 1.0f)
				return false;
			if (i > 0 && ratios[i] >= ratios[i - 1])
				return false;
		}
		return true;
	}
}
