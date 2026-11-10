// Exporting one map version to a file: the button's busy state, the destination and file name, the
// progress popup, and the per-chunk progress updates that drive it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using Environment = System.Environment;
using static Godot.Control;
using static Godot.CanvasItem;


namespace Realm.Client.UI;

public partial class StorageMenu
{
	private void OnExportMapVersionPressed(DownloadedMapInfo map, DownloadedMapVersionInfo version, Button exportBtn)
	{
		if (map == null || version == null)
		{
			return;
		}

		string defaultFileName = GenerateMapExportFileName(map, version);

		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Export Map as .rmap"),
			OS.GetSystemDir(OS.SystemDir.Documents),
			defaultFileName,
			false,
			DisplayServer.FileDialogMode.SaveFile,
			new[] { "*.rmap ; Realm Map Package (*.rmap)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (!status || selectedPaths.Length == 0)
				{
					return;
				}

				string destinationPath = selectedPaths[0];
				if (!destinationPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase))
				{
					destinationPath += ".rmap";
				}

				_ = ExportMapToFileAsync(map, version, destinationPath, exportBtn);
			})
		);

		if (err != Error.Ok)
		{
			string fallbackPath = Path.Combine(OS.GetSystemDir(OS.SystemDir.Documents), defaultFileName);
			_ = ExportMapToFileAsync(map, version, fallbackPath, exportBtn);
		}
	}

	private string GenerateMapExportFileName(DownloadedMapInfo map, DownloadedMapVersionInfo version)
	{
		string cleanMapName = GetCleanMapName(map.Title);
		string cleanMapVersion = GetCleanMapVersion(version.Version);
		string shortHash = GetShortManifestHash(version);

		return $"{cleanMapName}_{cleanMapVersion}_{shortHash}.rmap";
	}

	private string GetCleanMapName(string mapTitle)
	{
		string cleanMapName = string.Join("_", mapTitle.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanMapName) || MapNaming.IsUntitledMapName(cleanMapName))
		{
			return "MapExport";
		}

		return cleanMapName;
	}

	private string GetCleanMapVersion(string mapVersion)
	{
		string cleanMapVersion = string.Join("_", mapVersion.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanMapVersion))
		{
			return "1.0.0";
		}

		return cleanMapVersion;
	}

	private string GetShortManifestHash(DownloadedMapVersionInfo version)
	{
		string manifestBlake3 = version.ManifestHash;
		if (string.IsNullOrEmpty(manifestBlake3) && File.Exists(version.ManifestFilePath))
		{
			manifestBlake3 = RealmMetadataHelper.ComputeBlake3(version.ManifestFilePath);
		}

		if (string.IsNullOrEmpty(manifestBlake3) && !string.IsNullOrEmpty(version.DirectoryPath))
		{
			string manifestPath = Path.Combine(version.DirectoryPath, "manifest.json");
			if (File.Exists(manifestPath))
			{
				manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestPath);
			}
		}

		string normHash = !string.IsNullOrEmpty(manifestBlake3) ? ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3) : string.Empty;
		return normHash.Length >= 4 ? normHash.Substring(0, 4) : (normHash.Length > 0 ? normHash : "0000");
	}

	private async Task ExportMapToFileAsync(DownloadedMapInfo map, DownloadedMapVersionInfo version, string destinationPath, Button exportBtn)
	{
		destinationPath = ResolveExportDestinationPath(map, version, destinationPath);

		SetExportButtonState(exportBtn, true, "Exporting...");

		(Panel popup, ProgressBar progressBar, Label statusLabel, Button closeBtn) = ShowExportProgressUI(destinationPath);

		WorkspaceMetadataBootstrapper.EnsureLicenseFile(version.DirectoryPath);

		long lastProgressUpdateTicks = 0;
		bool success = await GameContext.MapStorageService.ExportMapAsync(
			version.DirectoryPath,
			destinationPath,
			progressCallback: (pct, file) => UpdateExportProgress(pct, file, ref lastProgressUpdateTicks, popup, progressBar, statusLabel),
			compressionLevel: 1
		);

		SetExportButtonState(exportBtn, false, "📦 " + TranslationServer.Translate("EXPORT MAP"));

		FinishExportProgressUI(success, progressBar, statusLabel, closeBtn);
	}

	private string ResolveExportDestinationPath(DownloadedMapInfo map, DownloadedMapVersionInfo version, string destinationPath)
	{
		if (!Directory.Exists(destinationPath) || map == null || version == null)
		{
			return destinationPath;
		}

		string fileName = GenerateMapExportFileName(map, version);
		return Path.Combine(destinationPath, fileName);
	}

	private void SetExportButtonState(Button exportBtn, bool disabled, string text)
	{
		if (!GodotObject.IsInstanceValid(exportBtn))
		{
			return;
		}

		exportBtn.Disabled = disabled;
		UIStyle.ApplyButtonText(exportBtn, TranslationServer.Translate(text), 12);
	}

	private (Panel popup, ProgressBar progressBar, Label statusLabel, Button closeBtn) ShowExportProgressUI(string destinationPath)
	{
		return UIStyle.CreateExportUI(this, destinationPath);
	}

	private void UpdateExportProgress(float pct, string file, ref long lastProgressUpdateTicks, Panel popup, ProgressBar progressBar, Label statusLabel)
	{
		long now = Environment.TickCount64;
		if (now - lastProgressUpdateTicks < 50 && pct < 1.0f)
		{
			return;
		}

		lastProgressUpdateTicks = now;

		Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(popup)) return;
			progressBar.Value = pct * 100;
			statusLabel.Text = string.Format(TranslationServer.Translate("Compressing package ({0}%): {1}..."), (int)(pct * 100), Path.GetFileName(file));
		}).CallDeferred();
	}

	private void FinishExportProgressUI(bool success, ProgressBar progressBar, Label statusLabel, Button closeBtn)
	{
		progressBar.Value = 100;
		closeBtn.Visible = true;

		if (success)
		{
			statusLabel.Text = "✓ " + TranslationServer.Translate("Map exported successfully!");
			statusLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 0.5f));
		}
		else
		{
			statusLabel.Text = "❌ " + TranslationServer.Translate("Map export failed.");
			statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
		}
	}

}