using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using LiteDB;
using Realm.Client.Network;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Walking the two map archives and recording what they contain: every global manifest is registered in
///     the index and its entries collected, the rows in the store's own directory that no manifest mentioned
///     (or whose file is gone) are dropped, and then every P2P manifest is registered the same way. The
///     register step and the index to write to are supplied by the caller, so this stays independent of how
///     the service resolves a target and takes its P2P lock.
/// </summary>
public static class AssetIndexArchiveScans
{
	public static void ScanAll(
		Func<AssetIndexTarget> globalTarget,
		Action<MapManifest, string, bool> registerManifest,
		object syncLock,
		IProgress<AssetIndexProgressUpdate>? progress)
	{
		var referencedCasPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		ScanGlobalManifests(globalTarget, referencedCasPaths, progress);
		CleanupOrphanedCasData(globalTarget(), syncLock, referencedCasPaths);
		ScanP2PManifests(registerManifest, progress);
	}

	private static void ScanGlobalManifests(Func<AssetIndexTarget> globalTarget, HashSet<string> referencedCasPaths, IProgress<AssetIndexProgressUpdate>? progress)
	{
		if (!Directory.Exists(MapAssetManager.GlobalArchiveDirectory)) return;

		var manifestFiles = Directory.GetFiles(MapAssetManager.GlobalArchiveDirectory, "manifest.json", SearchOption.AllDirectories);
		for (int i = 0; i < manifestFiles.Length; i++)
		{
			ProcessGlobalManifestFile(globalTarget, manifestFiles[i], i, manifestFiles.Length, referencedCasPaths, progress);
		}
	}

	private static void ProcessGlobalManifestFile(Func<AssetIndexTarget> globalTarget, string file, int index, int total, HashSet<string> referencedCasPaths, IProgress<AssetIndexProgressUpdate>? progress)
	{
		try
		{
			var manifest = MapManifest.LoadFromFile(file);
			if (manifest?.Files == null || manifest.Files.Count == 0) return;

			string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName : Path.GetFileName(Path.GetDirectoryName(file)) ?? "Map";
			double pct = 0.20 + 0.65 * ((double)index / Math.Max(1, total));
			progress?.Report(new AssetIndexProgressUpdate(pct, string.Format(TranslationServer.Translate("Indexing manifest {0}/{1}: {2}..."), index + 1, total, mapName)));

			var paths = AssetIndexManifestRegistration.Register(globalTarget(), manifest, file, isP2P: false);
			foreach (var p in paths)
			{
				referencedCasPaths.Add(p);
			}
		}
		catch
		{
		}
	}

	private static void CleanupOrphanedCasData(AssetIndexTarget target, object syncLock, HashSet<string> referencedCasPaths)
	{
		lock (syncLock)
		{
			var orphanedCasAssets = target.Assets.Find(Query.EQ("DirectoryPath", target.StorageDirectory))
				.Where(a => !referencedCasPaths.Contains(a.FilePath) || !File.Exists(a.FilePath))
				.Select(a => (BsonValue)a.Id)
				.ToArray();

			if (orphanedCasAssets.Length > 0)
			{
				target.Assets.DeleteMany(Query.In("_id", orphanedCasAssets));
			}

			var orphanedPackages = target.Packages.FindAll()
				.Where(p => !p.IsP2P && (string.IsNullOrWhiteSpace(p.ManifestPath) || !File.Exists(p.ManifestPath)))
				.Select(p => (BsonValue)p.Id)
				.ToArray();

			if (orphanedPackages.Length > 0)
			{
				target.Packages.DeleteMany(Query.In("_id", orphanedPackages));
			}

			target.Database.Checkpoint();
		}
	}

	private static void ScanP2PManifests(Action<MapManifest, string, bool> registerManifest, IProgress<AssetIndexProgressUpdate>? progress)
	{
		if (!Directory.Exists(MapAssetManager.P2PArchiveDirectory)) return;

		var p2pManifestFiles = Directory.GetFiles(MapAssetManager.P2PArchiveDirectory, "manifest.json", SearchOption.AllDirectories);
		for (int j = 0; j < p2pManifestFiles.Length; j++)
		{
			ProcessP2PManifestFile(registerManifest, p2pManifestFiles[j], j, p2pManifestFiles.Length, progress);
		}
	}

	private static void ProcessP2PManifestFile(Action<MapManifest, string, bool> registerManifest, string file, int index, int total, IProgress<AssetIndexProgressUpdate>? progress)
	{
		try
		{
			var manifest = MapManifest.LoadFromFile(file);
			if (manifest?.Files == null || manifest.Files.Count == 0) return;

			double pct = 0.85 + 0.12 * ((double)index / Math.Max(1, total));
			progress?.Report(new AssetIndexProgressUpdate(pct, string.Format(TranslationServer.Translate("Indexing P2P archive {0}/{1}..."), index + 1, total)));
			registerManifest(manifest, file, true);
		}
		catch
		{
		}
	}
}
